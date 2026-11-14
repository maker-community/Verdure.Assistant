namespace Verdure.Assistant.Core.Interfaces;

public enum ImageCaptureSource
{
    /// <summary>摄像头拍照</summary>
    Camera,

    /// <summary>截取主显示器屏幕</summary>
    Screen
}

/// <summary>已编码的图片（JPEG/PNG），宽高为编码后的像素尺寸</summary>
public sealed record CapturedImage(byte[] Data, string MimeType, int Width, int Height);

/// <summary>取图失败（没有摄像头、权限被拒绝、被其他程序占用等），消息可直接展示给用户</summary>
public sealed class ImageCaptureException : Exception
{
    public ImageCaptureException(string message, Exception? innerException = null) : base(message, innerException)
    {
    }
}

/// <summary>
/// 平台相关的取图服务：摄像头拍照与屏幕截图。实现负责把图片缩放到适合上传的大小。
/// </summary>
public interface IImageCaptureService
{
    bool IsSupported(ImageCaptureSource source);

    /// <summary>取一张图；失败抛出 <see cref="ImageCaptureException"/></summary>
    Task<CapturedImage> CaptureAsync(ImageCaptureSource source, CancellationToken cancellationToken = default);
}
