using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Services.Interrupt.Sources;

namespace Verdure.Assistant.Core.Services.Interrupt;

/// <summary>
/// 打断管理器 - 整合新旧打断架构的管理器
/// Enhanced interrupt manager that integrates old and new interrupt architectures
/// </summary>
public class EnhancedInterruptManager : IDisposable
{
    private readonly ILogger<EnhancedInterruptManager>? _logger;
    private readonly InterruptService _interruptService;
    private readonly ISharedAudioRecorder? _audioRecorder;
    private IVoiceChatService? _voiceChatService;
    
    // Interrupt sources
    private ManualInterruptSource? _manualSource;
    private VoiceActivityInterruptSource? _vadSource;
    private HotkeyInterruptSource? _hotkeySource;
    private ApiInterruptSource? _apiSource;
    
    private bool _isInitialized = false;
    private bool _disposed = false;

    public EnhancedInterruptManager(
        ISharedAudioRecorder? audioRecorder = null, 
        ILogger<EnhancedInterruptManager>? logger = null)
    {
        _logger = logger;
        _audioRecorder = audioRecorder;
        _interruptService = new InterruptService(null); // 创建时先不传logger，稍后可以通过其他方式设置
    }

    /// <summary>
    /// 获取打断服务实例
    /// </summary>
    public IInterruptService InterruptService => _interruptService;

    /// <summary>
    /// 设置语音聊天服务
    /// </summary>
    public void SetVoiceChatService(IVoiceChatService voiceChatService)
    {
        _voiceChatService = voiceChatService;
        
        // Set the interrupt service in the voice chat service
        _voiceChatService.SetInterruptService(_interruptService);
        
        _logger?.LogInformation("VoiceChatService set in EnhancedInterruptManager");
    }

    /// <summary>
    /// 设置音乐播放服务以支持音乐播放打断
    /// </summary>
    public void SetMusicPlayerService(IMusicPlayerService musicPlayerService)
    {
        // Subscribe to music playback state changes to handle music interruption
        musicPlayerService.PlaybackStateChanged += OnMusicPlaybackStateChanged;
        _logger?.LogInformation("MusicPlayerService set for music interruption handling");
    }

    /// <summary>
    /// 处理音乐播放状态变化，在音乐播放时允许打断
    /// </summary>
    private void OnMusicPlaybackStateChanged(object? sender, Interfaces.MusicPlaybackEventArgs e)
    {
        try
        {
            switch (e.Status.ToLower())
            {
                case "playing":
                    // 音乐开始播放时，启用打断功能（特别是VAD和热键）
                    _logger?.LogInformation("Music started playing, interrupt sources enabled for music interruption");
                    break;
                    
                case "paused":
                case "stopped":
                case "ended":
                    // 音乐停止时，可以选择性暂停某些打断源
                    _logger?.LogInformation("Music stopped, interrupt behavior updated");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error handling music playback state change for interruption");
        }
    }

    /// <summary>
    /// 触发音乐打断（停止音乐播放）
    /// </summary>
    public async Task TriggerMusicInterruptionAsync(string reason = "Voice interrupt during music playback")
    {
        try
        {
            _logger?.LogInformation("Triggering music interruption: {Reason}", reason);
            await _interruptService.TriggerManualInterruptAsync($"Music interruption: {reason}", 
                new { Type = "MusicInterruption", Reason = reason });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to trigger music interruption");
        }
    }

    /// <summary>
    /// 初始化打断管理器
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            _logger?.LogWarning("EnhancedInterruptManager is already initialized");
            return;
        }

        if (_voiceChatService == null)
        {
            _logger?.LogError("VoiceChatService must be set before initialization");
            return;
        }

