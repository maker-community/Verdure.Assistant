using System;
using System.Threading.Tasks;

namespace Verdure.Assistant.Core.Services
{
    /// <summary>
    /// 相机服务接口 - 支持不同平台的拍照实现
    /// </summary>
    public interface ICameraService
    {
        /// <summary>
        /// 拍摄照片并返回图片字节数组
        /// </summary>
        /// <param name="settings">拍照设置</param>
        /// <returns>图片的字节数组</returns>
        Task<byte[]> CapturePhotoAsync(CameraSettings? settings = null);

        /// <summary>
        /// 检查相机是否可用
        /// </summary>
        /// <returns>相机可用性</returns>
        Task<bool> IsAvailableAsync();

        /// <summary>
        /// 获取支持的分辨率列表
        /// </summary>
        /// <returns>分辨率列表</returns>
        Task<string[]> GetSupportedResolutionsAsync();

        /// <summary>
        /// 获取相机信息
        /// </summary>
        /// <returns>相机信息</returns>
        Task<CameraInfo> GetCameraInfoAsync();
    }

    /// <summary>
    /// 相机设置
    /// </summary>
    public class CameraSettings
    {
        public int Width { get; set; } = 1280;
        public int Height { get; set; } = 720;
        public int SkipFrames { get; set; } = 20;
        public int JpegQuality { get; set; } = 85;
        public string Device { get; set; } = "/dev/video0";
        public bool AddTimestamp { get; set; } = false;
        public string TimestampFormat { get; set; } = "%Y-%m-%d %H:%M:%S";
    }

    /// <summary>
    /// 相机信息
    /// </summary>
    public class CameraInfo
    {
        public string Name { get; set; } = "";
        public string Device { get; set; } = "";
        public string[] SupportedFormats { get; set; } = Array.Empty<string>();
        public string[] SupportedResolutions { get; set; } = Array.Empty<string>();
        public bool IsAvailable { get; set; }
        public string Platform { get; set; } = "";
    }
}
