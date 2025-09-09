using Microsoft.Extensions.Logging;
using Plugin.Maui.Audio;
using CommunityToolkit.Maui;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Services;
using Verdure.Assistant.ViewModels;
using Verdure.Assistant.MAUI.Services;
using Verdure.Assistant.MAUI.Views;

#if ANDROID
using Verdure.Assistant.MAUI.Platforms.Android.Services;
#endif

namespace Verdure.Assistant.MAUI;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // 注册音频服务
        builder.Services.AddSingleton(AudioManager.Current);
        builder.Services.AddSingleton<IAudioManager>(AudioManager.Current);

        // 注册服务接口
#if ANDROID
        builder.Services.AddSingleton<IAudioServiceManager, AudioServiceManager>();
        builder.Services.AddSingleton<IMusicAudioPlayer, AndroidMusicAudioPlayer>();
#endif

        // 注册UI调度器 - MAUI平台特定实现
        builder.Services.AddSingleton<IUIDispatcher, MauiUIDispatcher>();

        // 注册音乐播放服务
        builder.Services.AddSingleton<IMusicPlayerService, KuwoMusicService>();

        // 注册ViewModels
        builder.Services.AddTransient<HomePageViewModel>();
        builder.Services.AddTransient<SettingsPageViewModel>();

        // 注册Views
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<HomePage>();
        builder.Services.AddTransient<SettingsPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
