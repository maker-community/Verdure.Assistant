using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Models;
using Verdure.Assistant.Core.Services;
using Verdure.Assistant.ViewModels;
using Verdure.Assistant.WinUI.Services;
using Windows.Storage;

namespace Verdure.Assistant.WinUI;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private IHost? _host;

    /// <summary>
    /// Gets the main window instance
    /// </summary>
    public static Window? MainWindow { get; private set; }
    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary> 
    public App()
    {
        InitializeComponent();
    }
    
    /// <summary>
     /// Invoked when the application is launched.
     /// </summary>
     /// <param name="args">Details about the launch request and process.</param>
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Configure services
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(ConfigureServices)
            .Build();

        // Start the host
        await _host.StartAsync();

        MainWindow = new MainWindow();
        MainWindow.Activate();

        // Initialize theme service after window is created
        var themeService = GetService<ThemeService>();
        if (themeService != null)
        {
            await themeService.InitializeAsync();
            themeService.StartSystemThemeListener();
        }
    }
    private void ConfigureServices(IServiceCollection services)
    {
        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Information);
        });        // Settings services
        services.AddSingleton<ISettingsService<AppSettings>, WindowsSettingsService<AppSettings>>();

        // Theme service
        services.AddSingleton<ThemeService>();

        // Core services
        services.AddSingleton<IVerificationService, VerificationService>();
        services.AddSingleton<IConfigurationService, ConfigurationService>();          // Audio services
        services.AddSingleton<AudioStreamManager>(provider =>
        {
            var logger = provider.GetService<ILogger<AudioStreamManager>>();
            return AudioStreamManager.GetInstance(logger);
        });
        services.AddSingleton<IAudioRecorder>(provider => provider.GetService<AudioStreamManager>()!);
        services.AddSingleton<IAudioPlayer, PortAudioPlayer>();
        services.AddSingleton<IAudioCodec, OpusSharpAudioCodec>();

        // Communication services
        services.AddSingleton<ICommunicationClient, MqttNetClient>(provider =>
        {
            var logger = provider.GetService<ILogger<MqttNetClient>>();
            return new MqttNetClient("localhost", 1883, "winui-client", "verdure/chat", logger);
        });        // UI Dispatcher for thread-safe UI operations
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

        // Voice chat service
        services.AddSingleton<IVoiceChatService, VoiceChatService>();

        // Interrupt manager and related services
        services.AddSingleton<InterruptManager>();

        // Microsoft Cognitive Services keyword spotting service (matches py-xiaozhi wake word detector)
        services.AddSingleton<IKeywordSpottingService, KeywordSpottingService>();

        // Emotion Manager
        services.AddSingleton<IEmotionManager, EmotionManager>();
        
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
