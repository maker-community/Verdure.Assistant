using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Verdure.Assistant.Core.Interfaces;

namespace Verdure.Assistant.Core.Services
{
    /// <summary>
    /// 默认表情资源解析器实现
    /// </summary>
    public class DefaultEmotionAssetResolver : IEmotionAssetResolver
    {
        private readonly ILogger<DefaultEmotionAssetResolver> _logger;
        private readonly Dictionary<string, string> _emotionMappings;
        private readonly List<string> _searchPaths;

        public DefaultEmotionAssetResolver(ILogger<DefaultEmotionAssetResolver> logger)
        {
            _logger = logger;
            _emotionMappings = InitializeEmotionMappings();
            _searchPaths = InitializeSearchPaths();
        }

        public async Task<IEnumerable<EmotionAsset>> ResolveAssetsAsync(string emotionType)
        {
            var assets = new List<EmotionAsset>();
            var normalizedEmotion = NormalizeEmotionName(emotionType);

            foreach (var searchPath in _searchPaths)
            {
                if (!Directory.Exists(searchPath))
                    continue;

                // 查找GIF文件
                var gifFiles = Directory.GetFiles(searchPath, $"{normalizedEmotion}.gif", SearchOption.AllDirectories);
                foreach (var gifFile in gifFiles)
                {
                    assets.Add(new EmotionAsset
                    {
                        Path = gifFile,
                        Type = "gif",
                        Priority = 100, // GIF优先级最高
                        IsAvailable = File.Exists(gifFile)
                    });
                }

                // 查找Lottie文件
                var lottieFiles = Directory.GetFiles(searchPath, $"{normalizedEmotion}*.json", SearchOption.AllDirectories);
                foreach (var lottieFile in lottieFiles)
                {
                    if (Path.GetFileName(lottieFile).Contains("lottie", StringComparison.OrdinalIgnoreCase))
                    {
                        assets.Add(new EmotionAsset
                        {
                            Path = lottieFile,
                            Type = "lottie",
                            Priority = 80,
                            IsAvailable = File.Exists(lottieFile)
                        });
                    }
                }

                // 查找视频文件
                var videoExtensions = new[] { ".mp4", ".webm", ".avi" };
                foreach (var ext in videoExtensions)
                {
                    var videoFiles = Directory.GetFiles(searchPath, $"{normalizedEmotion}{ext}", SearchOption.AllDirectories);
                    foreach (var videoFile in videoFiles)
                    {
                        assets.Add(new EmotionAsset
                        {
                            Path = videoFile,
                            Type = "video",
                            Priority = 60,
                            IsAvailable = File.Exists(videoFile)
                        });
                    }
                }
            }

            // 总是添加Emoji作为后备
            assets.Add(new EmotionAsset
            {
                Path = GetEmotionEmoji(normalizedEmotion),
                Type = "emoji",
                Priority = 1, // 最低优先级
                IsAvailable = true
            });

            await Task.CompletedTask;
            return assets.OrderByDescending(a => a.Priority).Where(a => a.IsAvailable);
        }

        public async Task<EmotionAsset?> GetPreferredAssetAsync(string emotionType, string rendererType)
        {
            var assets = await ResolveAssetsAsync(emotionType);
            return assets.FirstOrDefault(a => a.Type.Equals(rendererType, StringComparison.OrdinalIgnoreCase));
        }

        public async Task<bool> HasAssetAsync(string emotionType)
        {
            var assets = await ResolveAssetsAsync(emotionType);
            return assets.Any();
        }

        public async Task<IEnumerable<string>> GetAvailableEmotionsAsync()
        {
            var emotions = new HashSet<string>();

            foreach (var searchPath in _searchPaths)
            {
                if (!Directory.Exists(searchPath))
                    continue;

                var files = Directory.GetFiles(searchPath, "*.*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    var fileName = Path.GetFileNameWithoutExtension(file);
                    var extension = Path.GetExtension(file).ToLowerInvariant();

                    if (extension == ".gif" || extension == ".json" || extension == ".mp4")
                    {
                        // 移除Lottie后缀
                        fileName = fileName.Replace(".lottie", "").Replace(".mp4", "");
                        emotions.Add(fileName.ToLowerInvariant());
                    }
                }
            }

            // 添加基础表情
            emotions.UnionWith(_emotionMappings.Keys);

            await Task.CompletedTask;
            return emotions;
        }

        private string NormalizeEmotionName(string emotionType)
        {
            var normalized = emotionType.ToLowerInvariant().Trim();
            return _emotionMappings.TryGetValue(normalized, out var mapped) ? mapped : normalized;
        }

        private string GetEmotionEmoji(string emotionType)
        {
            return emotionType switch
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
                _ => "😊"
            };
        }

        private Dictionary<string, string> InitializeEmotionMappings()
        {
            return new Dictionary<string, string>
            {
                ["smile"] = "happy",
                ["joy"] = "happy",
                ["laugh"] = "happy",
                ["upset"] = "sad",
                ["down"] = "sad",
                ["mad"] = "angry",
                ["furious"] = "angry",
                ["amazed"] = "surprised",
                ["shocked"] = "surprised",
                ["puzzled"] = "confused",
                ["bewildered"] = "confused",
                ["ponder"] = "thinking",
                ["pondering"] = "thinking",
                ["talking"] = "speaking",
                ["saying"] = "speaking"
            };
        }

        private List<string> InitializeSearchPaths()
        {
            var paths = new List<string>();

            try
            {
                // 应用程序目录下的Assets/Emotions
                var appPath = AppDomain.CurrentDomain.BaseDirectory;
                paths.Add(Path.Combine(appPath, "Assets", "Emotions"));

                // 工作目录下的Assets/Emotions
                var workingPath = Directory.GetCurrentDirectory();
                paths.Add(Path.Combine(workingPath, "Assets", "Emotions"));

                // 用户文档目录
                var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                paths.Add(Path.Combine(documentsPath, "VerdureAssistant", "Emotions"));

                _logger.LogDebug("Initialized emotion asset search paths: {Paths}", string.Join("; ", paths));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize search paths");
            }

            return paths;
        }
    }
}
