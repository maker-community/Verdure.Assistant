using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Device.Gpio;
using System.Device.Spi;

namespace Verdure.Assistant.Api.IoTDevice;

public class ST7789Display : IDisposable
{
    private readonly SpiDevice _spiDevice;
    private readonly GpioController _gpio;
    private readonly int _dcPin;
    private readonly int _resetPin;
    private readonly int _csPin;
    private readonly int _width;
    private readonly int _height;
    private readonly int _xOffset;
    private readonly int _yOffset;
    private readonly bool _isRgbPanel;
    private readonly DisplayType _displayType;
    private const int MAX_TRANSFER_SIZE = 4096; // 最大一次传输字节数

    // 构造函数支持不同尺寸屏幕的配置参数
    public ST7789Display(SpiConnectionSettings settings, GpioController gpio, bool isResetGpio, int dcPin, int resetPin, int csPin = -1, DisplayType displayType = DisplayType.Display24Inch, bool isLandscape = false)
    {
        _spiDevice = SpiDevice.Create(settings);
        _gpio = gpio;
        _dcPin = dcPin;
        _resetPin = resetPin;
        _csPin = csPin;
        _displayType = displayType;

        if (isResetGpio)
        {
            // 初始化GPIO引脚
            gpio.OpenPin(_dcPin, PinMode.Output);
            gpio.OpenPin(_resetPin, PinMode.Output);

            gpio.Write(_resetPin, PinValue.High);
            Thread.Sleep(20);
            gpio.Write(_resetPin, PinValue.Low);
            Thread.Sleep(20);  // 增加复位低电平时间
            gpio.Write(_resetPin, PinValue.High);
            Thread.Sleep(150); // 增加复位后等待时间
        }


        if (_csPin >= 0)
        {
            _gpio.OpenPin(_csPin, PinMode.Output);
            _gpio.Write(_csPin, PinValue.High);
        }

        // 根据屏幕类型设置参数
        switch (displayType)
        {
            case DisplayType.Display24Inch:
                _width = 320;
                _height = 240;
                _xOffset = 0;
                _yOffset = 0;
                _isRgbPanel = true;
                break;

            case DisplayType.Display147Inch:
                if (isLandscape)
                {
                    // 横屏模式：320x172
                    _width = 320;
                    _height = 172;
                    _xOffset = 0;   // 横屏模式X偏移
                    _yOffset = 34;  // 横屏模式可能需要Y偏移34像素
                }
                else
                {
                    // 竖屏模式：172x320  
                    _width = 172;
                    _height = 320;
                    _xOffset = 34;  // 竖屏模式需要X偏移34像素
                    _yOffset = 0;
                }
                _isRgbPanel = true;
                break;

            case DisplayType.Display13Inch:
                _width = 240;
                _height = 240;
                _xOffset = 0;
                _yOffset = 0;
                _isRgbPanel = true;
                break;

            default:
                throw new ArgumentException("不支持的显示屏类型");
        }

        Initialize(isLandscape);
    }

    // 初始化显示屏
    private void Initialize(bool isLandscape = false)
    {
        // 硬件复位
        //HardReset();

        switch (_displayType)
        {
            case DisplayType.Display24Inch:
                Initialize24Inch();
                break;

            case DisplayType.Display147Inch:
                Initialize147Inch(isLandscape);
                break;

            case DisplayType.Display13Inch:
                Initialize13Inch();
                break;
        }
    }

