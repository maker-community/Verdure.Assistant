using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Services;
using Verdure.Assistant.Core.Interfaces;

namespace Verdure.Assistant.Api.Services;

/// <summary>
/// 音频监控服务 - 监控音频流状态并在出现问题时自动恢复
/// </summary>
public class AudioMonitoringService : BackgroundService
{
    private readonly ILogger<AudioMonitoringService> _logger;
    private readonly AudioStreamManager _audioStreamManager;
    private readonly IVoiceChatService? _voiceChatService;
    private DateTime _lastSuccessfulCheck = DateTime.Now;
    private int _consecutiveFailures = 0;
    private const int MaxConsecutiveFailures = 3;
    private const int MonitoringIntervalSeconds = 30; // 每30秒检查一次

    public AudioMonitoringService(
        ILogger<AudioMonitoringService> logger,
        AudioStreamManager audioStreamManager,
        IVoiceChatService? voiceChatService = null)
    {
        _logger = logger;
        _audioStreamManager = audioStreamManager;
        _voiceChatService = voiceChatService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("音频监控服务已启动");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(MonitoringIntervalSeconds), stoppingToken);
                
                if (stoppingToken.IsCancellationRequested)
                    break;

                await PerformHealthCheckAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "音频监控服务执行过程中出错");
            }
        }

        _logger.LogInformation("音频监控服务已停止");
    }

    private async Task PerformHealthCheckAsync()
    {
        try
        {
            // 检查音频流管理器状态
            var isRecording = _audioStreamManager.IsRecording;
            
            _logger.LogDebug("音频健康检查 - 录音状态: {IsRecording}", isRecording);

            // 如果有语音聊天服务且正在活动，但音频流不工作，这可能是问题
            if (_voiceChatService?.IsVoiceChatActive == true && !isRecording)
            {
                _consecutiveFailures++;
                _logger.LogWarning("检测到音频状态不一致 - 语音聊天活动但音频流未录音 (连续失败: {Count})", _consecutiveFailures);

                if (_consecutiveFailures >= MaxConsecutiveFailures)
                {
                    _logger.LogError("连续 {Count} 次检测到音频问题，启动恢复程序", MaxConsecutiveFailures);
                    await TriggerRecoveryAsync("音频状态不一致");
                }
            }
            else
            {
                // 重置失败计数
                if (_consecutiveFailures > 0)
                {
                    _logger.LogInformation("音频状态已恢复正常，重置失败计数");
                    _consecutiveFailures = 0;
                }
                _lastSuccessfulCheck = DateTime.Now;
            }

            // 检查是否长时间没有成功检查
            var timeSinceLastSuccess = DateTime.Now - _lastSuccessfulCheck;
            if (timeSinceLastSuccess.TotalMinutes > 5) // 5分钟没有成功检查
            {
                _logger.LogWarning("音频系统超过 5 分钟没有正常响应，可能存在问题");
                await TriggerRecoveryAsync("长时间无响应");
            }
        }
        catch (Exception ex)
        {
            _consecutiveFailures++;
            _logger.LogError(ex, "执行音频健康检查时出错 (连续失败: {Count})", _consecutiveFailures);

            if (_consecutiveFailures >= MaxConsecutiveFailures)
            {
                await TriggerRecoveryAsync($"健康检查异常: {ex.Message}");
            }
        }
    }

    private async Task TriggerRecoveryAsync(string reason)
    {
        try
        {
            _logger.LogWarning("触发音频恢复程序，原因: {Reason}", reason);

            // 1. 强制清理音频系统
            try
            {
                _audioStreamManager.ForceCleanup();
                _logger.LogInformation("音频系统已强制清理");
            }
            catch (Exception cleanupEx)
            {
                _logger.LogError(cleanupEx, "强制清理音频系统时出错");
            }

            // 2. 如果有语音聊天服务，尝试使用其恢复方法
            if (_voiceChatService is VoiceChatService voiceChat)
            {
                try
                {
                    var recovered = await voiceChat.RecoverFromAudioStreamErrorAsync(
                        new Exception($"监控检测到问题: {reason}"));
                    
                    if (recovered)
                    {
                        _logger.LogInformation("语音聊天服务恢复成功");
                        _consecutiveFailures = 0;
                        _lastSuccessfulCheck = DateTime.Now;
                    }
                    else
                    {
                        _logger.LogError("语音聊天服务恢复失败");
                    }
                }
                catch (Exception recoveryEx)
                {
                    _logger.LogError(recoveryEx, "执行语音聊天服务恢复时出错");
                }
            }

            // 3. 等待系统稳定
            await Task.Delay(5000);

            _logger.LogInformation("音频恢复程序执行完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行音频恢复程序时出错");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("正在停止音频监控服务...");
        await base.StopAsync(cancellationToken);
    }
}
