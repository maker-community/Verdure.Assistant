using Microsoft.Extensions.DependencyInjection;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Services;

namespace Verdure.Assistant.WinUI.Services
{
    /// <summary>
    /// 表情播放系统的DI扩展方法
    /// </summary>
    public static class EmotionServiceExtensions
    {
        /// <summary>
        /// 添加WinUI表情播放服务
        /// </summary>
        public static IServiceCollection AddWinUIEmotionServices(this IServiceCollection services)
        {
            // 核心服务
            services.AddSingleton<IEmotionAssetResolver, DefaultEmotionAssetResolver>();
            services.AddSingleton<IEmotionPlaybackCoordinator, EmotionPlaybackCoordinator>();
            
            // WinUI特定的渲染器
            services.AddSingleton<IEmotionRenderer, WinUIGifEmotionRenderer>();
            services.AddSingleton<IEmotionRenderer, WinUIEmojiEmotionRenderer>();

            return services;
        }
    }
}
