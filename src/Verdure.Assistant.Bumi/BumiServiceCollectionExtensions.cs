using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Verdure.Assistant.Core.Interfaces;

namespace Verdure.Assistant.Bumi;

/// <summary>
/// 把助手的音频设备换成 Bumi 机器人：注册 <see cref="ISharedAudioRecorder"/> 与 <see cref="IAudioPlayer"/>。
/// </summary>
public static class BumiServiceCollectionExtensions
{
    /// <summary>
    /// 用机器人麦克风/扬声器替换本机声卡。调用后不要再用 SoundFlow 的实现覆盖注册。
    /// </summary>
    public static IServiceCollection AddBumiAudio(this IServiceCollection services, Action<BumiAudioOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<BumiMediaSession>();
        services.TryAddSingleton<BumiAudioRecorder>();
        services.TryAddSingleton<BumiAudioPlayer>();

        services.AddSingleton<ISharedAudioRecorder>(provider => provider.GetRequiredService<BumiAudioRecorder>());
        services.AddSingleton<IAudioRecorder>(provider => provider.GetRequiredService<BumiAudioRecorder>());
        services.AddSingleton<IAudioPlayer>(provider => provider.GetRequiredService<BumiAudioPlayer>());

        return services;
    }
}