using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using XiaoZhi.Core.Interfaces;
using XiaoZhi.Core.Services;
using XiaoZhi.WinUI.Views;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace XiaoZhi.WinUI
{
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
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            // Build and start the host
            _host = CreateHostBuilder().Build();
            _host.StartAsync();

            MainWindow = new Window();
            MainWindow.Content = new MainPage();
            MainWindow.Title = "小智语音聊天";
            MainWindow.Activate();
        }

        private static IHostBuilder CreateHostBuilder() =>
            Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {
                    // Configure logging
                    services.AddLogging(builder =>
                    {
                        builder.AddConsole();
                        builder.SetMinimumLevel(LogLevel.Information);
                    });                    // Register core services
                    services.AddSingleton<IVerificationService, VerificationService>();
                    services.AddSingleton<IConfigurationService, ConfigurationService>();
                    services.AddSingleton<IAudioRecorder, PortAudioRecorder>();
                    services.AddSingleton<IAudioPlayer, PortAudioPlayer>();
                    services.AddSingleton<IAudioCodec, OpusAudioCodec>();
                    services.AddSingleton<ICommunicationClient, MqttNetClient>(provider =>
                    {
                        var logger = provider.GetService<ILogger<MqttNetClient>>();
                        return new MqttNetClient("localhost", 1883, "winui-client", "xiaozhi/chat", logger);
                    });
                    services.AddSingleton<IVoiceChatService, VoiceChatService>();
                });

        /// <summary>
        /// Gets a service of the specified type from the dependency injection container
        /// </summary>
        public static T? GetService<T>() where T : class
        {
            return ((App)Current)?._host?.Services.GetService<T>();
        }
    }
}