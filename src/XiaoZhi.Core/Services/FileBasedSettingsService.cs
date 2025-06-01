using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using XiaoZhi.Core.Interfaces;

namespace XiaoZhi.Core.Services
{
    /// <summary>
    /// 基于文件系统的设置服务实现（跨平台默认实现）
    /// 适用于控制台程序和不依赖特定平台存储的场景
    /// </summary>
    /// <typeparam name="T">设置对象类型</typeparam>
    public class FileBasedSettingsService<T> : ISettingsService<T> where T : class, new()
    {
        private readonly ILogger<FileBasedSettingsService<T>>? _logger;
        private readonly string _settingsFilePath;
        private readonly JsonSerializerOptions _jsonOptions;
        private T _currentSettings;

        public event EventHandler<T>? SettingsChanged;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="settingsFilePath">设置文件路径，如果为null则使用默认路径</param>
        /// <param name="logger">日志记录器</param>
        public FileBasedSettingsService(string? settingsFilePath = null, ILogger<FileBasedSettingsService<T>>? logger = null)
        {
            _logger = logger;
            
            // 如果未指定路径，使用默认路径
            _settingsFilePath = settingsFilePath ?? GetDefaultSettingsPath();
            
            // 配置JSON序列化选项
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };

            // 初始化当前设置
            _currentSettings = new T();
        }

        /// <summary>
        /// 获取默认设置文件路径
        /// </summary>
        /// <returns>默认设置文件路径</returns>
        private static string GetDefaultSettingsPath()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appFolder = Path.Combine(appDataPath, "XiaoZhi");
            
            // 确保目录存在
            Directory.CreateDirectory(appFolder);
            
            return Path.Combine(appFolder, $"{typeof(T).Name}.json");
        }

        /// <summary>
        /// 加载设置
        /// </summary>
        /// <returns>设置对象</returns>
        public async Task<T> LoadSettingsAsync()
        {
            try
            {
                if (!File.Exists(_settingsFilePath))
                {
                    _logger?.LogInformation("Settings file not found at {Path}, creating default settings", _settingsFilePath);
                    _currentSettings = new T();
                    await SaveSettingsAsync(_currentSettings);
                    return _currentSettings;
                }

                var jsonContent = await File.ReadAllTextAsync(_settingsFilePath);
                var settings = JsonSerializer.Deserialize<T>(jsonContent, _jsonOptions);
                
                if (settings == null)
                {
                    _logger?.LogWarning("Failed to deserialize settings, using default");
                    _currentSettings = new T();
                }
                else
                {
                    _currentSettings = settings;
                }

                _logger?.LogInformation("Settings loaded successfully from {Path}", _settingsFilePath);
                return _currentSettings;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to load settings from {Path}", _settingsFilePath);
                _currentSettings = new T();
                return _currentSettings;
            }
        }

        /// <summary>
        /// 保存设置
        /// </summary>
        /// <param name="settings">要保存的设置对象</param>
        public async Task SaveSettingsAsync(T settings)
        {
            try
            {
                // 确保目录存在
                var directory = Path.GetDirectoryName(_settingsFilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var jsonContent = JsonSerializer.Serialize(settings, _jsonOptions);
                await File.WriteAllTextAsync(_settingsFilePath, jsonContent);
                
                _currentSettings = settings;
                _logger?.LogInformation("Settings saved successfully to {Path}", _settingsFilePath);
                
                // 触发设置变化事件
                SettingsChanged?.Invoke(this, settings);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to save settings to {Path}", _settingsFilePath);
                throw;
            }
        }

        /// <summary>
        /// 导出设置到文件
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <param name="settings">要导出的设置对象</param>
        /// <returns>是否导出成功</returns>
        public async Task<bool> ExportSettingsAsync(string filePath, T settings)
        {
            try
            {
                // 确保目录存在
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var jsonContent = JsonSerializer.Serialize(settings, _jsonOptions);
                await File.WriteAllTextAsync(filePath, jsonContent);
                
                _logger?.LogInformation("Settings exported successfully to {Path}", filePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to export settings to {Path}", filePath);
                return false;
            }
        }

        /// <summary>
        /// 从文件导入设置
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>导入的设置对象，失败时返回null</returns>
        public async Task<T?> ImportSettingsAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    _logger?.LogWarning("Import file not found at {Path}", filePath);
                    return null;
                }

                var jsonContent = await File.ReadAllTextAsync(filePath);
                var settings = JsonSerializer.Deserialize<T>(jsonContent, _jsonOptions);
                
                if (settings == null)
                {
                    _logger?.LogWarning("Failed to deserialize settings from {Path}", filePath);
                    return null;
                }

                _logger?.LogInformation("Settings imported successfully from {Path}", filePath);
                return settings;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to import settings from {Path}", filePath);
                return null;
            }
        }

        /// <summary>
        /// 重置为默认设置
        /// </summary>
        public async Task ResetToDefaultAsync()
        {
            try
            {
                var defaultSettings = new T();
                await SaveSettingsAsync(defaultSettings);
                _logger?.LogInformation("Settings reset to default values");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to reset settings to default");
                throw;
            }
        }

        /// <summary>
        /// 获取当前设置（同步）
        /// </summary>
        /// <returns>当前设置对象</returns>
        public T GetCurrentSettings()
        {
            return _currentSettings;
        }
    }
}