    // 初始化2.4英寸屏幕
    private void Initialize24Inch()
    {
        // 发送初始化命令序列
        SendCommand(0x01);    // Software Reset
        Thread.Sleep(150);

        // MADCTL: Memory Data Access Control
        SendCommand(0x36);
        SendData(0x70);    // 按照参考代码修改为0x00

        // COLMOD: Pixel Format Set
        SendCommand(0x3A);
        SendData(0x05);    // 16-bit/pixel (5-6-5 RGB)

        // Display Inversion On
        SendCommand(0x21);

        // 设置列地址 - 调整为参考代码的顺序
        SendCommand(0x2A);    // Column Address Set
        SendData(0x00);    // 起始列高字节
        SendData(0x00);    // 起始列低字节
        SendData(0x01);    // 结束列高字节 - 调整为参考代码值
        SendData(0x3F);    // 结束列低字节 (319)

        // 设置行地址 - 调整为参考代码的顺序
        SendCommand(0x2B);    // Row Address Set
        SendData(0x00);    // 起始行高字节
        SendData(0x00);    // 起始行低字节
        SendData(0x00);    // 结束行高字节 - 调整为参考代码值
        SendData(0xEF);    // 结束行低字节 (239)

        // 电源相关设置
        SendCommand(0xB2);    // Porch Setting
        SendData(0x0C);
        SendData(0x0C);
        SendData(0x00);
        SendData(0x33);
        SendData(0x33);

        SendCommand(0xB7);    // Gate Control
        SendData(0x35);

        SendCommand(0xBB);    // VCOM Setting
        SendData(0x1F);

        SendCommand(0xC0);    // LCM Control
        SendData(0x2C);

        SendCommand(0xC2);    // VDV and VRH Command Enable
        SendData(0x01);

        SendCommand(0xC3);    // VRH Set
        SendData(0x12);

        SendCommand(0xC4);    // VDV Set
        SendData(0x20);

        SendCommand(0xC6);    // Frame Rate Control
        SendData(0x0F);

        SendCommand(0xD0);    // Power Control 1
        SendData(0xA4);
        SendData(0xA1);

        // Gamma校正
        SendCommand(0xE0);    // Positive Voltage Gamma Control
        SendData(0xD0);
        SendData(0x08);
        SendData(0x11);
        SendData(0x08);
        SendData(0x0C);
        SendData(0x15);
        SendData(0x39);
        SendData(0x33);
        SendData(0x50);
        SendData(0x36);
        SendData(0x13);
        SendData(0x14);
        SendData(0x29);
        SendData(0x2D);

        SendCommand(0xE1);    // Negative Voltage Gamma Control
        SendData(0xD0);
        SendData(0x08);
        SendData(0x10);
        SendData(0x08);
        SendData(0x06);
        SendData(0x06);
        SendData(0x39);
        SendData(0x44);
        SendData(0x51);
        SendData(0x0B);
        SendData(0x16);
        SendData(0x14);
        SendData(0x2F);
        SendData(0x31);

        // Sleep Out
        SendCommand(0x11);
        Thread.Sleep(120);

        // 设置显示区域 - 保持现有代码中的调用
        SetAddressWindow(0, 0, _width, _height);

        // Display On
        SendCommand(0x29);
        Thread.Sleep(100); // 增加延时确保显示开启完成
    }

    // 初始化1.47英寸屏幕
    private void Initialize147Inch(bool isLandscape = false)
    {
        // 发送初始化命令序列
        SendCommand(0x01);    // Software Reset
        Thread.Sleep(150);

        SendCommand(0x11);    // Sleep Out
        Thread.Sleep(120);

        SendCommand(0x36);    // MADCTL: Memory Data Access Control
        if (isLandscape)
        {
            SendData(0x60);   // 横屏模式：MY=0, MX=1, MV=1
        }
        else
        {
            SendData(0x00);   // 竖屏模式：MY=0, MX=0, MV=0
        }

        SendCommand(0x3A);    // COLMOD: Pixel Format Set
        SendData(0x05);       // 16-bit/pixel

        SendCommand(0xB2);    // Porch Setting
        SendData(0x0C);
        SendData(0x0C);
        SendData(0x00);
        SendData(0x33);
        SendData(0x33);

        SendCommand(0xB7);    // Gate Control
        SendData(0x35);

        SendCommand(0xBB);    // VCOM Setting
        SendData(0x35);       // 根据参考代码调整为0x35

        SendCommand(0xC0);    // LCM Control
        SendData(0x2C);

        SendCommand(0xC2);    // VDV and VRH Command Enable
        SendData(0x01);

        SendCommand(0xC3);    // VRH Set
        SendData(0x13);       // 根据参考代码调整为0x13

        SendCommand(0xC4);    // VDV Set
        SendData(0x20);

        SendCommand(0xC6);    // Frame Rate Control
        SendData(0x0F);

        SendCommand(0xD0);    // Power Control 1
        SendData(0xA4);
        SendData(0xA1);

        SendCommand(0xE0);    // Positive Voltage Gamma Control
        SendData(0xF0);       // 根据参考代码调整Gamma值
        SendData(0xF0);
        SendData(0x00);
        SendData(0x04);
        SendData(0x04);
        SendData(0x04);
        SendData(0x05);
        SendData(0x29);
        SendData(0x33);
        SendData(0x3E);
        SendData(0x38);
        SendData(0x12);
        SendData(0x12);
        SendData(0x28);
        SendData(0x30);

        SendCommand(0xE1);    // Negative Voltage Gamma Control
        SendData(0xF0);       // 根据参考代码调整Gamma值
        SendData(0x07);
        SendData(0x0A);
        SendData(0x0D);
        SendData(0x0B);
        SendData(0x07);
        SendData(0x28);
        SendData(0x33);
        SendData(0x3E);
        SendData(0x36);
        SendData(0x14);
        SendData(0x14);
        SendData(0x29);
        SendData(0x32);

        SendCommand(0x21);    // Display Inversion On

        // 设置显示区域
        SetAddressWindow(0, 0, _width, _height);

        SendCommand(0x29);    // Display On

        Thread.Sleep(20);
    }

