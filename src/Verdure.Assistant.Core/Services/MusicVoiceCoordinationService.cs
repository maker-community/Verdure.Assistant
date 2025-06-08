using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Constants;

namespace Verdure.Assistant.Core.Services;

/// <summary>
/// 音乐播放与语音识别协调服务
/// 负责在音乐播放时暂停语音识别，在音乐停止/暂停时恢复语音识别
/// 确保音乐播放和语音识别的状态同步
/// </summary>
public class MusicVoiceCoordinationService : IDisposable
{
    private readonly ILogger<MusicVoiceCoordinationService>? _logger;
    private IMusicPlayerService? _musicPlayerService;
    private IVoiceChatService? _voiceChatService;
    private IKeywordSpottingService? _keywordSpottingService;
    private InterruptManager? _interruptManager;
    
    private bool _isMusicPlaying = false;
    private bool _wasVoiceRecognitionEnabled = false;
    private bool _isDisposed = false;

    /// <summary>
    /// 构造函数
    /// </summary>
    public MusicVoiceCoordinationService(
        IMusicPlayerService? musicPlayerService = null,
        IVoiceChatService? voiceChatService = null,
        IKeywordSpottingService? keywordSpottingService = null,
        InterruptManager? interruptManager = null,
        ILogger<MusicVoiceCoordinationService>? logger = null)
    {
        _musicPlayerService = musicPlayerService;
        _voiceChatService = voiceChatService;
        _keywordSpottingService = keywordSpottingService;
        _interruptManager = interruptManager;
        _logger = logger;

        Initialize();
    }

    /// <summary>
    /// 初始化事件订阅
    /// </summary>
    private void Initialize()
    {
        if (_musicPlayerService != null)
        {
            _musicPlayerService.PlaybackStateChanged += OnMusicPlaybackStateChanged;
            _logger?.LogInformation("已订阅音乐播放状态变化事件");
        }
        else
        {
            _logger?.LogWarning("音乐播放服务未设置，无法进行语音识别协调");
        }
    }

    /// <summary>
    /// 处理音乐播放状态变化事件
    /// </summary>
    private void OnMusicPlaybackStateChanged(object? sender, MusicPlaybackEventArgs e)
    {
        try
        {
            _logger?.LogDebug("音乐播放状态变化: {Status}", e.Status);
            
            switch (e.Status.ToLower())
            {
                case "playing":
                    HandleMusicStarted();
                    break;
                    
                case "paused":
                case "stopped":
                case "ended":
                case "failed":
                    HandleMusicStopped();
                    break;
                    
                default:
                    _logger?.LogDebug("未处理的音乐状态: {Status}", e.Status);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理音乐播放状态变化时出错");
        }
    }

    /// <summary>
    /// 处理音乐开始播放
    /// </summary>
    private void HandleMusicStarted()
    {
        if (_isMusicPlaying) return; // 避免重复处理
        
        _isMusicPlaying = true;
        _logger?.LogInformation("音乐开始播放，暂停语音识别系统");
        
        try
        {
            // 记录当前语音识别状态，以便后续恢复
            _wasVoiceRecognitionEnabled = _keywordSpottingService?.IsRunning ?? false;
            
            // 暂停关键词唤醒检测
            if (_keywordSpottingService?.IsRunning == true)
            {
                _keywordSpottingService.Pause();
                _logger?.LogDebug("关键词唤醒检测已暂停");
            }
            
            // 暂停VAD检测
            if (_interruptManager != null)
            {
                _interruptManager.PauseVAD();
                _logger?.LogDebug("VAD检测已暂停");
            }
            
            // 如果当前正在监听用户输入，停止监听
            if (_voiceChatService?.CurrentState == DeviceState.Listening)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _voiceChatService.StopVoiceChatAsync();
                        _logger?.LogDebug("语音聊天已停止，因为音乐开始播放");
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "停止语音聊天时出错");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "暂停语音识别系统时出错");
        }
    }

    /// <summary>
    /// 处理音乐停止播放
    /// </summary>
    private void HandleMusicStopped()
    {
        if (!_isMusicPlaying) return; // 避免重复处理
        
        _isMusicPlaying = false;
        _logger?.LogInformation("音乐停止播放，恢复语音识别系统");
        
        try
        {
            // 延迟一小段时间确保音频系统稳定
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(200); // 等待音频系统稳定
                    
                    // 恢复VAD检测
                    if (_interruptManager != null)
                    {
                        _interruptManager.ResumeVAD();
                        _logger?.LogDebug("VAD检测已恢复");
                    }
                    
                    // 恢复关键词唤醒检测
                    if (_wasVoiceRecognitionEnabled && _keywordSpottingService != null)
                    {
                        _keywordSpottingService.Resume();
                        _logger?.LogDebug("关键词唤醒检测已恢复");
                    }
                    
                    _logger?.LogInformation("语音识别系统已完全恢复");
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "恢复语音识别系统时出错");
                }
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "启动语音识别系统恢复任务时出错");
        }
    }

    /// <summary>
    /// 手动设置音乐播放器服务（用于后期注入）
    /// </summary>
    public void SetMusicPlayerService(IMusicPlayerService musicPlayerService)
    {
        if (_musicPlayerService != null)
        {
            _musicPlayerService.PlaybackStateChanged -= OnMusicPlaybackStateChanged;
        }
        
        _musicPlayerService = musicPlayerService;
        
        if (_musicPlayerService != null)
        {
            _musicPlayerService.PlaybackStateChanged += OnMusicPlaybackStateChanged;
            _logger?.LogInformation("音乐播放器服务已更新并重新订阅事件");
        }
    }

    /// <summary>
    /// 获取当前音乐播放状态
    /// </summary>
    public bool IsMusicPlaying => _isMusicPlaying;

    /// <summary>
    /// 获取当前语音识别是否因音乐播放而被暂停
    /// </summary>
    public bool IsVoiceRecognitionPausedByMusic => _isMusicPlaying;

    /// <summary>
    /// 手动恢复语音识别（仅在调试或特殊情况下使用）
    /// </summary>
    public void ForceResumeVoiceRecognition()
    {
        _logger?.LogWarning("手动强制恢复语音识别");
        HandleMusicStopped();
    }

    /// <summary>
    /// 手动暂停语音识别（仅在调试或特殊情况下使用）
    /// </summary>
    public void ForcePauseVoiceRecognition()
    {
        _logger?.LogWarning("手动强制暂停语音识别");
        HandleMusicStarted();
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        
        try
        {
            if (_musicPlayerService != null)
            {
                _musicPlayerService.PlaybackStateChanged -= OnMusicPlaybackStateChanged;
            }
            
            // 如果当前因为音乐播放而暂停了语音识别，尝试恢复
            if (_isMusicPlaying)
            {
                HandleMusicStopped();
            }
            
            _logger?.LogInformation("音乐语音协调服务已释放");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "释放音乐语音协调服务时出错");
        }
        finally
        {
            _isDisposed = true;
        }
    }
}
