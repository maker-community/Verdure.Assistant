using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Api.IoT.Interfaces;
using Verdure.Assistant.Api.IoT.Models;
using Verdure.Assistant.Core.Interfaces;

namespace Verdure.Assistant.Api.Services;

/// <summary>
/// 情感集成服务 - 用于将LLM情感事件与IoT机器人表情动作集成
/// </summary>
public interface IEmotionIntegrationService : IEmotionHandler
{
    /// <summary>
    /// 初始化服务
    /// </summary>
    Task InitializeAsync();
}

/// <summary>
/// 情感集成服务实现
/// </summary>
public class EmotionIntegrationService : IEmotionIntegrationService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EmotionIntegrationService> _logger;
    private bool _isInitialized = false;
    
    public EmotionIntegrationService(IServiceProvider serviceProvider, ILogger<EmotionIntegrationService> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 检查服务是否可用
    /// </summary>
    public bool IsAvailable
    {
        get
        {
            try
            {
                return _serviceProvider.GetService<IEmotionActionService>() != null;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>
    /// 处理LLM情感事件 (IEmotionHandler接口实现)
    /// </summary>
    public async Task HandleEmotionAsync(string emotion, string? context = null)
    {
        await HandleEmotionEventAsync(emotion, context);
    }

    /// <summary>
    /// 处理LLM情感事件
    /// </summary>
    public async Task HandleEmotionEventAsync(string emotion, string? context = null)
    {
        if (string.IsNullOrWhiteSpace(emotion))
        {
            _logger.LogWarning("收到空的情感事件");
            return;
        }

        try
        {
            // 获取情感动作服务
            var emotionService = _serviceProvider.GetService<IEmotionActionService>();
            if (emotionService == null)
            {
                _logger.LogWarning("情感动作服务未注册，跳过情感播放");
                return;
            }

            // 映射情感类型
            var mappedEmotion = EmotionMappingService.MapEmotion(emotion);
            
            _logger.LogInformation($"处理LLM情感事件: {emotion} -> {mappedEmotion}");

            // 创建播放请求
            var request = new PlayRequest
            {
                EmotionType = mappedEmotion,
                IncludeEmotion = true,  // 播放表情动画
                IncludeAction = true,   // 执行动作
                Loops = 1,              // 播放一次
                Fps = 30                // 30帧每秒
            };

            // 在后台执行情感播放，避免阻塞语音服务
            _ = Task.Run(async () =>
            {
                try
                {
                    var success = await emotionService.PlayEmotionWithActionAsync(request);
                    if (success)
                    {
                        _logger.LogInformation($"情感 {mappedEmotion} 播放完成");
                    }
                    else
                    {
                        _logger.LogWarning($"情感 {mappedEmotion} 播放失败");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"情感 {mappedEmotion} 播放时发生错误");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"处理情感事件时发生错误: {emotion}");
        }
    }

    /// <summary>
    /// 初始化服务
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized)
            return;

        try
        {
            _logger.LogInformation("初始化情感集成服务");

            // 检查依赖服务是否可用
            var emotionService = _serviceProvider.GetService<IEmotionActionService>();
            if (emotionService != null)
            {
                // 初始化机器人位置
                await emotionService.InitializeRobotAsync();
                _logger.LogInformation("机器人位置初始化完成");
            }
            else
            {
                _logger.LogWarning("情感动作服务未注册，跳过初始化");
            }

            _isInitialized = true;
            _logger.LogInformation("情感集成服务初始化完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "情感集成服务初始化失败");
        }
    }
}