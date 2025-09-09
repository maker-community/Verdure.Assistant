using System.Collections.Generic;
using System.Threading.Tasks;

namespace Verdure.Assistant.Core.Interfaces
{
    /// <summary>
    /// 表情资源解析器接口
    /// </summary>
    public interface IEmotionAssetResolver
    {
        /// <summary>
        /// 解析指定表情的所有可用资源
        /// </summary>
        Task<IEnumerable<EmotionAsset>> ResolveAssetsAsync(string emotionType);

        /// <summary>
        /// 获取指定渲染器类型的首选资源
        /// </summary>
        Task<EmotionAsset?> GetPreferredAssetAsync(string emotionType, string rendererType);

        /// <summary>
        /// 检查指定表情是否有可用资源
        /// </summary>
        Task<bool> HasAssetAsync(string emotionType);

        /// <summary>
        /// 获取所有可用的表情类型
        /// </summary>
        Task<IEnumerable<string>> GetAvailableEmotionsAsync();
    }

    /// <summary>
    /// 表情资源描述
    /// </summary>
    public class EmotionAsset
    {
        public string Path { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // "gif", "lottie", "emoji", "video"
        public int Priority { get; set; } = 0;
        public Dictionary<string, object> Metadata { get; set; } = new();
        public bool IsAvailable { get; set; } = true;
    }
}
