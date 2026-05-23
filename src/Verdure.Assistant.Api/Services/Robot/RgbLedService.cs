using System.Device.Gpio;
using System.Device.Gpio.Drivers;
using Verdure.Assistant.Api.Models;

namespace Verdure.Assistant.Api.Services.Robot;

/// <summary>
/// RGB LED 灯珠控制服务（共阴 RGB 5050，gpiochip2）
/// 接线：Pin36=R(line31)，Pin37=G(line6)，Pin40=B(line7)，Pin39=GND
/// </summary>
public class RgbLedService : IDisposable
{
    private const int RedLine   = 31;   // Pin 36, GPIO2_D7
    private const int GreenLine =  6;   // Pin 37, GPIO2_A6
    private const int BlueLine  =  7;   // Pin 40, GPIO2_A7

    private readonly GpioController _gpio;
    private readonly ILogger<RgbLedService> _logger;
    private bool _disposed;

    public RgbLedService(ILogger<RgbLedService> logger)
    {
        _logger = logger;
        try
        {
            _gpio = new GpioController(new LibGpiodV2Driver(2));
            _gpio.OpenPin(RedLine,   PinMode.Output);
            _gpio.OpenPin(GreenLine, PinMode.Output);
            _gpio.OpenPin(BlueLine,  PinMode.Output);
            TurnOff();
            _logger.LogInformation("RGB LED 初始化成功 (gpiochip2: R=line{R}, G=line{G}, B=line{B})", RedLine, GreenLine, BlueLine);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RGB LED 初始化失败，灯光功能将不可用");
            _gpio = null!;
        }
    }

    /// <summary>根据情绪类型设置对应颜色</summary>
    public void SetColorForEmotion(string emotionType)
    {
        var (r, g, b) = emotionType switch
        {
            EmotionTypes.Happy     => (true,  true,  false), // 黄色
            EmotionTypes.Sad       => (false, false, true),  // 蓝色
            EmotionTypes.Angry     => (true,  false, false), // 红色
            EmotionTypes.Surprised => (false, true,  true),  // 青色
            EmotionTypes.Confused  => (true,  false, true),  // 紫色
            _                      => (true,  true,  true),  // 白色（neutral）
        };
        SetColor(r, g, b);
    }

    /// <summary>设置 RGB 颜色（true=亮）</summary>
    public void SetColor(bool r, bool g, bool b)
    {
        if (_gpio is null) return;
        try
        {
            _gpio.Write(RedLine,   r ? PinValue.High : PinValue.Low);
            _gpio.Write(GreenLine, g ? PinValue.High : PinValue.Low);
            _gpio.Write(BlueLine,  b ? PinValue.High : PinValue.Low);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RGB LED 写入失败");
        }
    }

    /// <summary>熄灭所有颜色</summary>
    public void TurnOff() => SetColor(false, false, false);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        TurnOff();
        if (_gpio is not null)
        {
            if (_gpio.IsPinOpen(RedLine))   _gpio.ClosePin(RedLine);
            if (_gpio.IsPinOpen(GreenLine)) _gpio.ClosePin(GreenLine);
            if (_gpio.IsPinOpen(BlueLine))  _gpio.ClosePin(BlueLine);
            _gpio.Dispose();
        }
    }
}
