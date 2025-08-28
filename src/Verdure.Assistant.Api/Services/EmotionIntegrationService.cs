using Verdure.Assistant.Api.Models;
using Verdure.Assistant.Api.Services.Robot;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Models;

namespace Verdure.Assistant.Api.Services;

/// <summary>
/// 语音助手与机器人情感集成服务
/// 监听语音聊天中的情感事件，触发对应的机器人表情和动作
/// </summary>
public class EmotionIntegrationService : IDisposable
{
    private readonly ILogger<EmotionIntegrationService> _logger;
    private readonly EmotionActionService _emotionActionService;
    private IVoiceChatService? _voiceChatService;
    private bool _disposed = false;

    // 情感映射字典 - 将LLM情感映射到机器人表情
    private readonly Dictionary<string, string> _emotionMapping;

    public EmotionIntegrationService(
        ILogger<EmotionIntegrationService> logger,
        EmotionActionService emotionActionService)
    {
        _logger = logger;
        _emotionActionService = emotionActionService;
        _emotionMapping = InitializeEmotionMapping();
        
        _logger.LogInformation("情感集成服务初始化完成");
    }

    /// <summary>
    /// 初始化情感映射关系
    /// 根据需求文档中的映射规则
    /// </summary>
    private Dictionary<string, string> InitializeEmotionMapping()
    {
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // 中性/平静类表情 -> neutral
            ["neutral"] = EmotionTypes.Neutral,
            ["relaxed"] = EmotionTypes.Neutral,
            ["sleepy"] = EmotionTypes.Neutral,

            // 积极/开心类表情 -> happy
            ["happy"] = EmotionTypes.Happy,
            ["laughing"] = EmotionTypes.Happy,
            ["funny"] = EmotionTypes.Happy,
            ["loving"] = EmotionTypes.Happy,
            ["confident"] = EmotionTypes.Happy,
            ["winking"] = EmotionTypes.Happy,
            ["cool"] = EmotionTypes.Happy,
            ["delicious"] = EmotionTypes.Happy,
            ["kissy"] = EmotionTypes.Happy,
            ["silly"] = EmotionTypes.Happy,

            // 悲伤类表情 -> sad
            ["sad"] = EmotionTypes.Sad,
            ["crying"] = EmotionTypes.Sad,

            // 愤怒类表情 -> angry
            ["angry"] = EmotionTypes.Angry,

            // 惊讶类表情 -> surprised
            ["surprised"] = EmotionTypes.Surprised,
            ["shocked"] = EmotionTypes.Surprised,

            // 思考/困惑类表情 -> confused
            ["thinking"] = EmotionTypes.Confused,
            ["confused"] = EmotionTypes.Confused,
            ["embarrassed"] = EmotionTypes.Confused,
        };
    }

    /// <summary>
    /// 设置语音聊天服务并订阅事件
    /// </summary>
    public void SetVoiceChatService(IVoiceChatService voiceChatService)
    {
        // 取消订阅之前的服务
        if (_voiceChatService != null)
        {
            UnsubscribeFromEvents(_voiceChatService);
        }

        _voiceChatService = voiceChatService;
        
        if (_voiceChatService != null)
        {
            SubscribeToEvents(_voiceChatService);
            _logger.LogInformation("已连接到语音聊天服务，开始监听情感事件");
        }
    }

    /// <summary>
    /// 订阅语音聊天服务的事件
    /// </summary>
    private void SubscribeToEvents(IVoiceChatService voiceChatService)
    {
        voiceChatService.LlmMessageReceived += OnLlmMessageReceived;
        _logger.LogInformation("已订阅VoiceChatService的LLM消息事件");
    }

    /// <summary>
    /// 取消订阅语音聊天服务的事件
    /// </summary>
    private void UnsubscribeFromEvents(IVoiceChatService voiceChatService)
    {
        voiceChatService.LlmMessageReceived -= OnLlmMessageReceived;
        _logger.LogInformation("已取消订阅VoiceChatService的LLM消息事件");
    }

    /// <summary>
    /// 处理LLM消息事件
    /// </summary>
    private async void OnLlmMessageReceived(object? sender, LlmMessage llmMessage)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(llmMessage.Emotion))
            {
                await HandleEmotionAsync(llmMessage.Emotion);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理LLM情感消息时发生错误");
        }
    }

    /// <summary>
    /// 处理情感，将LLM情感映射到机器人表情和动作
    /// </summary>
    private async Task HandleEmotionAsync(string emotion)
    {
        if (string.IsNullOrWhiteSpace(emotion))
        {
            _logger.LogDebug("接收到空的情感，跳过处理");
            return;
        }

        var originalEmotion = emotion.Trim();
        _logger.LogInformation($"接收到LLM情感: {originalEmotion}");

        // 映射情感到机器人表情
        if (_emotionMapping.TryGetValue(originalEmotion, out var mappedEmotion))
        {
            _logger.LogInformation($"情感映射: {originalEmotion} -> {mappedEmotion}");

            try
            {
                // 创建播放请求，同时播放表情和动作
                var playRequest = new PlayRequest
                {
                    EmotionType = mappedEmotion,
                    IncludeAction = true,
                    IncludeEmotion = true,
                    Loops = 1,
                    Fps = 30
                };

                // 异步播放情感，不阻塞语音聊天
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var success = await _emotionActionService.PlayEmotionWithActionAsync(playRequest);
                        if (success)
                        {
                            _logger.LogInformation($"成功播放机器人情感: {mappedEmotion}");
                        }
                        else
                        {
                            _logger.LogWarning($"播放机器人情感失败: {mappedEmotion}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"播放机器人情感时发生错误: {mappedEmotion}");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"启动机器人情感播放任务时发生错误: {mappedEmotion}");
            }
        }
        else
        {
            _logger.LogDebug($"未找到情感映射: {originalEmotion}，使用默认中性表情");
            
            // 对于未映射的情感，播放中性表情
            _ = Task.Run(async () =>
            {
                try
                {
                    var defaultRequest = new PlayRequest
                    {
                        EmotionType = EmotionTypes.Neutral,
                        IncludeAction = true,
                        IncludeEmotion = true,
                        Loops = 1,
                        Fps = 30
                    };

                    await _emotionActionService.PlayEmotionWithActionAsync(defaultRequest);
                    _logger.LogInformation($"播放默认中性表情作为未知情感 {originalEmotion} 的回退");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "播放默认中性表情时发生错误");
                }
            });
        }
    }

    /// <summary>
    /// 获取当前情感映射配置
    /// </summary>
    public IReadOnlyDictionary<string, string> GetEmotionMapping()
    {
        return _emotionMapping;
    }

    /// <summary>
    /// 手动触发情感播放（用于测试）
    /// </summary>
    public async Task<bool> TriggerEmotionAsync(string emotion)
    {
        if (string.IsNullOrWhiteSpace(emotion))
        {
            return false;
        }

        _logger.LogInformation($"手动触发情感播放: {emotion}");
        await HandleEmotionAsync(emotion);
        return true;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_voiceChatService != null)
            {
                UnsubscribeFromEvents(_voiceChatService);
            }

            _logger.LogInformation("情感集成服务已释放资源");
            _disposed = true;
        }
    }
}