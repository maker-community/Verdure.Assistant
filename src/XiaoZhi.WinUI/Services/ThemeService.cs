using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using System;
using System.Threading.Tasks;
using Windows.Storage;
using XiaoZhi.Core.Interfaces;
using XiaoZhi.Core.Models;

namespace XiaoZhi.WinUI.Services
{
    /// <summary>
    /// 主题管理服务
    /// 负责应用主题的加载、应用和监听变化
    /// 参考Microsoft TemplateStudio的主题管理模式
    /// </summary>
    public class ThemeService
    {
        private readonly ILogger<ThemeService>? _logger;
        private readonly ISettingsService<AppSettings>? _settingsService;
        private string _currentTheme = "Default";

        /// <summary>
        /// 主题变化事件
        /// </summary>
        public event EventHandler<string>? ThemeChanged;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="settingsService">设置服务</param>
        /// <param name="logger">日志记录器</param>
        public ThemeService(ISettingsService<AppSettings>? settingsService = null, ILogger<ThemeService>? logger = null)
        {
            _settingsService = settingsService;
            _logger = logger;
        }

        /// <summary>
        /// 初始化主题服务
        /// 在应用启动时调用，加载并应用保存的主题设置
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                _logger?.LogInformation("Initializing theme service...");

                // 从设置服务加载主题，如果失败则从本地设置加载
                string theme = await LoadThemeFromSettings();
                
                if (string.IsNullOrEmpty(theme))
                {
                    theme = LoadThemeFromLocalSettings();
                }

                if (string.IsNullOrEmpty(theme))
                {
                    theme = "Default";
                }

                await SetThemeAsync(theme);
                _logger?.LogInformation("Theme service initialized with theme: {Theme}", theme);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to initialize theme service");
                // 失败时使用默认主题
                await SetThemeAsync("Default");
            }
        }

        /// <summary>
        /// 设置主题
        /// </summary>
        /// <param name="theme">主题名称: "Default", "Light", "Dark"</param>
        public async Task SetThemeAsync(string theme)
        {
            try
            {
                if (_currentTheme == theme)
                {
                    _logger?.LogDebug("Theme is already set to {Theme}, skipping", theme);
                    return;
                }

                _logger?.LogInformation("Setting theme to: {Theme}", theme);

                // 应用主题到UI
                ApplyThemeToUI(theme);

                // 保存主题设置
                await SaveThemeToSettings(theme);
                SaveThemeToLocalSettings(theme);

                _currentTheme = theme;

                // 触发主题变化事件
                ThemeChanged?.Invoke(this, theme);

                _logger?.LogInformation("Theme successfully changed to: {Theme}", theme);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to set theme to {Theme}", theme);
                throw;
            }
        }

        /// <summary>
        /// 获取当前主题
        /// </summary>
        /// <returns>当前主题名称</returns>
        public string GetCurrentTheme()
        {
            return _currentTheme;
        }

        /// <summary>
        /// 检测系统主题
        /// </summary>
        /// <returns>系统主题</returns>
        public ApplicationTheme GetSystemTheme()
        {
            try
            {
                var uiSettings = new Windows.UI.ViewManagement.UISettings();
                var foreground = uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground);
                
                // 如果前景色接近白色，系统使用暗主题
                // 如果前景色接近黑色，系统使用亮主题
                var brightness = (foreground.R + foreground.G + foreground.B) / 3.0;
                return brightness > 128 ? ApplicationTheme.Dark : ApplicationTheme.Light;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to detect system theme, defaulting to Light");
                return ApplicationTheme.Light;
            }
        }

        /// <summary>
        /// 监听系统主题变化
        /// </summary>
        public void StartSystemThemeListener()
        {
            try
            {
                var uiSettings = new Windows.UI.ViewManagement.UISettings();
                uiSettings.ColorValuesChanged += async (sender, args) =>
                {
                    if (_currentTheme == "Default")
                    {
                        _logger?.LogInformation("System theme changed, updating application theme");
                        await SetThemeAsync("Default");
                    }
                };
                _logger?.LogInformation("System theme listener started");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to start system theme listener");
            }
        }

        #region Private Methods

        /// <summary>
        /// 从设置服务加载主题
        /// </summary>
        private async Task<string> LoadThemeFromSettings()
        {
            try
            {
                if (_settingsService != null)
                {
                    var settings = await _settingsService.LoadSettingsAsync();
                    return settings?.Theme ?? "Default";
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load theme from settings service");
            }
            return string.Empty;
        }

        /// <summary>
        /// 从本地设置加载主题（备用方案）
        /// </summary>
        private string LoadThemeFromLocalSettings()
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                return localSettings.Values["Theme"]?.ToString() ?? "Default";
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to load theme from local settings");
                return "Default";
            }
        }

        /// <summary>
        /// 保存主题到设置服务
        /// </summary>
        private async Task SaveThemeToSettings(string theme)
        {
            try
            {
                if (_settingsService != null)
                {
                    var settings = await _settingsService.LoadSettingsAsync();
                    if (settings != null)
                    {
                        settings.Theme = theme;
                        await _settingsService.SaveSettingsAsync(settings);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to save theme to settings service");
            }
        }

        /// <summary>
        /// 保存主题到本地设置（备用方案）
        /// </summary>
        private void SaveThemeToLocalSettings(string theme)
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;
                localSettings.Values["Theme"] = theme;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Failed to save theme to local settings");
            }
        }

        /// <summary>
        /// 应用主题到UI
        /// </summary>
        private void ApplyThemeToUI(string themeName)
        {
            ApplicationTheme requestedTheme = themeName switch
            {
                "Light" => ApplicationTheme.Light,
                "Dark" => ApplicationTheme.Dark,
                _ => GetSystemTheme() // Default情况下跟随系统
            };

            // 获取主窗口并应用主题
            if (App.MainWindow?.Content is FrameworkElement rootElement)
            {
                var elementTheme = requestedTheme == ApplicationTheme.Light 
                    ? ElementTheme.Light 
                    : ElementTheme.Dark;
                
                // 在UI线程上应用主题
                App.MainWindow.DispatcherQueue.TryEnqueue(() =>
                {
                    rootElement.RequestedTheme = elementTheme;
                    _logger?.LogDebug("Applied theme {Theme} to main window", elementTheme);
                });
            }
            else
            {
                _logger?.LogWarning("Main window or root element not found, cannot apply theme");
            }
        }

        #endregion
    }
}
