using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using XiaoZhi.WinUI.Views;
using XiaoZhi.WinUI.ViewModels;
using XiaoZhi.WinUI.Services;
using XiaoZhi.Core.Services;
using XiaoZhi.Core.Interfaces;

namespace XiaoZhi.WinUI;

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
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        // Configure services
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(ConfigureServices)
            .Build();

        // Start the host
        _ = _host.StartAsync();

        MainWindow = new MainWindow();
        MainWindow.Activate();
    }    private void ConfigureServices(IServiceCollection services)
    {
        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddDebug();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // Core services
        services.AddSingleton<IVerificationService, VerificationService>();
        services.AddSingleton<IConfigurationService, ConfigurationService>();
        
        // Audio services
        services.AddSingleton<IAudioRecorder, NAudioRecorder>();
        services.AddSingleton<IAudioPlayer, NAudioPlayer>();
        services.AddSingleton<IAudioCodec, OpusAudioCodec>();
        
        // Communication services
        services.AddSingleton<ICommunicationClient, MqttNetClient>(provider =>
        {
            var logger = provider.GetService<ILogger<MqttNetClient>>();
            return new MqttNetClient("localhost", 1883, "winui-client", "xiaozhi/chat", logger);
        });
        
        // Voice chat service
        services.AddSingleton<IVoiceChatService, VoiceChatService>();
        
        // Interrupt manager and related services
        services.AddSingleton<InterruptManager>();
        
        // Emotion Manager
        services.AddSingleton<EmotionManager>();
        
        // ViewModels

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
}