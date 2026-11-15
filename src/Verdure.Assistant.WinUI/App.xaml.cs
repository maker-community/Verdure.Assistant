using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Windows.AppLifecycle;
using Microsoft.UI.Windowing;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Models;
using Verdure.Assistant.Core.Services;
using Verdure.Assistant.Core.Services.DeepSeek;
using Verdure.Assistant.Core.Services.DeepSeek.Speech;
using Verdure.Assistant.Core.Services.MCP;
using Verdure.Assistant.Core.Services.Interrupt;
using Verdure.Assistant.ViewModels;
using Verdure.Assistant.WinUI.Services;
using Verdure.Assistant.WinUI.Views;
using Windows.Storage;
using WinUIEx;

namespace Verdure.Assistant.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private const string SingleInstanceKey = "Verdure.Assistant.Main";

    private IHost? _host;
    private ILogger<App>? _log;
    private bool _shutdownStarted;
    private bool _shutdownFinished;

    /// <summary>
    /// Gets the main window instance
    /// </summary>
    public static Window? MainWindow { get; private set; }

    /// <summary>
    /// 启动阶段状态（供主窗口展示加载/降级/失败信息）
    /// </summary>
    public static StartupStatusService Startup { get; } = new();

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary> 
    public App()
    {
        InitializeComponent();

        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            // 同一时间只允许一个实例：两个进程同时占用麦克风/唤醒词模型会互相冲突
            if (!await EnsureSingleInstanceAsync())
            {
                Exit();
                return;
            }

            // 构建（但还不启动）宿主，先把窗口显示出来，慢操作放到窗口显示之后
            _host = Host.CreateDefaultBuilder()
                .ConfigureServices(ConfigureServices)
                .Build();
            _log = GetService<ILogger<App>>();
            _log?.LogInformation("Verdure.Assistant 启动 (packaged: {Packaged})", IsPackaged());

            Startup.Report(StartupPhase.Starting, "正在启动…");

            MainWindow = new MainWindow();
            MainWindow.AppWindow.Closing += OnMainWindowClosing;
            MainWindow.Activate();

            await StartServicesAsync();
        }
        catch (Exception ex)
        {
            _log?.LogCritical(ex, "应用启动失败");
            Startup.Report(StartupPhase.Failed, $"启动失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 启动后台服务（宿主、主题、MCP 设备）。每一步独立容错：某一步失败时应用降级运行并给出提示，而不是崩溃。
    /// 也供界面上的"重试"按钮调用。
    /// </summary>
    public static Task RetryStartupAsync()
        => Current is App app ? app.StartServicesAsync() : Task.CompletedTask;

    private async Task StartServicesAsync()
    {
        var failures = new List<string>();

        try
        {
            if (_host != null)
            {
                await _host.StartAsync();
            }
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "启动宿主服务失败");
            failures.Add("后台服务");
        }

        try
        {
            var themeService = GetService<ThemeService>();
            if (themeService != null)
            {
                await themeService.InitializeAsync();
                themeService.StartSystemThemeListener();
            }
        }
        catch (Exception ex)
        {
            _log?.LogError(ex, "初始化主题失败");
            failures.Add("主题");
        }

        // Initialize MCP device management (based on xiaozhi-esp32 architecture)
        if (!await InitializeMcpDevicesAsync())
        {
            failures.Add("设备控制(MCP)");
        }

        if (failures.Count == 0)
        {
            Startup.Report(StartupPhase.Ready, string.Empty);
        }
        else
        {
            Startup.Report(StartupPhase.Degraded, $"部分功能未能启动：{string.Join("、", failures)}。应用仍可使用，可点击重试。");
        }
    }

    private static bool IsPackaged()
    {
        try
        {
            _ = Windows.ApplicationModel.Package.Current.Id;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> EnsureSingleInstanceAsync()
    {
        try
        {
            var mainInstance = AppInstance.FindOrRegisterForKey(SingleInstanceKey);
            if (mainInstance.IsCurrent)
            {
                mainInstance.Activated += OnRedirectedActivation;
                return true;
            }

            // 在线程池线程上重定向，避免在 UI(STA) 线程上等待造成死锁
            var activation = AppInstance.GetCurrent().GetActivatedEventArgs();
            await Task.Run(() => mainInstance.RedirectActivationToAsync(activation).AsTask());
            return false;
        }
        catch (Exception ex)
        {
            // 单实例检测本身失败时不要阻止应用启动
            System.Diagnostics.Debug.WriteLine($"Single-instance check failed: {ex.Message}");
            return true;
        }
    }

    private void OnRedirectedActivation(object? sender, AppActivationArguments e)
    {
        if (MainWindow is not WindowEx window)
        {
            return;
        }

        window.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                window.Show();
                window.BringToFront();
            }
            catch (Exception ex)
            {
                _log?.LogDebug(ex, "激活已有窗口失败");
            }
        });
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        _log?.LogError(e.Exception, "未处理的界面异常: {Message}", e.Message);
        // 尽量让应用继续运行，并把问题告诉用户，而不是静默崩溃
        e.Handled = true;
        Startup.Report(StartupPhase.Degraded, $"发生未处理的错误：{e.Exception?.Message ?? e.Message}");
    }

    private void OnDomainUnhandledException(object? sender, System.UnhandledExceptionEventArgs e)
    {
        _log?.LogCritical(e.ExceptionObject as Exception, "未处理的域异常 (终止: {Terminating})", e.IsTerminating);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _log?.LogError(e.Exception, "未被观察的任务异常");
        e.SetObserved();
    }

    private async void OnMainWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_shutdownFinished)
        {
            return;
        }

        args.Cancel = true;
        await ShutdownAndCloseAsync();
    }

    internal async Task ShutdownAndCloseAsync()
    {
        if (_shutdownStarted)
        {
            return;
        }
        _shutdownStarted = true;

        try
        {
            await Task.Run(ShutdownServicesAsync).WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException)
        {
            _log?.LogWarning("关闭服务超时，继续关闭窗口；资源清理仍在后台进行");
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "应用关闭流程出错");
        }
        finally
        {
            _shutdownFinished = true;
            MainWindow?.Close();
        }
    }

    private async Task ShutdownServicesAsync()
    {
        try
        {
            async Task StopVoiceAsync()
            {
                try
                {
                    var voice = GetService<IVoiceChatService>();
                    if (voice != null)
                    {
                        await voice.DisconnectAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _log?.LogWarning(ex, "关闭小智语音服务时出错");
                }
            }

            async Task StopDeepSeekAsync()
            {
                try
                {
                    var voice = GetService<DeepSeekVoiceChatService>();
                    if (voice != null)
                    {
                        await voice.StopAsync().ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    _log?.LogWarning(ex, "关闭 DeepSeek 语音服务时出错");
                }
            }

            await Task.WhenAll(StopVoiceAsync(), StopDeepSeekAsync()).ConfigureAwait(false);
            if (_host != null)
            {
                await _host.StopAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _log?.LogWarning(ex, "停止宿主时出错");
        }
        finally
        {
            try { _host?.Dispose(); } catch (Exception ex) { _log?.LogWarning(ex, "释放宿主时出错"); }
        }
    }
    private void ConfigureServices(IServiceCollection services)
    {
        // Logging：控制台/调试输出 + 滚动文件日志（便于排查现场问题）
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddDebug();
            builder.AddProvider(new FileLoggerProvider(FileLoggerProvider.DefaultDirectory));
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // 启动状态与机密存储
        services.AddSingleton(Startup);
        services.AddSingleton<ISecretStore, PasswordVaultSecretStore>();
        services.AddSingleton<WinUISpeechService>();

        // DeepSeek：客户端/选项为单例，会话为单例（跨页面导航保留上下文），本地 MCP 工具可开关
        services.AddDeepSeek();
        services.AddDeepSeekMcpTools();
        services.AddSingleton(sp => new SwitchableToolExecutor(sp.GetService<IDeepSeekToolExecutor>()));
        services.AddSingleton(sp => new DeepSeekChatSession(
            sp.GetRequiredService<IDeepSeekClient>(),
            sp.GetRequiredService<DeepSeekOptions>(),
            sp.GetRequiredService<SwitchableToolExecutor>(),
            sp.GetService<ILogger<DeepSeekChatSession>>()));
        services.AddSingleton<DeepSeekChatViewModel>();
        services.AddTransient<DeepSeekChatPage>();

        // 语音后端：DeepSeek 语音对话 + 小智之间的协调（唤醒词/连续对话/按住说话路由）
        services.AddSingleton<DeepSeekVoiceChatService>();
        services.AddSingleton<VoiceBackendCoordinator>();

        // Settings services：打包运行用 ApplicationData；未打包运行（winapp run -p WindowsPackageType=None 等）没有包标识，
        // 访问 ApplicationData 会抛异常，因此改用基于文件的设置存储
        if (IsPackaged())
        {
            services.AddSingleton<ISettingsService<AppSettings>, WindowsSettingsService<AppSettings>>();
        }
        else
        {
            services.AddSingleton<ISettingsService<AppSettings>>(sp =>
                new FileBasedSettingsService<AppSettings>(null, sp.GetService<ILogger<FileBasedSettingsService<AppSettings>>>()));
        }

        // Theme service
        services.AddSingleton<ThemeService>();

        // Core services
        services.AddSingleton<IConfigurationService, ConfigurationService>();
        // Audio services - Register AudioStreamManager as singleton using factory pattern
        services.AddSingleton<SoundFlowAudioRecorder>(provider =>
        {
            var logger = provider.GetService<ILogger<SoundFlowAudioRecorder>>();
            return SoundFlowAudioRecorder.GetInstance(logger);
        });
        services.AddSingleton<ISharedAudioRecorder>(provider => provider.GetRequiredService<SoundFlowAudioRecorder>());

        services.AddSingleton<IAudioPlayer, SoundFlowAudioPlayer>();
        services.AddSingleton<IAudioCodec, OpusSharpAudioCodec>();

        // UI Dispatcher for thread-safe UI operations
        services.AddSingleton<IUIDispatcher>(provider =>
        {
            // Get the DispatcherQueue from the current thread (main UI thread)
            var dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            if (dispatcherQueue == null)
            {
                throw new InvalidOperationException("No DispatcherQueue available for current thread. This service must be resolved on the UI thread.");
            }
            return new WinUIDispatcher(dispatcherQueue);
        });

        // 摄像头拍照 / 屏幕截图（供 AI 工具与 DeepSeek 页使用）
        services.AddSingleton<IImageCaptureService, WinUIImageCaptureService>();

        // Voice chat service
        services.AddSingleton<IVoiceChatService, VoiceChatService>();
        // Music player service
        services.AddSingleton<IMusicAudioPlayer, WinUIMusicAudioPlayer>();
        services.AddSingleton<IMusicPlayerService, KuwoMusicService>();
        // Register MCP services (new architecture based on xiaozhi-esp32)
        services.AddSingleton<McpServer>();
        services.AddSingleton<McpDeviceManager>();
        // Interrupt manager and related services (using enhanced architecture)
        // Interrupt architecture (now integrated into VoiceChatService)
        // EnhancedInterruptManager has been removed - functionality integrated into VoiceChatService

        // Microsoft Cognitive Services keyword spotting service (matches py-xiaozhi wake word detector)
        services.AddSingleton<IKeywordSpottingService, KeywordSpottingService>();

        // New Emotion Playback System
        services.AddWinUIEmotionServices();

        // ViewModels
        services.AddTransient<MainWindowViewModel>();
        services.AddTransient<HomePageViewModel>();
        services.AddTransient<SettingsPageViewModel>();

        // Views
        services.AddTransient<HomePage>();
        services.AddTransient<SettingsPage>();
        services.AddTransient<MainWindow>();
    }
    /// <summary>
    /// Initialize MCP devices and setup integration (based on xiaozhi-esp32 architecture)
    /// </summary>
    private async Task<bool> InitializeMcpDevicesAsync()
    {
        try
        {
            var logger = GetService<ILogger<App>>();
            logger?.LogInformation("开始初始化MCP设备...");

            // Get required services
            var mcpServer = GetService<McpServer>();
            var mcpDeviceManager = GetService<McpDeviceManager>();
            var voiceChatService = GetService<IVoiceChatService>();
            var musicPlayerService = GetService<IMusicPlayerService>();
            var keywordSpottingService = GetService<IKeywordSpottingService>();

            if (mcpServer == null || mcpDeviceManager == null)
            {
                logger?.LogError("Required MCP services not found");
                return false;
            }

            if (voiceChatService == null)
            {
                logger?.LogError("VoiceChatService not found");
                return false;
            }

            // VoiceChatService now gets music player service via constructor injection
            // No need to call SetMusicPlayerService anymore
            if (musicPlayerService != null)
            {
                logger?.LogInformation("音乐播放服务已通过构造函数注入用于VAD控制");
            }

            // Initialize MCP server and device manager
            await mcpServer.InitializeAsync();
            await mcpDeviceManager.InitializeAsync();
            logger?.LogInformation("MCP服务器已初始化");

            // 尽早创建语音后端协调器：它负责给唤醒词安装路由，并读取上次选择的对话后端
            var backend = GetService<VoiceBackendCoordinator>();
            if (backend != null)
            {
                await backend.InitializeAsync();
            }

            logger?.LogInformation("MCP集成服务已设置到语音聊天服务");

            logger?.LogInformation("MCP设备初始化完成");
            return true;
        }
        catch (Exception ex)
        {
            var logger = GetService<ILogger<App>>();
            logger?.LogError(ex, "MCP设备初始化失败");
            return false;
        }
    }

    /// <summary>
    /// Gets a service of the specified type from the dependency injection container
    /// </summary>
    public static T? GetService<T>() where T : class
    {
        return ((App)Current)?._host?.Services.GetService<T>();
    }

    /// <summary>
    /// Apply the saved theme preference or follow system theme
    /// </summary>
    private void ApplySavedTheme()
    {
        try
        {
            var localSettings = ApplicationData.Current.LocalSettings;
            var theme = localSettings.Values["Theme"]?.ToString() ?? "Follow System";

            ApplyTheme(theme);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to apply saved theme: {ex.Message}");
            // Default to system theme if there's an error
            RequestedTheme = ApplicationTheme.Light;
        }
    }

    /// <summary>
    /// Apply the specified theme to the application
    /// </summary>
    /// <param name="themeName">Theme name: "Default", "Light", or "Dark"</param>
    public void ApplyTheme(string themeName)
    {
        ApplicationTheme requestedTheme = themeName switch
        {
            "Light" => ApplicationTheme.Light,
            "Dark" => ApplicationTheme.Dark,
            _ => ApplicationTheme.Light // Default for "Default" and others
        };

        // For "Default", we need to detect the system theme
        if (themeName == "Default")
        {
            requestedTheme = GetSystemTheme();
        }

        // Apply the theme to the main window if it exists
        if (MainWindow?.Content is FrameworkElement rootElement)
        {
            rootElement.RequestedTheme = requestedTheme == ApplicationTheme.Light
                ? ElementTheme.Light
                : ElementTheme.Dark;
        }
    }

    /// <summary>
    /// Get the current system theme preference
    /// </summary>
    private ApplicationTheme GetSystemTheme()
    {
        try
        {
            var uiSettings = new Windows.UI.ViewManagement.UISettings();
            var foreground = uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground);

            // If foreground is light (close to white), system is using dark theme
            // If foreground is dark (close to black), system is using light theme
            var brightness = (foreground.R + foreground.G + foreground.B) / 3.0;
            return brightness > 128 ? ApplicationTheme.Dark : ApplicationTheme.Light;
        }
        catch
        {
            // Fallback to light theme if system theme detection fails
            return ApplicationTheme.Light;
        }
    }

    /// <summary>
    /// Public method to change theme from settings page
    /// </summary>
    /// <param name="themeName">Theme name to apply</param>
    public static void ChangeTheme(string themeName)
    {
        if (Current is App app)
        {
            app.ApplyTheme(themeName);
        }
    }
}
