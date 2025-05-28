using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Storage;

namespace XiaoZhi.WinUI.Views
{
    /// <summary>
    /// Enhanced Emotion Manager for handling GIF animations and emotion display
    /// Similar to py-xiaozhi emotion system
    /// </summary>
    public class EmotionManager
    {
        private readonly ILogger<EmotionManager>? _logger;
        private readonly Dictionary<string, string> _emotionPaths = new();
        private readonly Dictionary<string, string> _emotionEmojis = new();
        private bool _isInitialized = false;

        public EmotionManager()
        {
            _logger = App.GetService<ILogger<EmotionManager>>();
            InitializeEmotionMappings();
        }

        public async Task InitializeAsync()
        {
            if (_isInitialized) return;

            try
            {
                await LoadEmotionAssetsAsync();
                _isInitialized = true;
                _logger?.LogInformation("EmotionManager initialized successfully");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to initialize EmotionManager");
                throw;
            }
        }

        private void InitializeEmotionMappings()
        {
            // Emotion to emoji mappings (fallback when GIF not available)
            _emotionEmojis = new Dictionary<string, string>
            {
                ["neutral"] = "😊",
                ["happy"] = "😄",
                ["laughing"] = "😂",
                ["funny"] = "🤣",
                ["sad"] = "😢",
                ["angry"] = "😠",
                ["crying"] = "😭",
                ["loving"] = "😍",
                ["embarrassed"] = "😳",
                ["surprised"] = "😲",
                ["shocked"] = "😱",
                ["thinking"] = "🤔",
                ["winking"] = "😉",
                ["cool"] = "😎",
                ["relaxed"] = "😌",
                ["delicious"] = "😋",
                ["kissy"] = "😘",
                ["confident"] = "😏",
                ["sleepy"] = "😴",
                ["silly"] = "🤪",
                ["confused"] = "😕",
                ["talking"] = "🗣️"
            };
        }

        private async Task LoadEmotionAssetsAsync()
        {
            try
            {
                // Get the package installation folder
                var packageFolder = Package.Current.InstalledLocation;
                var assetsFolder = await packageFolder.GetFolderAsync("Assets");
                var emotionsFolder = await assetsFolder.GetFolderAsync("Emotions");

                // Get all GIF files
                var files = await emotionsFolder.GetFilesAsync();
                
                foreach (var file in files)
                {
                    if (file.FileType.ToLower() == ".gif")
                    {
                        var emotionName = Path.GetFileNameWithoutExtension(file.Name);
                        _emotionPaths[emotionName] = file.Path;
                        _logger?.LogDebug($"Loaded emotion asset: {emotionName} -> {file.Path}");
                    }
                }

                _logger?.LogInformation($"Loaded {_emotionPaths.Count} emotion assets");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Could not load emotion assets from package, using emoji fallbacks");
                
                // Fallback: try to load from application directory
                await TryLoadFromApplicationDirectoryAsync();
            }
        }

        private async Task TryLoadFromApplicationDirectoryAsync()
        {
            try
            {
                var appFolder = await StorageFolder.GetFolderFromPathAsync(
                    Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "");
                var assetsFolder = await appFolder.GetFolderAsync("Assets");
                var emotionsFolder = await assetsFolder.GetFolderAsync("Emotions");

                var files = await emotionsFolder.GetFilesAsync();
                
                foreach (var file in files)
                {
                    if (file.FileType.ToLower() == ".gif")
                    {
                        var emotionName = Path.GetFileNameWithoutExtension(file.Name);
                        _emotionPaths[emotionName] = file.Path;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Could not load emotion assets from application directory either");
            }
        }

        public async Task<string?> GetEmotionImageAsync(string emotion)
        {
            if (!_isInitialized)
            {
                await InitializeAsync();
            }

            // Normalize emotion name
            emotion = emotion.ToLower().Trim();

            // Try to get the exact match first
            if (_emotionPaths.TryGetValue(emotion, out var path))
            {
                if (File.Exists(path))
                {
                    return path;
                }
            }

            // Try common emotion mappings
            var mappedEmotion = MapEmotionName(emotion);
            if (!string.IsNullOrEmpty(mappedEmotion) && _emotionPaths.TryGetValue(mappedEmotion, out var mappedPath))
            {
                if (File.Exists(mappedPath))
                {
                    return mappedPath;
                }
            }

            _logger?.LogDebug($"No GIF found for emotion: {emotion}");
            return null;
        }

        public string GetEmotionEmoji(string emotion)
        {
            // Normalize emotion name
            emotion = emotion.ToLower().Trim();

            // Try exact match first
            if (_emotionEmojis.TryGetValue(emotion, out var emoji))
            {
                return emoji;
            }

            // Try mapped emotion
            var mappedEmotion = MapEmotionName(emotion);
            if (!string.IsNullOrEmpty(mappedEmotion) && _emotionEmojis.TryGetValue(mappedEmotion, out var mappedEmoji))
            {
                return mappedEmoji;
            }

            // Default fallback
            return "😊";
        }

        private string? MapEmotionName(string emotion)
        {
            // Map similar emotion names to our standard set
            return emotion switch
            {
                "smile" or "smiling" => "happy",
                "laugh" or "laughter" => "laughing",
                "joy" or "joyful" => "happy",
                "upset" or "down" => "sad",
                "mad" or "furious" => "angry",
                "love" or "adore" => "loving",
                "shy" or "bashful" => "embarrassed",
                "amazed" or "astonished" => "surprised",
                "ponder" or "pondering" => "thinking",
                "wink" => "winking",
                "awesome" or "great" => "cool",
                "calm" or "peaceful" => "relaxed",
                "tasty" or "yummy" => "delicious",
                "kiss" or "kissing" => "kissy",
                "proud" or "swagger" => "confident",
                "tired" or "sleepy" => "sleepy",
                "crazy" or "wild" => "silly",
                "puzzled" or "bewildered" => "confused",
                "speaking" or "saying" => "talking",
                _ => null
            };
        }

        public bool HasEmotionAsset(string emotion)
        {
            emotion = emotion.ToLower().Trim();
            return _emotionPaths.ContainsKey(emotion) || 
                   (!string.IsNullOrEmpty(MapEmotionName(emotion)) && 
                    _emotionPaths.ContainsKey(MapEmotionName(emotion)!));
        }

        public IEnumerable<string> GetAvailableEmotions()
        {
            return _emotionPaths.Keys;
        }

        public void ClearCache()
        {
            // Future: implement image cache clearing if needed
            _logger?.LogInformation("Emotion cache cleared");
        }
    }
}
