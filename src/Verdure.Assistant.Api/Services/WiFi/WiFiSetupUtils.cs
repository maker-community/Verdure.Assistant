using System.Runtime.InteropServices;
using SkiaSharp;
using ZXing;
using ZXing.QrCode;

namespace Verdure.Assistant.Api.Services.WiFi;

/// <summary>
/// WiFi配网工具类 - 处理QR码生成和图像创建
/// </summary>
public static class WiFiSetupUtils
{
    private static readonly ILogger _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger("WiFiSetupUtils");

    /// <summary>
    /// 生成QR码并保存为图片文件（用于非Linux环境测试）
    /// </summary>
    public static void GenerateQrCodeImage(string url, string outputPath = "qrcode.png")
    {
        try
        {
            var qrCodeData = GenerateQrCodeData(url, 400);

            // 保存PNG文件
            using var stream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            qrCodeData.SaveTo(stream);

            _logger.LogInformation("QR码图片已保存到: {OutputPath}", outputPath);
            _logger.LogInformation("QR码内容: {Url}", url);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "生成QR码图片失败");
        }
    }

    /// <summary>
    /// 生成二维码数据（PNG格式）- 使用ZXing.Net
    /// </summary>
    private static SKData GenerateQrCodeData(string text, int size)
    {
        var qrWriter = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.QR_CODE,
            Options = new QrCodeEncodingOptions
            {
                Height = size,
                Width = size,
                Margin = 1
            }
        };
        var pixelData = qrWriter.Write(text);

        // 创建SkiaSharp位图
        using var bitmap = new SKBitmap(pixelData.Width, pixelData.Height);
        var pixels = pixelData.Pixels;

        // ZXing的pixelData.Pixels是BGRA格式，每个像素4个字节
        for (int y = 0; y < pixelData.Height; y++)
        {
            for (int x = 0; x < pixelData.Width; x++)
            {
                int offset = (y * pixelData.Width + x) * 4; // 4 bytes per pixel (BGRA)
                // 根据像素值设置为黑色或白色
                byte pixelValue = pixels[offset]; // 取B通道值判断
                bitmap.SetPixel(x, y, pixelValue == 0 ? SKColors.Black : SKColors.White);
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 100);
    }

    /// <summary>
    /// 创建带文本的QR码图像数据（RGB565格式，用于SPI显示器）
    /// </summary>
    public static byte[]? CreateQrCodeWithTextImageData(string url, string text, int width, int height)
    {
        try
        {
            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            var canvas = surface.Canvas;

            // 清除背景为黑色
            canvas.Clear(SKColors.Black);

            // 计算二维码大小 - 留出空间给文本
            int textAreaHeight = string.IsNullOrEmpty(text) ? 0 : Math.Min(30, height / 10);
            int qrSize = Math.Min(width - 20, height - textAreaHeight - 20);

            // 生成二维码 - 使用ZXing.Net
            var qrWriter = new BarcodeWriterPixelData
            {
                Format = BarcodeFormat.QR_CODE,
                Options = new QrCodeEncodingOptions
                {
                    Height = qrSize,
                    Width = qrSize,
                    Margin = 1
                }
            };
            var pixelData = qrWriter.Write(url);

            // 计算二维码位置（居中上部）
            int qrX = (width - qrSize) / 2;
            int qrY = 10; // 顶部边距

            // 绘制二维码
            var pixels = pixelData.Pixels;
            for (int y = 0; y < pixelData.Height; y++)
            {
                for (int x = 0; x < pixelData.Width; x++)
                {
                    int offset = (y * pixelData.Width + x) * 4; // BGRA格式
                    byte pixelValue = pixels[offset]; // 取B通道值
                    var color = pixelValue == 0 ? SKColors.White : SKColors.Black; // 反转颜色以适应黑色背景

                    int drawX = qrX + x;
                    int drawY = qrY + y;

                    if (drawX >= 0 && drawX < width && drawY >= 0 && drawY < height)
                    {
                        using var paint = new SKPaint { Color = color };
                        canvas.DrawPoint(drawX, drawY, paint);
                    }
                }
            }

            // 绘制文本（如果提供）
            if (!string.IsNullOrEmpty(text))
            {
                int fontSize = Math.Min(18, textAreaHeight - 4);
                using var font = new SKFont(SKTypeface.Default, fontSize);
                using var paint = new SKPaint
                {
                    Color = SKColors.White,
                    IsAntialias = true
                };

                var textBounds = font.MeasureText(text);
                float textX = (width - textBounds) / 2;
                float textY = qrY + qrSize + textAreaHeight / 2 + fontSize / 2;

                if (textY < height - 5) // 确保文本在屏幕内
                {
                    canvas.DrawText(text, textX, textY, SKTextAlign.Left, font, paint);
                }
            }

            // 转换为RGB565格式
            using var image = surface.Snapshot();
            using var pixmap = image.PeekPixels();
            
            if (pixmap == null)
            {
                _logger.LogError("无法获取图像像素数据");
                return null;
            }

            return ConvertToRgb565(pixmap, width, height);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建QR码图像失败");
            return null;
        }
    }

    /// <summary>
    /// 创建IP地址显示图像数据（RGB565格式）
    /// </summary>
    public static byte[]? CreateIpDisplayImageData(string ipAddress, int width, int height)
    {
        try
        {
            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            var canvas = surface.Canvas;

            // 清除背景为黑色
            canvas.Clear(SKColors.Black);

            // 绘制IP地址文本
            using var paint = new SKPaint
            {
                Color = SKColors.White,
                IsAntialias = true
            };

            // 主标题
            using var titleFont = new SKFont(SKTypeface.Default, 24);
            canvas.DrawText("绿荫助手", width / 2, height / 3, SKTextAlign.Center, titleFont, paint);

            // IP地址
            using var ipFont = new SKFont(SKTypeface.Default, 20);
            canvas.DrawText($"IP: {ipAddress}", width / 2, height / 2, SKTextAlign.Center, ipFont, paint);

            // 状态信息
            using var statusFont = new SKFont(SKTypeface.Default, 16);
            canvas.DrawText("已连接到网络", width / 2, height * 2 / 3, SKTextAlign.Center, statusFont, paint);

            // 转换为RGB565格式
            using var image = surface.Snapshot();
            using var pixmap = image.PeekPixels();
            
            if (pixmap == null)
            {
                _logger.LogError("无法获取IP显示图像像素数据");
                return null;
            }

            return ConvertToRgb565(pixmap, width, height);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "创建IP显示图像失败");
            return null;
        }
    }

    /// <summary>
    /// 将SkiaSharp像素转换为RGB565格式
    /// </summary>
    private static byte[] ConvertToRgb565(SKPixmap pixmap, int width, int height)
    {
        var rgb565Data = new byte[width * height * 2];
        var pixelData = pixmap.GetPixels();

        unsafe
        {
            var pixels = (uint*)pixelData.ToPointer();
            
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var pixelIndex = y * width + x;
                    var pixel = pixels[pixelIndex];
                    
                    // 提取RGBA组件
                    var r = (byte)((pixel >> 16) & 0xFF);
                    var g = (byte)((pixel >> 8) & 0xFF);
                    var b = (byte)(pixel & 0xFF);
                    
                    // 转换为RGB565
                    var r565 = (ushort)((r >> 3) << 11);
                    var g565 = (ushort)((g >> 2) << 5);
                    var b565 = (ushort)(b >> 3);
                    var rgb565 = (ushort)(r565 | g565 | b565);
                    
                    // 存储RGB565数据（大端格式）
                    var dataIndex = pixelIndex * 2;
                    rgb565Data[dataIndex] = (byte)(rgb565 >> 8);
                    rgb565Data[dataIndex + 1] = (byte)(rgb565 & 0xFF);
                }
            }
        }

        return rgb565Data;
    }

    /// <summary>
    /// 获取热点网关IP地址
    /// </summary>
    public static string GetHotspotGatewayIp(string defaultIp)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            _logger.LogInformation("非Linux系统，返回默认IP: {DefaultIp}", defaultIp);
            return defaultIp;
        }

        try
        {
            // 在Linux系统中，尝试获取实际的网关IP
            var result = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = "-c \"ip route | grep wlan0 | grep scope | head -1 | awk '{print $9}'\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (result != null)
            {
                result.WaitForExit();
                var output = result.StandardOutput.ReadToEnd().Trim();
                if (!string.IsNullOrEmpty(output) && output.Contains("."))
                {
                    _logger.LogInformation("检测到热点网关IP: {GatewayIp}", output);
                    return output;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "获取热点网关IP失败，使用默认值");
        }

        return defaultIp;
    }

    /// <summary>
    /// 生成测试用的配网页面图片（用于非Linux环境）
    /// </summary>
    public static void GenerateTestConfigPageImage(string url, string gatewayIp, string outputPath = "config_page.png")
    {
        try
        {
            const int width = 800;
            const int height = 600;

            using var surface = SKSurface.Create(new SKImageInfo(width, height));
            var canvas = surface.Canvas;

            // 清除背景为白色
            canvas.Clear(SKColors.White);

            // 绘制边框
            using var borderPaint = new SKPaint
            {
                Color = SKColors.Black,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2
            };
            canvas.DrawRect(10, 10, width - 20, height - 20, borderPaint);

            // 绘制标题
            using var titlePaint = new SKPaint
            {
                Color = SKColors.DarkBlue,
                IsAntialias = true
            };
            using var titleFont = new SKFont(SKTypeface.Default, 32);
            canvas.DrawText("绿荫助手 WiFi 配置", width / 2, 80, SKTextAlign.Center, titleFont, titlePaint);

            // 绘制QR码 - 使用ZXing.Net
            var qrSize = 200;
            var qrX = (width - qrSize) / 2;
            var qrY = 120;
            
            var qrWriter = new BarcodeWriterPixelData
            {
                Format = BarcodeFormat.QR_CODE,
                Options = new QrCodeEncodingOptions
                {
                    Height = qrSize,
                    Width = qrSize,
                    Margin = 1
                }
            };
            var pixelData = qrWriter.Write(url);
            var pixels = pixelData.Pixels;

            // 绘制二维码
            for (int y = 0; y < pixelData.Height; y++)
            {
                for (int x = 0; x < pixelData.Width; x++)
                {
                    int offset = (y * pixelData.Width + x) * 4; // BGRA格式
                    byte pixelValue = pixels[offset]; // 取B通道值
                    var color = pixelValue == 0 ? SKColors.Black : SKColors.White;

                    int drawX = qrX + x;
                    int drawY = qrY + y;

                    using var paint = new SKPaint { Color = color };
                    canvas.DrawPoint(drawX, drawY, paint);
                }
            }

            // 绘制说明文本
            using var textPaint = new SKPaint
            {
                Color = SKColors.Black,
                IsAntialias = true
            };

            using var normalFont = new SKFont(SKTypeface.Default, 20);
            canvas.DrawText("扫描二维码或访问以下地址配置WiFi:", width / 2, 360, SKTextAlign.Center, normalFont, textPaint);
            
            using var urlFont = new SKFont(SKTypeface.Default, 18);
            textPaint.Color = SKColors.Blue;
            canvas.DrawText(url, width / 2, 390, SKTextAlign.Center, urlFont, textPaint);
            
            textPaint.Color = SKColors.Black;
            canvas.DrawText($"网关IP: {gatewayIp}", width / 2, 420, SKTextAlign.Center, normalFont, textPaint);
            
            canvas.DrawText("在浏览器中打开上述地址即可配置WiFi网络", width / 2, 480, SKTextAlign.Center, normalFont, textPaint);

            // 绘制提示信息
            textPaint.Color = SKColors.Gray;
            using var smallFont = new SKFont(SKTypeface.Default, 16);
            canvas.DrawText("此图片仅用于非Linux环境的测试显示", width / 2, 550, SKTextAlign.Center, smallFont, textPaint);

            // 保存图片
            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.OpenWrite(outputPath);
            data.SaveTo(stream);

            _logger.LogInformation("配网页面测试图片已保存到: {OutputPath}", outputPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "生成测试配网页面图片失败");
        }
    }
}