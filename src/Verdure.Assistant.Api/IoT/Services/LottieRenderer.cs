using SkiaSharp;
using SkiaSharp.Skottie;
using Verdure.Assistant.Api.IoT.Interfaces;

namespace Verdure.Assistant.Api.IoT.Services;

/// <summary>
/// Lottie动画渲染器
/// </summary>
public class LottieRenderer : ILottieRenderer
{
    private readonly string _filePath;
    private readonly Animation? _animation;
    private readonly ILogger<LottieRenderer>? _logger;
    private bool _disposed = false;

    public LottieRenderer(string filePath, ILogger<LottieRenderer>? logger = null)
    {
        _filePath = filePath;
        _logger = logger;

        try
        {
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                _animation = Animation.Create(json);
                
                if (_animation != null)
                {
                    _logger?.LogDebug($"Lottie动画加载成功: {filePath}, 帧数: {FrameCount}");
                }
                else
                {
                    _logger?.LogError($"Lottie动画解析失败: {filePath}");
                }
            }
            else
            {
                _logger?.LogError($"Lottie文件不存在: {filePath}");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, $"加载Lottie动画失败: {filePath}");
        }
    }

    /// <summary>
    /// 获取动画帧数
    /// </summary>
    public uint FrameCount => _animation?.Duration.TotalMilliseconds > 0 
        ? (uint)(_animation.Duration.TotalMilliseconds / (1000.0 / 30.0)) // 假设30fps
        : 0;

    /// <summary>
    /// 渲染指定帧
    /// </summary>
    public byte[] RenderFrame(int frameIndex, int width, int height)
    {
        if (_animation == null || _disposed)
        {
            return CreateEmptyFrame(width, height);
        }

        try
        {
            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            using var canvas = surface.Canvas;

            // 清除背景
            canvas.Clear(SKColors.Black);

            // 计算时间进度
            double progress = Math.Min(1.0, (double)frameIndex / Math.Max(1, FrameCount));
            var timeProgress = progress * _animation.Duration.TotalSeconds;

            // 渲染动画帧
            _animation.SeekFrame(timeProgress);
            _animation.Render(canvas, new SKRect(0, 0, width, height));

            // 获取图像并转换为RGB565
            using var image = surface.Snapshot();
            using var pixmap = image.PeekPixels();

            return ConvertToRgb565(pixmap, width, height);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, $"渲染Lottie帧失败: frame={frameIndex}");
            return CreateEmptyFrame(width, height);
        }
    }

    /// <summary>
    /// 重置播放状态
    /// </summary>
    public void ResetPlayback()
    {
        if (_animation != null && !_disposed)
        {
            try
            {
                _animation.SeekFrame(0.0);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "重置Lottie播放状态失败");
            }
        }
    }

    /// <summary>
    /// 将SkiaSharp像素转换为RGB565格式
    /// </summary>
    private byte[] ConvertToRgb565(SKPixmap pixmap, int width, int height)
    {
        byte[] buffer = new byte[width * height * 2]; // 16位/像素

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                SKColor color = pixmap.GetPixelColor(x, y);

                // 转换为RGB565格式
                int r = color.Red >> 3;
                int g = color.Green >> 2;
                int b = color.Blue >> 3;
                ushort rgb565 = (ushort)(r << 11 | g << 5 | b);

                // 存储为大端序
                int pos = (y * width + x) * 2;
                buffer[pos] = (byte)(rgb565 >> 8);
                buffer[pos + 1] = (byte)(rgb565 & 0xFF);
            }
        }

        return buffer;
    }

    /// <summary>
    /// 创建空白帧
    /// </summary>
    private byte[] CreateEmptyFrame(int width, int height)
    {
        return new byte[width * height * 2]; // 全黑帧
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            _animation?.Dispose();
            _logger?.LogDebug($"Lottie渲染器已释放: {_filePath}");
            _disposed = true;
        }
    }
}