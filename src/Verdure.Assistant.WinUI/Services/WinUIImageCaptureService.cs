using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Interfaces;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;

namespace Verdure.Assistant.WinUI.Services;

/// <summary>
/// 图片编码辅助：把任意图片/位图缩放到适合上传的大小并编码为 JPEG
/// </summary>
public static class ImageEncoding
{
    public const int DefaultMaxEdge = 1280;

    public static async Task<CapturedImage> EncodeJpegAsync(SoftwareBitmap bitmap, int maxEdge = DefaultMaxEdge, double quality = 0.85)
    {
        var width = bitmap.PixelWidth;
        var height = bitmap.PixelHeight;
        var scale = Math.Min(1.0, maxEdge / (double)Math.Max(width, height));
        var targetWidth = Math.Max(1, (int)Math.Round(width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(height * scale));

        using var output = new InMemoryRandomAccessStream();
        var props = new BitmapPropertySet
        {
            { "ImageQuality", new BitmapTypedValue(quality, Windows.Foundation.PropertyType.Single) }
        };
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, output, props);
        encoder.SetSoftwareBitmap(bitmap);
        if (scale < 1.0)
        {
            encoder.BitmapTransform.ScaledWidth = (uint)targetWidth;
            encoder.BitmapTransform.ScaledHeight = (uint)targetHeight;
            encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;
        }

        await encoder.FlushAsync();

        var bytes = new byte[output.Size];
        output.Seek(0);
        using var reader = new DataReader(output);
        await reader.LoadAsync((uint)output.Size);
        reader.ReadBytes(bytes);
        return new CapturedImage(bytes, "image/jpeg", targetWidth, targetHeight);
    }

    /// <summary>解码任意格式的图片流（含 EXIF 方向）并重新编码为缩放后的 JPEG</summary>
    public static async Task<CapturedImage> FromStreamAsync(IRandomAccessStream stream, int maxEdge = DefaultMaxEdge)
    {
        var decoder = await BitmapDecoder.CreateAsync(stream);
        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, new BitmapTransform(),
            ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
        return await EncodeJpegAsync(bitmap, maxEdge);
    }
}

/// <summary>
/// WinUI 的取图服务：摄像头通过 <see cref="MediaCapture"/>（需要 webcam 能力与系统授权），屏幕通过 GDI 截取主显示器。
/// </summary>
public sealed class WinUIImageCaptureService : IImageCaptureService
{
    private readonly IUIDispatcher _ui;
    private readonly ILogger<WinUIImageCaptureService>? _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public WinUIImageCaptureService(IUIDispatcher ui, ILogger<WinUIImageCaptureService>? logger = null)
    {
        _ui = ui;
        _logger = logger;
    }

    public bool IsSupported(ImageCaptureSource source) => true;

    public async Task<CapturedImage> CaptureAsync(ImageCaptureSource source, CancellationToken cancellationToken = default)
    {
        // 同一时刻只允许一次取图，避免摄像头被重复初始化
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return source == ImageCaptureSource.Camera
                ? await CaptureCameraAsync(cancellationToken).ConfigureAwait(false)
                : await Task.Run(CaptureScreenAsync, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    #region 摄像头

    private async Task<CapturedImage> CaptureCameraAsync(CancellationToken ct)
    {
        var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
        if (devices.Count == 0)
        {
            throw new ImageCaptureException("没有找到摄像头，请确认摄像头已连接并且没有被禁用");
        }

        // MediaCapture 需要在带消息循环的线程上初始化
        return await _ui.InvokeAsync(async () =>
        {
            ct.ThrowIfCancellationRequested();
            using var capture = new MediaCapture();
            try
            {
                await capture.InitializeAsync(new MediaCaptureInitializationSettings
                {
                    StreamingCaptureMode = StreamingCaptureMode.Video,
                    PhotoCaptureSource = PhotoCaptureSource.Auto,
                    MemoryPreference = MediaCaptureMemoryPreference.Auto
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new ImageCaptureException("摄像头访问被拒绝：请在 Windows 设置 → 隐私和安全性 → 摄像头 中，允许应用和桌面应用访问摄像头", ex);
            }
            catch (Exception ex) when (ex is not ImageCaptureException)
            {
                throw new ImageCaptureException($"无法打开摄像头（可能正被其他程序占用）：{ex.Message}", ex);
            }

            // 刚打开的摄像头自动曝光/白平衡还没稳定，立刻拍会偏暗
            await Task.Delay(900, ct);

            using var stream = new InMemoryRandomAccessStream();
            await capture.CapturePhotoToStreamAsync(ImageEncodingProperties.CreateJpeg(), stream);
            stream.Seek(0);
            var image = await ImageEncoding.FromStreamAsync(stream);
            _logger?.LogInformation("摄像头拍照完成：{Width}x{Height}，{Kb}KB", image.Width, image.Height, image.Data.Length / 1024);
            return image;
        }).ConfigureAwait(false);
    }

    #endregion

    #region 屏幕

    private async Task<CapturedImage> CaptureScreenAsync()
    {
        var width = GetSystemMetrics(SM_CXSCREEN);
        var height = GetSystemMetrics(SM_CYSCREEN);
        if (width <= 0 || height <= 0)
        {
            throw new ImageCaptureException("无法获取屏幕尺寸");
        }

        var pixels = new byte[width * height * 4];
        var screenDc = GetDC(IntPtr.Zero);
        var memDc = CreateCompatibleDC(screenDc);
        var bitmap = CreateCompatibleBitmap(screenDc, width, height);
        var old = SelectObject(memDc, bitmap);

        try
        {
            if (!BitBlt(memDc, 0, 0, width, height, screenDc, 0, 0, SRCCOPY | CAPTUREBLT))
            {
                throw new ImageCaptureException("截取屏幕失败");
            }

            var info = new BITMAPINFO
            {
                biSize = 40,
                biWidth = width,
                biHeight = -height, // 自上而下
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            };

            if (GetDIBits(memDc, bitmap, 0, (uint)height, pixels, ref info, 0) == 0)
            {
                throw new ImageCaptureException("读取屏幕像素失败");
            }
        }
        finally
        {
            SelectObject(memDc, old);
            DeleteObject(bitmap);
            DeleteDC(memDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }

        using var software = SoftwareBitmap.CreateCopyFromBuffer(
            pixels.AsBuffer(), BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Ignore);
        var image = await ImageEncoding.EncodeJpegAsync(software, maxEdge: 1600).ConfigureAwait(false);
        _logger?.LogInformation("屏幕截图完成：{SrcW}x{SrcH} → {Width}x{Height}，{Kb}KB", width, height, image.Width, image.Height, image.Data.Length / 1024);
        return image;
    }

    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;
    private const uint SRCCOPY = 0x00CC0020;
    private const uint CAPTUREBLT = 0x40000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(IntPtr dc, IntPtr bmp, uint start, uint lines, byte[] bits, ref BITMAPINFO info, uint usage);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);

    #endregion
}
