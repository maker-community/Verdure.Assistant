using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Verdure.Assistant.Core.Interfaces;

namespace Verdure.Assistant.MAUI.Services
{
    /// <summary>
    /// MAUI 表情资源解析器
    /// 提供固定的表情资源映射，无需文件系统访问
    /// </summary>
    public class MauiEmotionAssetResolver : IEmotionAssetResolver
    {
        private readonly ILogger<MauiEmotionAssetResolver> _logger;
        private readonly Dictionary<string, string> _emotionMappings;
        private readonly HashSet<string> _availableEmotions;

        public MauiEmotionAssetResolver(ILogger<MauiEmotionAssetResolver> logger)
        {
            _logger = logger;
            _emotionMappings = InitializeEmotionMappings();
            _availableEmotions = InitializeAvailableEmotions();
        }

        public Task<IEnumerable<EmotionAsset>> ResolveAssetsAsync(string emotionType)
        {
            var assets = new List<EmotionAsset>();
            var normalizedEmotion = NormalizeEmotionName(emotionType);

            _logger.LogDebug("Resolving assets for emotion: {EmotionType} (normalized: {NormalizedEmotion})", 
                emotionType, normalizedEmotion);

            // 如果是可用的表情，添加GIF资源
            if (_availableEmotions.Contains(normalizedEmotion))
            {
                assets.Add(new EmotionAsset
                {
                    Path = $"Emotions/{normalizedEmotion}.gif",
                    Type = "gif",
                    Priority = 100, // GIF优先级最高
                    IsAvailable = true,
                    Metadata = new Dictionary<string, object>
                    {
                        ["Source"] = "MAUI AppPackage",
                        ["Platform"] = "MAUI",
                        ["EmotionType"] = normalizedEmotion
                    }
                });
            }

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

            return Task.FromResult<IEnumerable<EmotionAsset>>(availableAssets);
        }

        public Task<EmotionAsset?> GetPreferredAssetAsync(string emotionType, string rendererType)
        {
            var normalizedEmotion = NormalizeEmotionName(emotionType);
            
            // 如果请求GIF渲染器且表情可用，返回GIF资源
            if (rendererType.Equals("gif", StringComparison.OrdinalIgnoreCase) && 
                _availableEmotions.Contains(normalizedEmotion))
            {
                var asset = new EmotionAsset
                {
                    Path = $"Emotions/{normalizedEmotion}.gif",
                    Type = "gif",
                    Priority = 100,
                    IsAvailable = true,
                    Metadata = new Dictionary<string, object>
                    {
                        ["Source"] = "MAUI AppPackage",
                        ["Platform"] = "MAUI",
                        ["EmotionType"] = normalizedEmotion
                    }
                };
                
                _logger.LogDebug("Preferred asset for {EmotionType} with renderer {RendererType}: {AssetPath}", 
                    emotionType, rendererType, asset.Path);
                    
                return Task.FromResult<EmotionAsset?>(asset);
            }
            
            // 返回Emoji后备
            var emojiAsset = new EmotionAsset
            {
                Path = GetEmotionEmoji(normalizedEmotion),
                Type = "emoji",
                Priority = 1,
                IsAvailable = true,
                Metadata = new Dictionary<string, object>
                {
                    ["Source"] = "Emoji",
                    ["Platform"] = "MAUI"
                }
            };
            
            return Task.FromResult<EmotionAsset?>(emojiAsset);
        }

        public Task<bool> HasAssetAsync(string emotionType)
        {
            var normalizedEmotion = NormalizeEmotionName(emotionType);
            // 总是返回true，因为至少有Emoji后备
            return Task.FromResult(true);
        }

        public Task<IEnumerable<string>> GetAvailableEmotionsAsync()
        {
            _logger.LogDebug("Found {Count} available emotions", _availableEmotions.Count);
            return Task.FromResult<IEnumerable<string>>(_availableEmotions);
        }

        private HashSet<string> InitializeAvailableEmotions()
        {
            // 定义所有可用的表情（与Emotions目录中的GIF文件对应）
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "angry", "confident", "confused", "cool", "crying", "delicious",
                "embarrassed", "funny", "happy", "kissy", "laughing", "loving",
                "neutral", "relaxed", "sad", "shocked", "silly", "sleepy",
                "surprised", "thinking", "winking"
            };
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
