using SkiaSharp;
using Verdure.Assistant.Api.IoTDevice;
using Xunit;

namespace Verdure.Assistant.Api.Tests;

public class ST7789ImageTests
{
    [Fact]
    public void GetImageBytes_ConvertsColorsToBigEndianRgb565()
    {
        using var bitmap = new SKBitmap(3, 2);
        bitmap.SetPixel(0, 0, SKColors.Red);
        bitmap.SetPixel(1, 0, SKColors.Lime);
        bitmap.SetPixel(2, 0, SKColors.Blue);
        bitmap.SetPixel(0, 1, SKColors.White);
        bitmap.SetPixel(1, 1, SKColors.Black);
        bitmap.SetPixel(2, 1, new SKColor(0x12, 0x34, 0x56));

        Assert.Equal(
            new byte[] { 0xF8, 0x00, 0x07, 0xE0, 0x00, 0x1F, 0xFF, 0xFF, 0x00, 0x00, 0x11, 0xAA },
            ST7789Display.GetImageBytes(bitmap));
    }

    [Fact]
    public void GetImageBytes_CropsEachRowToRequestedSize()
    {
        using var bitmap = new SKBitmap(3, 2);
        bitmap.SetPixel(0, 0, SKColors.Red);
        bitmap.SetPixel(1, 0, SKColors.Lime);
        bitmap.SetPixel(2, 0, SKColors.Blue);
        bitmap.SetPixel(0, 1, SKColors.White);
        bitmap.SetPixel(1, 1, SKColors.Black);
        bitmap.SetPixel(2, 1, SKColors.Blue);

        Assert.Equal(new byte[] { 0xF8, 0x00, 0x07, 0xE0, 0xFF, 0xFF, 0x00, 0x00 },
            ST7789Display.GetImageBytes(bitmap, 2, 2));
    }

    [Fact]
    public void GetImageBytes_RejectsInvalidDimensions()
    {
        using var bitmap = new SKBitmap(2, 2);
        Assert.Throws<ArgumentOutOfRangeException>(() => ST7789Display.GetImageBytes(bitmap, 3, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ST7789Display.GetImageBytes(bitmap, 1, 0));
    }
}