        try
        {
            // Create and register interrupt sources
            await CreateInterruptSources();
            
            // Start all interrupt sources
            await _interruptService.StartAllAsync();
            
            _isInitialized = true;
            _logger?.LogInformation("EnhancedInterruptManager initialized successfully");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize EnhancedInterruptManager");
            throw;
        }
    }

    /// <summary>
    /// 创建并注册打断源
    /// </summary>
    private async Task CreateInterruptSources()
    {
        // Manual interrupt source
        _manualSource = new ManualInterruptSource(_logger != null ? 
            Microsoft.Extensions.Logging.LoggerFactory.Create(builder => {}).CreateLogger<ManualInterruptSource>() : null);
        _interruptService.RegisterInterruptSource(_manualSource);

        // Voice activity interrupt source with proper configuration
        var vadConfig = new VoiceActivityInterruptSource.VadConfiguration
        {
            EnergyThreshold = 0.001f,
            MinVoiceFrames = 3,
            MinSilenceFrames = 10,
            MinVoiceDurationMs = 100f,
            MaxSilenceDurationMs = 500f,
            DebugOutput = _logger?.IsEnabled(LogLevel.Debug) ?? false
        };
        
        //_vadSource = new VoiceActivityInterruptSource(
        //    _audioRecorder, 
        //    _voiceChatService, 
        //    vadConfig,
        //    _logger != null ? 
        //        Microsoft.Extensions.Logging.LoggerFactory.Create(builder => {}).CreateLogger<VoiceActivityInterruptSource>() : null);
        //_interruptService.RegisterInterruptSource(_vadSource);

        // Hotkey interrupt source
        _hotkeySource = new HotkeyInterruptSource(_logger != null ? 
            Microsoft.Extensions.Logging.LoggerFactory.Create(builder => {}).CreateLogger<HotkeyInterruptSource>() : null);
        _interruptService.RegisterInterruptSource(_hotkeySource);

        // API interrupt source
        _apiSource = new ApiInterruptSource(_logger != null ? 
            Microsoft.Extensions.Logging.LoggerFactory.Create(builder => {}).CreateLogger<ApiInterruptSource>() : null);
        _interruptService.RegisterInterruptSource(_apiSource);

        _logger?.LogInformation("All interrupt sources created and registered successfully");
        await Task.CompletedTask;
    }

    /// <summary>
    /// 启用或禁用VAD检测
    /// </summary>
    public async Task SetVADEnabledAsync(bool enabled)
    {
        if (_vadSource != null)
        {
            _vadSource.IsEnabled = enabled;
            if (enabled)
            {
                await _interruptService.ResumeSourceAsync(_vadSource.Name);
                _logger?.LogInformation("VAD interrupt detection enabled");
            }
            else
            {
                await _interruptService.PauseSourceAsync(_vadSource.Name);
                _logger?.LogInformation("VAD interrupt detection disabled");
            }
        }
    }

    /// <summary>
    /// 启用或禁用热键检测
    /// </summary>
    public async Task SetHotkeyEnabledAsync(bool enabled)
    {
        if (_hotkeySource != null)
        {
            _hotkeySource.IsEnabled = enabled;
            if (enabled)
            {
                await _interruptService.ResumeSourceAsync(_hotkeySource.Name);
                _logger?.LogInformation("Hotkey interrupt detection enabled");
            }
            else
            {
                await _interruptService.PauseSourceAsync(_hotkeySource.Name);
                _logger?.LogInformation("Hotkey interrupt detection disabled");
            }
        }
    }

    /// <summary>
    /// 暂停VAD检测（例如在用户语音输入期间）
    /// </summary>
    public async Task PauseVADAsync()
    {
        if (_vadSource != null && _vadSource.IsEnabled)
        {
            await _interruptService.PauseSourceAsync(_vadSource.Name);
            _logger?.LogDebug("VAD detection paused");
        }
    }

    /// <summary>
    /// 恢复VAD检测
    /// </summary>
    public async Task ResumeVADAsync()
    {
        if (_vadSource != null && _vadSource.IsEnabled)
        {
            await _interruptService.ResumeSourceAsync(_vadSource.Name);
            _logger?.LogDebug("VAD detection resumed");
        }
    }

    /// <summary>
    /// 触发手动打断
    /// </summary>
    public async Task TriggerManualInterruptAsync(string description, object? data = null)
    {
        await _interruptService.TriggerManualInterruptAsync(description, data);
    }

    /// <summary>
    /// 触发API打断
    /// </summary>
    public void TriggerApiInterrupt(string endpoint, object? requestData = null)
    {
        _apiSource?.TriggerApiInterrupt(endpoint, requestData);
    }

    /// <summary>
    /// 触发外部系统打断
    /// </summary>
    public void TriggerExternalInterrupt(string source, string description, object? data = null)
    {
        _apiSource?.TriggerExternalInterrupt(source, description, data);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _interruptService?.Dispose();
            _disposed = true;
        }
    }
}