using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Verdure.Assistant.Core.Interfaces;

namespace Verdure.Assistant.MAUI.Services
{
    /// <summary>
    /// MAUI 表情资源解析器
    /// 专门处理MAUI应用包中的表情资源
    /// </summary>
    public class MauiEmotionAssetResolver : IEmotionAssetResolver
    {
        private readonly ILogger<MauiEmotionAssetResolver> _logger;
        private readonly MauiResourceService _resourceService;
        private readonly Dictionary<string, string> _emotionMappings;

        public MauiEmotionAssetResolver(
            ILogger<MauiEmotionAssetResolver> logger,
            MauiResourceService resourceService)
        {
            _logger = logger;
            _resourceService = resourceService;
            _emotionMappings = InitializeEmotionMappings();
        }

        public async Task<IEnumerable<EmotionAsset>> ResolveAssetsAsync(string emotionType)
        {
            var assets = new List<EmotionAsset>();
            var normalizedEmotion = NormalizeEmotionName(emotionType);

            _logger.LogDebug("Resolving assets for emotion: {EmotionType} (normalized: {NormalizedEmotion})", 
                emotionType, normalizedEmotion);

            // 在MAUI中查找GIF资源
            await FindMauiGifAssets(normalizedEmotion, assets);

            // 总是添加Emoji作为后备
            assets.Add(new EmotionAsset
            {
                Path = GetEmotionEmoji(normalizedEmotion),
                Type = "emoji",
                Priority = 1, // 最低优先级
                IsAvailable = true,
                Metadata = new Dictionary<string, object>
                {
                    ["Source"] = "Emoji",
                    ["Platform"] = "MAUI"
                }
            });

            var availableAssets = assets.Where(a => a.IsAvailable).OrderByDescending(a => a.Priority).ToList();
            
            _logger.LogDebug("Found {Count} available assets for emotion {EmotionType}", 
                availableAssets.Count, emotionType);

            return availableAssets;
        }

        public async Task<EmotionAsset?> GetPreferredAssetAsync(string emotionType, string rendererType)
        {
            var assets = await ResolveAssetsAsync(emotionType);
            var preferredAsset = assets.FirstOrDefault(a => a.Type.Equals(rendererType, StringComparison.OrdinalIgnoreCase));
            
            _logger.LogDebug("Preferred asset for {EmotionType} with renderer {RendererType}: {AssetPath}", 
                emotionType, rendererType, preferredAsset?.Path ?? "Not found");
                
            return preferredAsset;
        }

        public async Task<bool> HasAssetAsync(string emotionType)
        {
            var assets = await ResolveAssetsAsync(emotionType);
            return assets.Any(a => a.IsAvailable);
        }

        public async Task<IEnumerable<string>> GetAvailableEmotionsAsync()
        {
            var availableEmotions = new List<string>();

            // 检查所有已知表情的GIF资源
            var knownEmotions = new[]
            {
                "angry", "confident", "confused", "cool", "crying", "delicious", 
                "embarrassed", "funny", "happy", "kissy", "laughing", "loving", 
                "neutral", "relaxed", "sad", "shocked", "silly", "sleepy", 
                "surprised", "thinking", "winking", "listening", "speaking"
            };

            foreach (var emotion in knownEmotions)
            {
                if (await CheckMauiGifExists(emotion))
                {
                    availableEmotions.Add(emotion);
                }
            }

            _logger.LogDebug("Found {Count} available emotions in MAUI resources", availableEmotions.Count);
            return availableEmotions;
        }

