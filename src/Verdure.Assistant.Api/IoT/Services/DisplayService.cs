using System.Device.Gpio;
using System.Device.Spi;
using System.Runtime.InteropServices;
using SkiaSharp;
using Verdure.Assistant.Api.IoT.Display;
using Verdure.Assistant.Api.IoT.Interfaces;
using Verdure.Assistant.Api.IoT.Models;

namespace Verdure.Assistant.Api.IoT.Services;

/// <summary>
/// 双屏显示服务实现
/// </summary>
public class DisplayService : IDisplayService
{
    private ST7789Display? _display24Inch;  // 2.4寸屏幕 - 表情
    private ST7789Display? _display147Inch; // 1.47寸屏幕 - 时间
    private GpioController? _gpio;
    private readonly ILogger<DisplayService> _logger;
    private readonly Dictionary<string, ILottieRenderer> _lottieRenderers;
    private bool _disposed = false;

    // 屏幕尺寸配置
    private const int Display24Width = 320;
    private const int Display24Height = 240;
    private const int Display147Width = 320;
    private const int Display147Height = 172;

    public DisplayService(ILogger<DisplayService> logger)
    {
        _logger = logger;
        _lottieRenderers = new Dictionary<string, ILottieRenderer>();

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            InitializeDisplays();
        }
        else
        {
            _logger.LogWarning("非Linux平台，显示器初始化跳过");
        }

