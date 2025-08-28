using SkiaSharp;
using SkiaSharp.Resources;
using SkiaSharp.Skottie;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Verdure.Assistant.Api.Services.Robot;

public class LottieRenderer : IDisposable
{
    private readonly string _lottieFilePath;
    private SKData? _lottieData;
    private Animation? _animation;
    private double _totalFrames;
    private double _totalSeconds;
    private double _framerate;
    private Stopwatch _playbackTimer = new Stopwatch();
    private double _lastRenderTime = 0;
    private bool _disposed = false;

    // 播放控制参数
    public bool EnableFrameSkipping { get; set; } = true;  // 允许在延迟时跳过帧
    public bool EnableInterpolation { get; set; } = false; // 是否启用帧插值(需要额外计算资源)
    public double PlaybackRate { get; set; } = 1.0;        // 播放速率控制

    public LottieRenderer(string lottieFilePath)
    {
        _lottieFilePath = lottieFilePath;
        InitializeAnimation();
    }

    private void InitializeAnimation()
    {
        try
        {
            _lottieData = SKData.Create(_lottieFilePath);

            using var dataUriProvider = new DataUriResourceProvider(preDecode: true);

            _animation = Animation
                .CreateBuilder()
                .SetResourceProvider(dataUriProvider)
                .Build(_lottieData);

            if (_animation == null)
                throw new InvalidOperationException("无法加载Lottie动画");

            _totalFrames = (int)_animation.OutPoint;
            _totalSeconds = _animation.Duration.TotalSeconds;
            _framerate = _animation.Fps;
            _playbackTimer.Start();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Lottie 动画加载错误: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 渲染当前时间点的帧到位图
    /// </summary>
    public byte[] RenderCurrentFrame(int width, int height)
    {
        if (_animation == null)
            throw new InvalidOperationException("动画未初始化");

        // 基于当前实际时间计算应该显示的帧
        double elapsedSeconds = _playbackTimer.Elapsed.TotalSeconds * PlaybackRate;

        // 循环播放：对总时长取模
        double normalizedTime = elapsedSeconds % _totalSeconds;

        // 计算对应的时间点
        return RenderAtTimePoint(normalizedTime, width, height);
    }

    /// <summary>
    /// 渲染指定帧索引到位图(保留原方法以保持兼容性)
    /// </summary>
    public byte[] RenderFrame(int frameIndex, int width, int height)
    {
        if (_animation == null)
            throw new InvalidOperationException("动画未初始化");

        // 确保帧索引在有效范围内
        frameIndex = Math.Max(0, frameIndex % (int)_totalFrames);

        // 计算时间点
        double timePoint = frameIndex / _framerate;

        return RenderAtTimePoint(timePoint, width, height);
    }

    /// <summary>
    /// 在指定时间点渲染帧
    /// </summary>
    private byte[] RenderAtTimePoint(double timePoint, int width, int height)
    {
        // 检查是否需要跳过此帧(如果渲染速度跟不上，且启用了跳帧)
        if (EnableFrameSkipping && _lastRenderTime > 0)
        {
            double timeSinceLastRender = _playbackTimer.Elapsed.TotalSeconds - _lastRenderTime;
            double frameTime = 1.0 / _framerate;

            // 如果当前帧已经落后两帧以上，跳到最新的时间点
            if (timeSinceLastRender > frameTime * 2)
            {
                timePoint = _playbackTimer.Elapsed.TotalSeconds % _totalSeconds;
            }
        }

        // 更新最后渲染时间
        _lastRenderTime = _playbackTimer.Elapsed.TotalSeconds;

        // 创建适合目标屏幕的Surface
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        using var canvas = surface.Canvas;

        // 清除背景
        canvas.Clear(SKColors.Black);

        // 设置动画大小以适应屏幕
        var rect = new SKRect(0, 0, width, height);

            // 在指定时间点渲染帧
            _animation?.SeekFrameTime(timePoint);
            _animation?.Render(canvas, rect);        // 获取图像
        using var image = surface.Snapshot();
        using var pixmap = image.PeekPixels();

        // 转换为RGB565格式（ST7789V3兼容）
        return ConvertToRgb565(pixmap, width, height);
    }

    /// <summary>
    /// 重置动画播放计时器
    /// </summary>
    public void ResetPlayback()
    {
        _playbackTimer.Restart();
        _lastRenderTime = 0;
    }

    /// <summary>
    /// 将SkiaSharp像素转换为RGB565格式
    /// </summary>
    private byte[] ConvertToRgb565(SKPixmap pixmap, int width, int height)
    {
        byte[] buffer = new byte[width * height * 2]; // 16位/像素
        // 尝试使用更快的内存复制方法
        if (pixmap.ColorType == SKColorType.Rgb565)
        {
            // 如果已经是RGB565格式，直接复制
            Marshal.Copy(pixmap.GetPixels(), buffer, 0, buffer.Length);
            return buffer;
        }

        // 否则进行逐像素转换
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
    /// 渲染特定进度的帧 (0.0 - 1.0)
    /// </summary>
    public byte[] RenderProgress(double progress, int width, int height)
    {
        if (_animation == null)
            throw new InvalidOperationException("动画未初始化");

        // 确保进度在0到1之间
        progress = Math.Max(0, Math.Min(1, progress));

        // 计算对应的时间点
        double timePoint = progress * _totalSeconds;

        return RenderAtTimePoint(timePoint, width, height);
    }

    public double FrameCount => _totalFrames;
    public double Framerate => _framerate;
    public double TotalSeconds => _totalSeconds;

    // 获取当前播放进度 (0.0 - 1.0)
    public double CurrentProgress => (_playbackTimer.Elapsed.TotalSeconds * PlaybackRate % _totalSeconds) / _totalSeconds;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _animation?.Dispose();
                _lottieData?.Dispose();
                _playbackTimer?.Stop();
            }

            _disposed = true;
        }
    }

    ~LottieRenderer()
    {
        Dispose(false);
    }
}