        private async Task FindMauiGifAssets(string emotionName, List<EmotionAsset> assets)
        {
            // MAUI中的可能路径
            var possiblePaths = new[]
            {
                $"{emotionName}.gif",
                $"Emotions/{emotionName}.gif",
                $"emotions/{emotionName}.gif",
                $"Images/Emotions/{emotionName}.gif",
                $"images/emotions/{emotionName}.gif"
            };

            foreach (var path in possiblePaths)
            {
                if (await CheckMauiResourceExists(path))
                {
                    assets.Add(new EmotionAsset
                    {
                        Path = path,
                        Type = "gif",
                        Priority = 100, // GIF优先级最高
                        IsAvailable = true,
                        Metadata = new Dictionary<string, object>
                        {
                            ["Source"] = "MAUI AppPackage",
                            ["Platform"] = "MAUI",
                            ["OriginalPath"] = path
                        }
                    });

                    _logger.LogDebug("Found MAUI GIF asset: {Path}", path);
                    break; // 找到第一个就停止
                }
            }
        }

        private async Task<bool> CheckMauiGifExists(string emotionName)
        {
            var possiblePaths = new[]
            {
                $"{emotionName}.gif",
                $"Emotions/{emotionName}.gif",
                $"Images/Emotions/{emotionName}.gif"
            };

            foreach (var path in possiblePaths)
            {
                if (await CheckMauiResourceExists(path))
                {
                    return true;
                }
            }

            return false;
        }

        private async Task<bool> CheckMauiResourceExists(string resourcePath)
        {
            try
            {
                using var stream = await Microsoft.Maui.Storage.FileSystem.Current.OpenAppPackageFileAsync(resourcePath);
                return stream != null;
            }
            catch (Exception ex)
            {
                _logger.LogTrace("Resource not found: {ResourcePath} - {Error}", resourcePath, ex.Message);
                return false;
            }
        }

        private string NormalizeEmotionName(string emotionType)
        {
            if (string.IsNullOrEmpty(emotionType))
                return "neutral";

            var normalized = emotionType.ToLowerInvariant().Trim();

            // 应用映射表
            if (_emotionMappings.TryGetValue(normalized, out var mapped))
            {
                return mapped;
            }

            return normalized;
        }

        private Dictionary<string, string> InitializeEmotionMappings()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // 状态映射
                ["listening"] = "thinking",
                ["speaking"] = "happy",
                ["talking"] = "happy",
                ["processing"] = "thinking",
                ["idle"] = "neutral",
                ["waiting"] = "neutral",
                
                // 情感映射
                ["joy"] = "happy",
                ["excited"] = "happy",
                ["cheerful"] = "happy",
                ["pleased"] = "happy",
                ["content"] = "relaxed",
                ["calm"] = "relaxed",
                ["peaceful"] = "relaxed",
                
                ["upset"] = "sad",
                ["disappointed"] = "sad",
                ["melancholy"] = "sad",
                ["grief"] = "crying",
                
                ["furious"] = "angry",
                ["mad"] = "angry",
                ["irritated"] = "angry",
                ["annoyed"] = "confused",
                
                ["amazed"] = "surprised",
                ["astonished"] = "shocked",
                ["startled"] = "shocked",
                
                ["puzzled"] = "confused",
                ["uncertain"] = "thinking",
                ["contemplating"] = "thinking",
                ["pondering"] = "thinking",
                
                ["sleepy"] = "sleepy",
                ["tired"] = "sleepy",
                ["drowsy"] = "sleepy",
                
                ["playful"] = "silly",
                ["mischievous"] = "winking",
                ["flirty"] = "kissy"
            };
        }

        private string GetEmotionEmoji(string emotionType)
        {
            return emotionType.ToLowerInvariant() switch
            {
                "neutral" => "😊",
                "happy" => "😄",
                "sad" => "😢",
                "angry" => "😠",
                "surprised" => "😲",
                "confused" => "😕",
                "thinking" => "🤔",
                "speaking" => "🗣️",
                "listening" => "👂",
                "laughing" => "😂",
                "loving" => "😍",
                "embarrassed" => "😳",
                "shocked" => "😱",
                "winking" => "😉",
                "cool" => "😎",
                "relaxed" => "😌",
                "sleepy" => "😴",
                "silly" => "🤪",
                "confident" => "😎",
                "crying" => "😭",
                "delicious" => "😋",
                "funny" => "🤣",
                "kissy" => "😘",
                _ => "😊"
            };
        }
    }
}
