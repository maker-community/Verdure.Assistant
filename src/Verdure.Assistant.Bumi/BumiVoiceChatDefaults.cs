using Microsoft.Extensions.Configuration;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Models;

namespace Verdure.Assistant.Bumi;

/// <summary>
/// 把 appsettings.json 里的语音配置整理成 <see cref="VoiceChatService"/> 需要的参数。
/// </summary>
internal static class BumiVoiceChatDefaults
{
    public static VerdureConfig CreateConfig(IConfiguration configuration)
    {
        var config = new VerdureConfig();
        configuration.Bind(config);

        if (string.IsNullOrEmpty(config.KeywordModels.ModelsPath))
        {
            config.KeywordModels.ModelsPath = "ModelFiles";
        }

        return config;
    }

    /// <summary>
    /// 机器人没有「松开按钮结束说话」这个动作，手动模式永远不会发出 listen stop，
    /// 服务器会一直等下去而不做识别，所以默认让服务端自己判断一句话说完了。
    /// </summary>
    public static void ApplyAutoConversationMode(IVoiceChatService voiceChat, IConfiguration configuration)
    {
        voiceChat.KeepListening = configuration.GetValue("AutoConversationMode", true);
    }
}
