using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Constants;
using Verdure.Assistant.Core.Interfaces;

namespace Verdure.Assistant.Core.Services;

/// <summary>
/// Enhanced interrupt manager that coordinates multiple interrupt sources
/// Based on the Python py-xiaozhi application abort mechanisms
/// </summary>
public class InterruptManager : IDisposable
{
    private readonly ILogger<InterruptManager>? _logger;
    private IVoiceChatService? _voiceChatService;
    private VADDetectorService? _vadDetector;
    
    // Interrupt state tracking
    private bool _isInitialized = false;
    private AbortReason _lastAbortReason = AbortReason.None;
    private DateTime _lastInterruptTime = DateTime.MinValue;
    private readonly TimeSpan _interruptCooldown = TimeSpan.FromMilliseconds(500);

    public event EventHandler<InterruptEventArgs>? InterruptTriggered;

    public bool IsVADEnabled { get; private set; } = true;
    public bool IsHotkeyEnabled { get; private set; } = true;
    public AbortReason LastAbortReason => _lastAbortReason;    
    public InterruptManager(
        ILogger<InterruptManager>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 设置语音聊天服务（用于打破循环依赖）
    /// </summary>
    public void SetVoiceChatService(IVoiceChatService voiceChatService)
    {
        _voiceChatService = voiceChatService;
        
        // Initialize interrupt services
        // Pass null for audioRecorder since voice interruption is disabled
        _vadDetector = new VADDetectorService(_voiceChatService, null);
        
        // Subscribe to interrupt events
        _vadDetector.VoiceInterruptDetected += OnVADInterrupt;
        
        _logger?.LogInformation("语音聊天服务已设置到中断管理器");
    }

    public Task InitializeAsync()
    {        
        if (_isInitialized)
        {
            _logger?.LogWarning("Interrupt manager is already initialized");
            return Task.CompletedTask;
        }

        if (_voiceChatService == null || _vadDetector == null)
        {
            _logger?.LogError("VoiceChatService must be set before initialization");
            return Task.CompletedTask;
        }

        try
        {
            // Note: Hotkey functionality is now handled by the new HotkeyInterruptSource
            // This will be integrated through the new interrupt source architecture
            _logger?.LogInformation("Hotkey interrupt will be handled by HotkeyInterruptSource");

            // Start VAD if enabled
            if (IsVADEnabled)
            {
                _vadDetector.Start();
                _logger?.LogInformation("VAD interrupt detection enabled");
            }            

            _isInitialized = true;
            _logger?.LogInformation("Interrupt manager initialized successfully");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize interrupt manager");
            throw;
        }
    }    public async Task ShutdownAsync()
    {
        if (!_isInitialized)
            return;

        try
        {
            // Stop VAD
            _vadDetector?.Stop();
            
            // Note: Hotkey unregistration is now handled by HotkeyInterruptSource
            
            _isInitialized = false;
            _logger?.LogInformation("Interrupt manager shut down");
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error during interrupt manager shutdown");
        }
    }

    /// <summary>
    /// Manually trigger an interrupt (e.g., from UI button)
    /// </summary>
    public async Task TriggerManualInterruptAsync()
    {
        await ProcessInterrupt(AbortReason.UserInterruption, "Manual interrupt triggered");
    }

    /// <summary>
    /// Enable or disable VAD-based interrupts
    /// </summary>
    public void SetVADEnabled(bool enabled)
    {
        if (IsVADEnabled == enabled)
            return;

        IsVADEnabled = enabled;
        
        if (_isInitialized)
        {
            if (enabled)
            {
                _vadDetector?.Start();
                _logger?.LogInformation("VAD interrupt detection enabled");
            }
            else
            {
                _vadDetector?.Stop();
                _logger?.LogInformation("VAD interrupt detection disabled");
            }
        }
    }

    /// <summary>
    /// Pause VAD detection temporarily (e.g., during user speech input)
    /// </summary>
    public void PauseVAD()
    {
        if (IsVADEnabled && _vadDetector?.IsRunning == true)
        {
            _vadDetector.Pause();
            _logger?.LogDebug("VAD detection paused");
        }
    }

    /// <summary>
    /// Resume VAD detection
    /// </summary>
    public void ResumeVAD()
    {
        if (IsVADEnabled && _vadDetector?.IsPaused == true)
        {
            _vadDetector.Resume();
            _logger?.LogDebug("VAD detection resumed");
        }
    }

    private void OnVADInterrupt(object? sender, bool detected)
    {
        if (detected)
        {
            _ = ProcessInterrupt(AbortReason.VoiceInterruption, "Voice activity detected during response");
        }
    }

    private async Task ProcessInterrupt(AbortReason reason, string description)
    {
        // Implement cooldown to prevent rapid-fire interrupts
        var now = DateTime.UtcNow;
        if (now - _lastInterruptTime < _interruptCooldown)
        {
            _logger?.LogDebug("Interrupt ignored due to cooldown period");
            return;
        }

        _lastInterruptTime = now;
        _lastAbortReason = reason;

        _logger?.LogInformation("Processing interrupt: {Reason} - {Description}", reason, description);

        try
        {
            // Notify listeners
            var eventArgs = new InterruptEventArgs(reason, description);
            InterruptTriggered?.Invoke(this, eventArgs);

            // Stop voice chat if active
            if (_voiceChatService?.IsVoiceChatActive == true)
            {
                await _voiceChatService.StopVoiceChatAsync();
                _logger?.LogInformation("Voice chat stopped due to {Reason}", reason);
            }
            else
            {
                _logger?.LogDebug("Interrupt received but voice chat is not active");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error processing interrupt");
        }
    }

    public void Dispose()
    {
        _ = ShutdownAsync();
        
        _vadDetector?.Dispose();
    }
}

/// <summary>
/// Event arguments for interrupt events
/// </summary>
public class InterruptEventArgs : EventArgs
{
    public AbortReason Reason { get; }
    public string Description { get; }
    public DateTime Timestamp { get; }

    public InterruptEventArgs(AbortReason reason, string description)
    {
        Reason = reason;
        Description = description;
        Timestamp = DateTime.UtcNow;
    }
}