    // 初始化1.3英寸屏幕
    private void Initialize13Inch()
    {
        // 发送初始化命令序列
        SendCommand(0x01);    // Software Reset
        Thread.Sleep(150);

        SendCommand(0x11);    // Sleep Out
        Thread.Sleep(120);

        SendCommand(0x3A);    // COLMOD: Pixel Format Set
        SendData(0x05);       // 16-bit/pixel

        SendCommand(0x36);    // MADCTL: Memory Data Access Control
        SendData(0x00);       // RGB顺序

        SendCommand(0xB2);    // Porch Setting
        SendData(0x0C);
        SendData(0x0C);
        SendData(0x00);
        SendData(0x33);
        SendData(0x33);

        SendCommand(0xB7);    // Gate Control
        SendData(0x35);

        SendCommand(0xBB);    // VCOM Setting
        SendData(0x19);

        SendCommand(0xC0);    // LCM Control
        SendData(0x2C);

        SendCommand(0xC2);    // VDV and VRH Command Enable
        SendData(0x01);

        SendCommand(0xC3);    // VRH Set
        SendData(0x12);

        SendCommand(0xC4);    // VDV Set
        SendData(0x20);

        SendCommand(0xC6);    // Frame Rate Control
        SendData(0x0F);

        SendCommand(0xD0);    // Power Control 1
        SendData(0xA4);
        SendData(0xA1);

        SendCommand(0xE0);    // Positive Voltage Gamma Control
        SendData(0xD0);
        SendData(0x04);
        SendData(0x0D);
        SendData(0x11);
        SendData(0x13);
        SendData(0x2B);
        SendData(0x3F);
        SendData(0x54);
        SendData(0x4C);
        SendData(0x18);
        SendData(0x0D);
        SendData(0x0B);
        SendData(0x1F);
        SendData(0x23);

        SendCommand(0xE1);    // Negative Voltage Gamma Control
        SendData(0xD0);
        SendData(0x04);
        SendData(0x0C);
        SendData(0x11);
        SendData(0x13);
        SendData(0x2C);
        SendData(0x3F);
        SendData(0x44);
        SendData(0x51);
        SendData(0x2F);
        SendData(0x1F);
        SendData(0x1F);
        SendData(0x20);
        SendData(0x23);

        // 设置显示区域
        SetAddressWindow(0, 0, _width, _height);

        SendCommand(0x21);    // Display Inversion On

        SendCommand(0x29);    // Display On
        Thread.Sleep(20);
    }

    /// <summary>
    /// 执行默认的复位逻辑
    /// </summary>
    public void PerformDefaultReset()
    {
        _gpio.Write(_resetPin, PinValue.High);
        Thread.Sleep(20);
        _gpio.Write(_resetPin, PinValue.Low);
        Thread.Sleep(20);  // 增加复位低电平时间
        _gpio.Write(_resetPin, PinValue.High);
        Thread.Sleep(150); // 增加复位后等待时间
    }

    // 发送命令
    public void SendCommand(byte command)
    {
        if (_csPin >= 0)
        {
            _gpio.Write(_csPin, PinValue.Low);
        }
        _gpio.Write(_dcPin, PinValue.Low);  // DC低电平表示命令
        _spiDevice.Write(new[] { command });
        if (_csPin >= 0)
        {
            _gpio.Write(_csPin, PinValue.High);
        }
        // 添加短暂延时以确保命令被处理
        Thread.Sleep(1);
    }

    // 发送数据
    public void SendData(byte data)
    {
        if (_csPin >= 0)
        {
            _gpio.Write(_csPin, PinValue.Low);
        }
        _gpio.Write(_dcPin, PinValue.High); // DC高电平表示数据
        _spiDevice.Write(new[] { data });
        if (_csPin >= 0)
        {
            _gpio.Write(_csPin, PinValue.High);
        }
    }

