using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Verdure.Assistant.Core.Interfaces;

namespace Verdure.Assistant.WinUI.Services
{
    /// <summary>
    /// 基于 Windows ApplicationData 的设置服务实�?
    /// 专门�?WinUI 应用优化，使�?Windows 平台的本地存储机�?
    /// </summary>
    /// <typeparam name="T">设置对象类型</typeparam>
    public class WindowsSettingsService<T> : ISettingsService<T> where T : class, new()
    {
        private readonly ILogger<WindowsSettingsService<T>>? _logger;
        private readonly ApplicationDataContainer _localSettings;
        private readonly string _settingsKey;
        private readonly JsonSerializerOptions _jsonOptions;
        private T _currentSettings;

        public event EventHandler<T>? SettingsChanged;

        /// <summary>
        /// 构造函�?
        /// </summary>
        /// <param name="logger">日志记录�?/param>
        public WindowsSettingsService(ILogger<WindowsSettingsService<T>>? logger = null)
        {
            _logger = logger;
            _localSettings = ApplicationData.Current.LocalSettings;
            _settingsKey = $"{typeof(T).Name}_Settings";
            
            // 配置JSON序列化选项
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            // 初始化当前设�?
            _currentSettings = new T();
        }

        /// <summary>
        /// 加载设置
        /// </summary>
        /// <returns>设置对象</returns>
        public async Task<T> LoadSettingsAsync()
        {
            try
            {
                if (_localSettings.Values.ContainsKey(_settingsKey))
                {
                    var jsonString = _localSettings.Values[_settingsKey] as string;
                    if (!string.IsNullOrEmpty(jsonString))
                    {
                        var settings = JsonSerializer.Deserialize<T>(jsonString, _jsonOptions);
                        if (settings != null)
                        {
                            _currentSettings = settings;
                            _logger?.LogInformation("Settings loaded successfully from ApplicationData");
                            return _currentSettings;
                        }
                    }
                }

                _logger?.LogInformation("No existing settings found, creating default settings");
                _currentSettings = new T();
                await SaveSettingsAsync(_currentSettings);
                return _currentSettings;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to load settings from ApplicationData");
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
                var jsonString = JsonSerializer.Serialize(settings, _jsonOptions);
                _localSettings.Values[_settingsKey] = jsonString;
                
                _currentSettings = settings;
                _logger?.LogInformation("Settings saved successfully to ApplicationData");
                
                // 触发设置变化事件
                SettingsChanged?.Invoke(this, settings);
                
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to save settings to ApplicationData");
                throw;
            }
        }

        /// <summary>
        /// 导出设置到文�?
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <param name="settings">要导出的设置对象</param>
        /// <returns>是否导出成功</returns>
        public async Task<bool> ExportSettingsAsync(string filePath, T settings)
        {
            try
            {
                // 如果没有指定文件路径，使用文件选择�?
                if (string.IsNullOrEmpty(filePath))
                {
                    var picker = new FileSavePicker();
                    picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                    picker.FileTypeChoices.Add("JSON 文件", new[] { ".json" });
                    picker.SuggestedFileName = $"{typeof(T).Name}_Settings";

                    // 获取当前窗口句柄
                    var window = App.MainWindow;
                    var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

                    var file = await picker.PickSaveFileAsync();
                    if (file == null)
                    {
                        return false; // 用户取消了保�?
                    }
                    filePath = file.Path;
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
        /// 从文件导入设�?
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>导入的设置对象，失败时返回null</returns>
        public async Task<T?> ImportSettingsAsync(string filePath)
        {
            try
            {
                // 如果没有指定文件路径，使用文件选择�?
                if (string.IsNullOrEmpty(filePath))
                {
                    var picker = new FileOpenPicker();
                    picker.ViewMode = PickerViewMode.List;
                    picker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                    picker.FileTypeFilter.Add(".json");

                    // 获取当前窗口句柄
                    var window = App.MainWindow;
                    var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, hWnd);

                    var file = await picker.PickSingleFileAsync();
                    if (file == null)
                    {
                        return null; // 用户取消了选择
                    }
                    filePath = file.Path;
                }

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
        /// 重置为默认设�?
        /// </summary>
        public async Task ResetToDefaultAsync()
        {
            try
            {
                // 清除所有本地设�?
                _localSettings.Values.Clear();
                
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