        InitializeLottieRenderers();
    }

    /// <summary>
    /// 初始化显示器
    /// </summary>
    private void InitializeDisplays()
    {
        try
        {
            _gpio = new GpioController();

            var settings1 = new SpiConnectionSettings(0, 0)
            {
                ClockFrequency = 24_000_000,
                Mode = SpiMode.Mode0,
            };

            var settings2 = new SpiConnectionSettings(0, 1)
            {
                ClockFrequency = 24_000_000,
                Mode = SpiMode.Mode0,
            };

            // 创建2.4寸显示器 (表情显示)
            _display24Inch = new ST7789Display(settings1, _gpio, false, dcPin: 25, resetPin: 27, 
                displayType: DisplayType.Display24Inch, logger: _logger);
            
            // 创建1.47寸显示器 (时间显示，横屏模式)
            _display147Inch = new ST7789Display(settings2, _gpio, false, dcPin: 25, resetPin: 27, 
                displayType: DisplayType.Display147Inch, isLandscape: true, logger: _logger);

            // 清屏
            _display24Inch.FillScreen(0x0000);  // 黑色
            _display147Inch.FillScreen(0x0000); // 黑色

            _logger.LogInformation("显示器初始化成功");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "显示器初始化失败");
        }
    }

    /// <summary>
    /// 初始化Lottie渲染器
    /// </summary>
    private void InitializeLottieRenderers()
    {
        try
        {
            // 查找所有lottie文件
            var emotionFiles = new Dictionary<string, string>
            {
                [EmotionTypes.Neutral] = "neutral.mp4.lottie.json",
                [EmotionTypes.Happy] = "happy.mp4.lottie.json",
                [EmotionTypes.Sad] = "sad.mp4.lottie.json",
                [EmotionTypes.Angry] = "angry.mp4.lottie.json",
                [EmotionTypes.Surprised] = "surprised.mp4.lottie.json",
                [EmotionTypes.Confused] = "confused.mp4.lottie.json"
            };

            foreach (var kvp in emotionFiles)
            {
                var emotionType = kvp.Key;
                var fileName = kvp.Value;
                var filePath = FindLottieFile(fileName);

                if (!string.IsNullOrEmpty(filePath))
                {
                    _lottieRenderers[emotionType] = new LottieRenderer(filePath, _logger);
                    _logger.LogInformation($"加载{emotionType}表情文件: {filePath}");
                }
                else
                {
                    _logger.LogWarning($"未找到{emotionType}表情文件: {fileName}");
                }
            }

            _logger.LogInformation($"成功加载 {_lottieRenderers.Count} 个表情渲染器");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Lottie渲染器初始化失败");
        }
    }

    /// <summary>
    /// 查找Lottie文件
    /// </summary>
    private string FindLottieFile(string fileName)
    {
        // 在多个可能的路径中查找
        var searchPaths = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "EmojisFile", fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "Assets", fileName),
            Path.Combine(Directory.GetCurrentDirectory(), "Lottie", fileName),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "EmojisFile", fileName),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", fileName),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Lottie", fileName),
        };

        foreach (var path in searchPaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        _logger.LogWarning($"未找到Lottie文件: {fileName}");
        return string.Empty;
    }

    /// <summary>
    /// 在2.4寸屏幕上播放表情动画
    /// </summary>
    public async Task PlayEmotionAsync(string emotionType, int loops = 1, int fps = 30, CancellationToken cancellationToken = default)
    {
        // 映射表情类型
        var mappedEmotion = EmotionMappingService.MapEmotion(emotionType);
        
        if (!EmotionTypes.IsValid(mappedEmotion))
        {
            _logger.LogWarning($"无效的表情类型: {emotionType} -> {mappedEmotion}");
            return;
        }
        
        if (!_lottieRenderers.ContainsKey(mappedEmotion))
        {
            _logger.LogWarning($"未找到表情类型 {mappedEmotion} 的渲染器");
            return;
        }

        if (_display24Inch == null)
        {
            _logger.LogWarning("2.4寸显示器未初始化");
            return;
        }

        var renderer = _lottieRenderers[mappedEmotion];
        renderer.ResetPlayback();

        _logger.LogInformation($"开始播放表情 {emotionType} -> {mappedEmotion}，循环 {loops} 次，帧率 {fps} fps");

        var totalFrames = (int)renderer.FrameCount;
        int frameDurationMs = 1000 / fps;
        int currentLoop = 0;

        try
        {
            while ((loops == -1 || currentLoop < loops) && !cancellationToken.IsCancellationRequested)
            {
                for (int frame = 0; frame < totalFrames && !cancellationToken.IsCancellationRequested; frame++)
                {
                    var startTime = DateTime.Now;

                    // 渲染当前帧
                    byte[] frameData = renderer.RenderFrame(frame, Display24Width, Display24Height);

                    // 发送到2.4寸屏幕 - 使用ConfigureAwait(false)避免死锁
                    await Task.Run(() => _display24Inch.SendData(frameData), cancellationToken).ConfigureAwait(false);

                    // 帧率控制 - 更精确的时间控制
                    var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                    var delay = frameDurationMs - elapsed;

                    if (delay > 0)
                    {
                        await Task.Delay((int)delay, cancellationToken).ConfigureAwait(false);
                    }
                    else if (delay < -frameDurationMs) // 如果延迟太久，记录警告
                    {
                        _logger.LogDebug($"帧渲染耗时过长: {elapsed}ms (目标: {frameDurationMs}ms)");
                    }
                }

                if (loops != -1)
                    currentLoop++;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug($"表情播放 {mappedEmotion} 已取消");
            // 不清屏，保持最后一帧显示
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"表情播放 {mappedEmotion} 发生错误");
        }
    }

    /// <summary>
    /// 在1.47寸屏幕上显示时间
    /// </summary>
    public async Task DisplayTimeAsync(CancellationToken cancellationToken = default)
    {
        if (_display147Inch == null)
        {
            _logger.LogDebug("1.47寸显示器未初始化，跳过时间显示");
            return;
        }

        try
        {
            var timeText = DateTime.Now.ToString("HH:mm:ss");
            var dateText = DateTime.Now.ToString("yyyy-MM-dd");

            // 创建时间显示的位图
            var timeImage = CreateTimeImage(timeText, dateText, Display147Width, Display147Height);
            
            // 发送到1.47寸屏幕
            await Task.Run(() => _display147Inch.SendData(timeImage), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "时间显示发生错误");
        }
    }

    /// <summary>
    /// 创建时间显示图像
    /// </summary>
    private byte[] CreateTimeImage(string timeText, string dateText, int width, int height)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        using var canvas = surface.Canvas;

        // 清除背景为深蓝色
        canvas.Clear(new SKColor(0, 50, 100));

        // 设置时间字体
        using var timePaint = new SKPaint
        {
            Color = SKColors.White,
            TextSize = 48,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold)
        };

        // 设置日期字体
        using var datePaint = new SKPaint
        {
            Color = SKColors.LightGray,
            TextSize = 24,
            IsAntialias = true,
            Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal)
        };

        // 计算文本位置
        var timeTextBounds = new SKRect();
        timePaint.MeasureText(timeText, ref timeTextBounds);
        
        var dateTextBounds = new SKRect();
        datePaint.MeasureText(dateText, ref dateTextBounds);

        // 绘制时间 (居中显示)
        float timeX = (width - timeTextBounds.Width) / 2;
        float timeY = (height - timeTextBounds.Height) / 2 + timeTextBounds.Height;
        canvas.DrawText(timeText, timeX, timeY, timePaint);

        // 绘制日期 (在时间下方)
        float dateX = (width - dateTextBounds.Width) / 2;
        float dateY = timeY + 40;
        canvas.DrawText(dateText, dateX, dateY, datePaint);

        // 获取图像并转换为RGB565
        using var image = surface.Snapshot();
        using var pixmap = image.PeekPixels();

        return ConvertToRgb565(pixmap, width, height);
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
    /// 清除指定屏幕
    /// </summary>
    public void ClearScreen(bool is24Inch = true, ushort color = 0x0000)
    {
        try
        {
            if (is24Inch && _display24Inch != null)
            {
                _display24Inch.FillScreen(color);
                _logger.LogDebug($"已清除2.4寸屏幕 (颜色: 0x{color:X4})");
            }
            else if (!is24Inch && _display147Inch != null)
            {
                _display147Inch.FillScreen(color);
                _logger.LogDebug($"已清除1.47寸屏幕 (颜色: 0x{color:X4})");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"清屏失败 (2.4寸: {is24Inch})");
        }
    }

    /// <summary>
    /// 渐变清屏
    /// </summary>
    public async Task FadeToBlackAsync(bool is24Inch = true, int durationMs = 500)
    {
        try
        {
            const int steps = 10;
            int delayPerStep = durationMs / steps;
            
            // 从当前显示内容逐渐变暗到黑色
            for (int i = steps; i >= 0; i--)
            {
                // 这里可以实现更复杂的渐变效果
                // 目前简化为直接清屏
                if (i == 0)
                {
                    ClearScreen(is24Inch);
                }
                await Task.Delay(delayPerStep);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "渐变清屏失败");
            // 回退到普通清屏
            ClearScreen(is24Inch);
        }
    }

    /// <summary>
    /// 获取可用的表情类型
    /// </summary>
    public IEnumerable<string> GetAvailableEmotions()
    {
        return _lottieRenderers.Keys;
    }

    /// <summary>
    /// 获取显示器状态
    /// </summary>
    public DisplayStatus GetDisplayStatus(DisplayType type)
    {
        var display = type == DisplayType.Display24Inch ? _display24Inch : _display147Inch;
        
        return new DisplayStatus
        {
            Type = type,
            IsInitialized = display?.IsInitialized ?? false,
            IsDisplaying = display != null,
            LastUpdate = DateTime.Now
        };
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
            foreach (var renderer in _lottieRenderers.Values)
            {
                renderer?.Dispose();
            }
            _lottieRenderers.Clear();

            _display24Inch?.Dispose();
            _display147Inch?.Dispose();
            _gpio?.Dispose();

            _logger.LogInformation("显示服务已释放资源");
            _disposed = true;
        }
    }
}