    // 发送数据数组
    public void SendData(byte[] data)
    {
        if (_csPin >= 0)
        {
            _gpio.Write(_csPin, PinValue.Low);
        }
        _gpio.Write(_dcPin, PinValue.High);

        // 分块发送大数据，避免内存问题
        for (int i = 0; i < data.Length; i += MAX_TRANSFER_SIZE)
        {
            int length = Math.Min(MAX_TRANSFER_SIZE, data.Length - i);
            _spiDevice.Write(data.AsSpan(i, length).ToArray());
        }

        if (_csPin >= 0)
        {
            _gpio.Write(_csPin, PinValue.High);
        }
    }

    // 发送数据Span
    public void SendData(ReadOnlySpan<byte> data)
    {
        if (_csPin >= 0)
        {
            _gpio.Write(_csPin, PinValue.Low);
        }
        _gpio.Write(_dcPin, PinValue.High);

        // 分块发送大数据，避免内存问题
        for (int i = 0; i < data.Length; i += MAX_TRANSFER_SIZE)
        {
            int length = Math.Min(MAX_TRANSFER_SIZE, data.Length - i);
            _spiDevice.Write(data.Slice(i, length).ToArray());
        }

        if (_csPin >= 0)
        {
            _gpio.Write(_csPin, PinValue.High);
        }
    }

    // 设置显示区域
    public void SetAddressWindow(int x0, int y0, int x1, int y1)
    {
        // 应用偏移量
        x0 += _xOffset;
        y0 += _yOffset;
        x1 += _xOffset;
        y1 += _yOffset;

        // 根据屏幕类型调整显示区域设置
        switch (_displayType)
        {
            case DisplayType.Display24Inch:
                // 2.4寸屏幕设置
                SendCommand(0x2A);
                SendData((byte)(x0 >> 8));
                SendData((byte)(x0 & 0xff));
                SendData((byte)(x1 >> 8));
                SendData((byte)((x1 - 1) & 0xff));

                SendCommand(0x2B);
                SendData((byte)(y0 >> 8));
                SendData((byte)(y0 & 0xff));
                SendData((byte)(y1 >> 8));
                SendData((byte)((y1 - 1) & 0xff));

                SendCommand(0x2C);
                break;

            case DisplayType.Display147Inch:
                // 1.47寸屏幕设置 - 偏移已在方法开头统一处理
                if (_width == 320 && _height == 172)
                {
                    // 横屏模式 (320x172) - 使用已应用偏移的坐标
                    SendCommand(0x2A);
                    SendData((byte)(x0 >> 8));
                    SendData((byte)(x0 & 0xff));
                    SendData((byte)((x1 - 1) >> 8));
                    SendData((byte)((x1 - 1) & 0xff));

                    SendCommand(0x2B);
                    SendData((byte)(y0 >> 8));
                    SendData((byte)(y0 & 0xff));
                    SendData((byte)((y1 - 1) >> 8));
                    SendData((byte)((y1 - 1) & 0xff));
                }
                else
                {
                    // 竖屏模式 (172x320) - 使用已应用偏移的坐标
                    SendCommand(0x2A);
                    SendData((byte)(x0 >> 8));
                    SendData((byte)(x0 & 0xff));
                    SendData((byte)((x1 - 1) >> 8));
                    SendData((byte)((x1 - 1) & 0xff));

                    SendCommand(0x2B);
                    SendData((byte)(y0 >> 8));
                    SendData((byte)(y0 & 0xff));
                    SendData((byte)((y1 - 1) >> 8));
                    SendData((byte)((y1 - 1) & 0xff));
                }
                break;

            case DisplayType.Display13Inch:
                // 1.3寸屏幕设置
                SendCommand(0x2A);
                SendData((byte)(x0 >> 8));
                SendData((byte)(x0 & 0xff));
                SendData((byte)(x1 >> 8));
                SendData((byte)(x1 & 0xff));

                SendCommand(0x2B);
                SendData((byte)(y0 >> 8));
                SendData((byte)(y0 & 0xff));
                SendData((byte)(y1 >> 8));
                SendData((byte)(y1 & 0xff));
                break;

            default:
                // 默认设置方式
                SendCommand(0x2A);
                SendData((byte)(x0 >> 8));
                SendData((byte)(x0 & 0xff));
                SendData((byte)(x1 >> 8));
                SendData((byte)(x1 & 0xff));

                SendCommand(0x2B);
                SendData((byte)(y0 >> 8));
                SendData((byte)(y0 & 0xff));
                SendData((byte)(y1 >> 8));
                SendData((byte)(y1 & 0xff));
                break;
        }

        // 写入RAM命令
        SendCommand(0x2C);
    }
    // 显示图像
    public void DrawImage(byte[] imageData)
    {
        SetAddressWindow(0, 0, _width - 1, _height - 1);
        SendData(imageData);
    }

