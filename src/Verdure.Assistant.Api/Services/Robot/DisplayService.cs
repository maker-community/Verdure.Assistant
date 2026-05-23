using SkiaSharp;
using System.Device.Gpio;
using System.Device.Gpio.Drivers;
using System.Device.Spi;
using System.Runtime.InteropServices;
using Verdure.Assistant.Api.IoTDevice;
using Verdure.Assistant.Api.Models;

namespace Verdure.Assistant.Api.Services.Robot;

/// <summary>
/// 双屏显示服务
/// </summary>
public class DisplayService : IDisposable
{
    private ST7789Display? _display24Inch;  // 2.4寸屏幕 - 表情
    private GpioController? _gpio;
    private readonly ILogger<DisplayService> _logger;
    private readonly Dictionary<string, LottieRenderer> _lottieRenderers;
    private bool _disposed = false;
    private volatile bool _isPlayingEmotion = false;

    // 屏幕尺寸配置
    private const int Display24Width = 320;
    private const int Display24Height = 240;

    public DisplayService(ILogger<DisplayService> logger)
    {
        _logger = logger;
        _lottieRenderers = new Dictionary<string, LottieRenderer>();

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
            // 泰山派 3M RK3576: gpiochip2, LibGpiodV2Driver
            _gpio = new GpioController(new LibGpiodV2Driver(2));

            // 泰山派 3M RK3576: SPI1_M1 → /dev/spidev1.0
            var settings1 = new SpiConnectionSettings(1, 0)
            {
                ClockFrequency = 24_000_000,
                Mode = SpiMode.Mode0,
            };

            // 泰山派 3M RK3576: DC=gpiochip2 line30(GPIO2_D6), RESET=gpiochip2 line22(GPIO2_C6)
            _display24Inch = new ST7789Display(settings1, _gpio, true, dcPin: 30, resetPin: 22, displayType: DisplayType.Display24Inch);

            // 清屏
            _display24Inch.FillScreen(0x0000);  // 黑色

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
                    _lottieRenderers[emotionType] = new LottieRenderer(filePath);
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
        if (!EmotionTypes.IsValid(emotionType))
        {
            _logger.LogWarning($"无效的表情类型: {emotionType}");
            return;
        }

        if (!_lottieRenderers.ContainsKey(emotionType))
        {
            _logger.LogWarning($"未找到表情类型 {emotionType} 的渲染器");
            return;
        }

        if (_display24Inch == null)
        {
            _logger.LogWarning("2.4寸显示器未初始化");
            return;
        }

        var renderer = _lottieRenderers[emotionType];
        renderer.ResetPlayback();

        _logger.LogInformation($"开始播放表情 {emotionType}，循环 {loops} 次，帧率 {fps} fps");

        var totalFrames = (int)renderer.FrameCount;
        int frameDurationMs = 1000 / fps;
        int currentLoop = 0;

        _isPlayingEmotion = true;
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
                    await Task.Run(() => _display24Inch.DrawRgb565(frameData), cancellationToken).ConfigureAwait(false);

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
            _logger.LogDebug($"表情播放 {emotionType} 已取消");
            // 不清屏，保持最后一帧显示
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"表情播放 {emotionType} 发生错误");
        }
        finally
        {
            _isPlayingEmotion = false;
        }
    }

    /// <summary>
    /// 在2.4寸屏幕上显示时间
    /// </summary>
    public async Task DisplayTimeAsync(CancellationToken cancellationToken = default)
    {
        if (_display24Inch == null)
        {
            _logger.LogDebug("2.4寸显示器未初始化，跳过时间显示");
            return;
        }
        if (_isPlayingEmotion) return;
        try
        {
            var now = DateTime.Now;
            var imageData = CreateTimeImage(now.ToString("HH:mm:ss"), now.ToString("yyyy-MM-dd"), Display24Width, Display24Height);
            await Task.Run(() => _display24Inch.DrawRgb565(imageData), cancellationToken).ConfigureAwait(false);
            _logger.LogDebug($"2.4寸显示器时间已更新: {now:HH:mm:ss}");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "2.4寸显示器时间显示失败");
        }
    }

    /// <summary>
    /// 在2.4寸屏幕上显示时间和网络信息（IP+端口）
    /// </summary>
    public async Task DisplayTimeWithNetworkInfoAsync(string? ipAddress, CancellationToken cancellationToken = default)
    {
        if (_display24Inch == null)
        {
            _logger.LogDebug("2.4寸显示器未初始化，跳过时间网络信息显示");
            return;
        }
        if (_isPlayingEmotion) return;
        try
        {
            var now = DateTime.Now;
            byte[] imageData;
            if (!string.IsNullOrEmpty(ipAddress))
                imageData = CreateTimeWithNetworkImage(now.ToString("HH:mm:ss"), now.ToString("yyyy-MM-dd"), ipAddress, Display24Width, Display24Height);
            else
                imageData = CreateTimeImage(now.ToString("HH:mm:ss"), now.ToString("yyyy-MM-dd"), Display24Width, Display24Height);
            await Task.Run(() => _display24Inch.DrawRgb565(imageData), cancellationToken).ConfigureAwait(false);
            _logger.LogDebug($"2.4寸显示器时间+网络信息已更新: {now:HH:mm:ss} IP:{ipAddress}");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "2.4寸显示器时间网络信息显示失败");
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
        using var timeFont = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), 48);
        using var timePaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true
        };

        // 设置日期字体
        using var dateFont = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal), 24);
        using var datePaint = new SKPaint
        {
            Color = SKColors.LightGray,
            IsAntialias = true
        };

        // 计算文本位置
        var timeTextWidth = timeFont.MeasureText(timeText);
        var dateTextWidth = dateFont.MeasureText(dateText);

        // 绘制时间 (居中显示)
        float timeX = (width - timeTextWidth) / 2;
        float timeY = (height / 2) - 10;
        canvas.DrawText(timeText, timeX, timeY, SKTextAlign.Left, timeFont, timePaint);

        // 绘制日期 (在时间下方)
        float dateX = (width - dateTextWidth) / 2;
        float dateY = timeY + 40;
        canvas.DrawText(dateText, dateX, dateY, SKTextAlign.Left, dateFont, datePaint);

        // 获取图像并转换为RGB565
        using var image = surface.Snapshot();
        using var pixmap = image.PeekPixels();

        return ConvertToRgb565(pixmap, width, height);
    }

    /// <summary>
    /// 创建时间和网络信息显示图像（显示IP和端口）
    /// </summary>
    private byte[] CreateTimeWithNetworkImage(string timeText, string dateText, string ipAddress, int width, int height)
    {
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        using var canvas = surface.Canvas;

        // 清除背景为深蓝色（表示有网络）
        canvas.Clear(new SKColor(0, 50, 100));

        // 设置时间字体（稍小一些，为IP信息腾出空间）
        using var timeFont = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold), 36);
        using var timePaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true
        };

        // 设置日期字体
        using var dateFont = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal), 18);
        using var datePaint = new SKPaint
        {
            Color = SKColors.LightGray,
            IsAntialias = true
        };

        // 设置IP信息字体
        using var ipFont = new SKFont(SKTypeface.FromFamilyName("Arial", SKFontStyle.Normal), 16);
        using var ipPaint = new SKPaint
        {
            Color = new SKColor(144, 238, 144), // 浅绿色表示网络已连接
            IsAntialias = true
        };

        // 获取API端口（假设从配置读取，这里硬编码为5241）
        var serverInfo = $"{ipAddress}:5241";

        // 计算文本位置
        var timeTextWidth = timeFont.MeasureText(timeText);
        var dateTextWidth = dateFont.MeasureText(dateText);
        var serverTextWidth = ipFont.MeasureText(serverInfo);

        // 绘制时间 (靠上居中)
        float timeX = (width - timeTextWidth) / 2;
        float timeY = height / 3;
        canvas.DrawText(timeText, timeX, timeY, SKTextAlign.Left, timeFont, timePaint);

        // 绘制日期 (在时间下方)
        float dateX = (width - dateTextWidth) / 2;
        float dateY = timeY + 30;
        canvas.DrawText(dateText, dateX, dateY, SKTextAlign.Left, dateFont, datePaint);

        // 绘制服务器地址 (在底部)
        float serverX = (width - serverTextWidth) / 2;
        float serverY = height * 2 / 3 + 20;
        canvas.DrawText(serverInfo, serverX, serverY, SKTextAlign.Left, ipFont, ipPaint);

        // 绘制网络状态指示器（小圆点）
        using var statusPaint = new SKPaint
        {
            Color = new SKColor(0, 255, 0), // 绿色表示在线
            IsAntialias = true,
            Style = SKPaintStyle.Fill
        };
        canvas.DrawCircle(width - 15, 15, 8, statusPaint);

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
    /// <param name="is24Inch">是否为2.4寸屏幕</param>
    /// <param name="color">清除的颜色 (RGB565格式，默认黑色)</param>
    public void ClearScreen(bool is24Inch = true, ushort color = 0x0000)
    {
        try
        {
            if (is24Inch && _display24Inch != null)
            {
                _display24Inch.FillScreen(color);
                _logger.LogDebug($"已清除2.4寸屏幕 (颜色: 0x{color:X4})");
            }
            // 1.47寸屏幕已移除，忽略非24寸清屏请求
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"清屏失败 (2.4寸: {is24Inch})");
        }
    }

    /// <summary>
    /// 渐变清屏 (可选的视觉效果)
    /// </summary>
    /// <param name="is24Inch">是否为2.4寸屏幕</param>
    /// <param name="durationMs">渐变持续时间</param>
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
            _gpio?.Dispose();

            _logger.LogInformation("显示服务已释放资源");
            _disposed = true;
        }
    }
}