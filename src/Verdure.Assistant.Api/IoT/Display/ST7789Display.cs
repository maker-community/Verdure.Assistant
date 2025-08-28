using System.Device.Gpio;
using System.Device.Spi;
using System.Runtime.InteropServices;
using Verdure.Assistant.Api.IoT.Models;

namespace Verdure.Assistant.Api.IoT.Display;

/// <summary>
/// ST7789 TFT显示器驱动 - 迁移自Verdure.Iot.Device
/// 支持2.4寸和1.47寸显示器
/// </summary>
public class ST7789Display : IDisposable
{
    private readonly SpiDevice _spiDevice;
    private readonly GpioController _gpio;
    private readonly bool _shouldDisposeGpio;
    private readonly int _dcPin;
    private readonly int _resetPin;
    private readonly DisplayType _displayType;
    private readonly bool _isLandscape;
    private readonly ILogger<ST7789Display>? _logger;
    
    private bool _disposed = false;
    private bool _isInitialized = false;

    // 显示器尺寸配置
    private const int Display24Width = 320;
    private const int Display24Height = 240;
    private const int Display147Width = 320;
    private const int Display147Height = 172;

    public ST7789Display(
        SpiConnectionSettings spiSettings,
        GpioController? gpio = null,
        bool shouldDisposeGpio = true,
        int dcPin = 25,
        int resetPin = 27,
        DisplayType displayType = DisplayType.Display24Inch,
        bool isLandscape = false,
        ILogger<ST7789Display>? logger = null)
    {
        _spiDevice = SpiDevice.Create(spiSettings);
        _gpio = gpio ?? new GpioController();
        _shouldDisposeGpio = shouldDisposeGpio;
        _dcPin = dcPin;
        _resetPin = resetPin;
        _displayType = displayType;
        _isLandscape = isLandscape;
        _logger = logger;

        InitializeDisplay();
    }

    /// <summary>
    /// 初始化显示器
    /// </summary>
    private void InitializeDisplay()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            _logger?.LogWarning("非Linux平台，ST7789显示器初始化跳过");
            return;
        }

        try
        {
            // 设置GPIO引脚
            _gpio.OpenPin(_dcPin, PinMode.Output);
            _gpio.OpenPin(_resetPin, PinMode.Output);

            // 硬件复位
            _gpio.Write(_resetPin, PinValue.Low);
            Thread.Sleep(100);
            _gpio.Write(_resetPin, PinValue.High);
            Thread.Sleep(100);

            // 发送初始化命令序列
            SendInitCommands();
            
            _isInitialized = true;
            _logger?.LogInformation($"ST7789显示器初始化成功 - 类型: {_displayType}, 横屏: {_isLandscape}");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "ST7789显示器初始化失败");
        }
    }

    /// <summary>
    /// 发送初始化命令
    /// </summary>
    private void SendInitCommands()
    {
        // 软件复位
        SendCommand(0x01);
        Thread.Sleep(150);

        // 退出睡眠
        SendCommand(0x11);
        Thread.Sleep(120);

        // 设置颜色模式为RGB565
        SendCommand(0x3A);
        SendData(0x05);

        // 设置内存访问控制
        SendCommand(0x36);
        if (_displayType == DisplayType.Display147Inch && _isLandscape)
        {
            SendData(0x70); // 1.47寸横屏模式
        }
        else
        {
            SendData(0x00); // 默认竖屏模式
        }

        // 设置显示区域
        SetDisplayWindow();

        // 开启显示
        SendCommand(0x29);
        Thread.Sleep(100);
    }

    /// <summary>
    /// 设置显示窗口
    /// </summary>
    private void SetDisplayWindow()
    {
        int width, height;
        
        if (_displayType == DisplayType.Display147Inch)
        {
            width = _isLandscape ? Display147Height : Display147Width;
            height = _isLandscape ? Display147Width : Display147Height;
        }
        else
        {
            width = Display24Width;
            height = Display24Height;
        }

        // 设置列地址
        SendCommand(0x2A);
        SendData((byte)(0 >> 8));
        SendData((byte)(0 & 0xFF));
        SendData((byte)((width - 1) >> 8));
        SendData((byte)((width - 1) & 0xFF));

        // 设置行地址
        SendCommand(0x2B);
        SendData((byte)(0 >> 8));
        SendData((byte)(0 & 0xFF));
        SendData((byte)((height - 1) >> 8));
        SendData((byte)((height - 1) & 0xFF));

        // 写入内存
        SendCommand(0x2C);
    }

    /// <summary>
    /// 发送命令
    /// </summary>
    private void SendCommand(byte command)
    {
        _gpio.Write(_dcPin, PinValue.Low);
        _spiDevice.WriteByte(command);
    }

    /// <summary>
    /// 发送数据
    /// </summary>
    private void SendData(byte data)
    {
        _gpio.Write(_dcPin, PinValue.High);
        _spiDevice.WriteByte(data);
    }

    /// <summary>
    /// 发送数据块
    /// </summary>
    public void SendData(byte[] data)
    {
        if (!_isInitialized || data == null || data.Length == 0)
            return;

        try
        {
            _gpio.Write(_dcPin, PinValue.High);
            _spiDevice.Write(data);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "发送数据块失败");
        }
    }

    /// <summary>
    /// 填充屏幕为指定颜色
    /// </summary>
    public void FillScreen(ushort color)
    {
        if (!_isInitialized)
            return;

        try
        {
            SetDisplayWindow();

            int width, height;
            if (_displayType == DisplayType.Display147Inch)
            {
                width = _isLandscape ? Display147Height : Display147Width;
                height = _isLandscape ? Display147Width : Display147Height;
            }
            else
            {
                width = Display24Width;
                height = Display24Height;
            }

            int totalPixels = width * height;
            byte[] buffer = new byte[totalPixels * 2];

            // 填充颜色数据 (RGB565格式)
            for (int i = 0; i < totalPixels; i++)
            {
                buffer[i * 2] = (byte)(color >> 8);
                buffer[i * 2 + 1] = (byte)(color & 0xFF);
            }

            SendData(buffer);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "填充屏幕失败");
        }
    }

    /// <summary>
    /// 获取显示器宽度
    /// </summary>
    public int Width => _displayType == DisplayType.Display147Inch 
        ? (_isLandscape ? Display147Height : Display147Width)
        : Display24Width;

    /// <summary>
    /// 获取显示器高度
    /// </summary>
    public int Height => _displayType == DisplayType.Display147Inch 
        ? (_isLandscape ? Display147Width : Display147Height)
        : Display24Height;

    /// <summary>
    /// 检查是否已初始化
    /// </summary>
    public bool IsInitialized => _isInitialized;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            try
            {
                if (_isInitialized && RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    // 关闭显示器
                    SendCommand(0x28); // Display off
                    SendCommand(0x10); // Sleep in
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "关闭显示器时发生错误");
            }

            _spiDevice?.Dispose();
            
            if (_shouldDisposeGpio)
            {
                _gpio?.Dispose();
            }

            _logger?.LogInformation("ST7789显示器已释放资源");
            _disposed = true;
        }
    }
}