    // 显示ImageSharp格式图像
    public void DrawImage(Image<Bgr24> image, int xStart = 0, int yStart = 0)
    {
        int imwidth = image.Width;
        int imheight = image.Height;

        // 计算显示区域
        int displayWidth = Math.Min(_width - xStart, imwidth);
        int displayHeight = Math.Min(_height - yStart, imheight);

        SetAddressWindow(xStart, yStart, xStart + displayWidth, yStart + displayHeight);

        // 将图像转换为设备支持的格式
        var pixelData = GetImageBytes(image);
        SendData(pixelData);
    }

    // 获取图像的字节数组表示
    public byte[] GetImageBytes(Image<Bgr24> image)
    {
        int imwidth = image.Width;
        int imheight = image.Height;
        var pix = new byte[imheight * imwidth * 2];

        for (int y = 0; y < imheight; y++)
        {
            for (int x = 0; x < imwidth; x++)
            {
                var color = image[x, y];
                pix[(y * imwidth + x) * 2] = (byte)((color.R & 0xF8) | (color.G >> 5));
                pix[(y * imwidth + x) * 2 + 1] = (byte)(((color.G << 3) & 0xE0) | (color.B >> 3));
            }
        }

        return pix;
    }

    // 填充纯色
    public void FillScreen(ushort color)
    {
        SetAddressWindow(0, 0, _width, _height);

        // 创建缓冲区（考虑内存限制，使用较小的缓冲区）
        int bufferSize = Math.Min(_width * _height * 2, 32768); // 最大32KB的缓冲区
        byte[] buffer = new byte[bufferSize];

        // 填充缓冲区
        for (int i = 0; i < bufferSize; i += 2)
        {
            buffer[i] = (byte)(color >> 8);
            buffer[i + 1] = (byte)(color & 0xFF);
        }

        // 分块发送数据
        int totalBytes = _width * _height * 2;
        int bytesWritten = 0;

        while (bytesWritten < totalBytes)
        {
            int bytesToWrite = Math.Min(buffer.Length, totalBytes - bytesWritten);
            SendData(buffer.AsSpan(0, bytesToWrite));
            bytesWritten += bytesToWrite;
        }
    }

    // 设置内存访问控制 - 修正2.4寸屏幕旋转值
    public void SetRotation(byte rotation)
    {
        SendCommand(0x36);
        switch (_displayType)
        {
            case DisplayType.Display24Inch:
                // 特别调整2.4寸屏幕的旋转参数
                switch (rotation % 4)
                {
                    case 0:
                        SendData(0x70); // 0度旋转
                        break;
                    case 1:
                        SendData(0x00); // 90度旋转
                        break;
                    case 2:
                        SendData(0xC0); // 180度旋转
                        break;
                    case 3:
                        SendData(0xA0); // 270度旋转
                        break;
                }
                break;

            default:
                // 其他屏幕使用原来的逻辑
                switch (rotation % 4)
                {
                    case 0:
                        SendData(_isRgbPanel ? (byte)0x70 : (byte)0x00); // 0度旋转
                        break;
                    case 1:
                        SendData(_isRgbPanel ? (byte)0x10 : (byte)0x60); // 90度旋转
                        break;
                    case 2:
                        SendData(_isRgbPanel ? (byte)0xB0 : (byte)0xC0); // 180度旋转
                        break;
                    case 3:
                        SendData(_isRgbPanel ? (byte)0xD0 : (byte)0xA0); // 270度旋转
                        break;
                }
                break;
        }
    }

    // 获取屏幕宽度
    public int Width => _width;

    // 获取屏幕高度
    public int Height => _height;


    public void Dispose()
    {
        _spiDevice?.Dispose();

        // 关闭GPIO引脚
        if (_gpio != null)
        {
            _gpio?.ClosePin(_dcPin);
            _gpio?.ClosePin(_resetPin);
            if (_csPin >= 0)
            {
                _gpio?.ClosePin(_csPin);
            }
            _gpio?.Dispose();
        }
    }
}

public enum DisplayType
{
    Display24Inch,
    Display147Inch,
    Display13Inch
}