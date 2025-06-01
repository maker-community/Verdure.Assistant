using Microsoft.Extensions.Logging;
using XiaoZhi.Core.Constants;
using XiaoZhi.Core.Interfaces;

namespace XiaoZhi.Core.Services;

/// <summary>
/// Enhanced interrupt manager that coordinates multiple interrupt sources
/// Based on the Python py-xiaozhi application abort mechanisms
/// </summary>
public class InterruptManager : IDisposable
{
    private readonly ILogger<InterruptManager>? _logger;
    private readonly IVoiceChatService _voiceChatService;
    private readonly VADDetectorService _vadDetector;
    private readonly GlobalHotkeyService _hotkeyService;
    
    // Interrupt state tracking
    private bool _isInitialized = false;
    private AbortReason _lastAbortReason = AbortReason.None;
    private DateTime _lastInterruptTime = DateTime.MinValue;
    private readonly TimeSpan _interruptCooldown = TimeSpan.FromMilliseconds(500);

    public event EventHandler<InterruptEventArgs>? InterruptTriggered;

    public bool IsVADEnabled { get; private set; } = true;
    public bool IsHotkeyEnabled { get; private set; } = true;
    public AbortReason LastAbortReason => _lastAbortReason;    public InterruptManager(
        IVoiceChatService voiceChatService,
        ILogger<InterruptManager>? logger = null)
    {
        _voiceChatService = voiceChatService;
        _logger = logger;
          // Initialize interrupt services
        // Pass null for audioRecorder since voice interruption is disabled
        _vadDetector = new VADDetectorService(_voiceChatService, null);
        _hotkeyService = new GlobalHotkeyService(_voiceChatService);
        
        // Subscribe to interrupt events
        _vadDetector.VoiceInterruptDetected += OnVADInterrupt;
        _hotkeyService.HotkeyPressed += OnHotkeyInterrupt;
    }

    public Task InitializeAsync()
    {        if (_isInitialized)
        {
            _logger?.LogWarning("Interrupt manager is already initialized");
            return Task.CompletedTask;
        }

        try
        {
            // Register global hotkey
            if (IsHotkeyEnabled)
            {
                var hotkeyRegistered = _hotkeyService.RegisterHotkey();
                if (hotkeyRegistered)
                {
                    _logger?.LogInformation("F3 hotkey interrupt enabled");
                }
                else
                {
                    _logger?.LogWarning("Failed to register F3 hotkey - hotkey interrupts disabled");
                    IsHotkeyEnabled = false;
                }
            }

            // Start VAD if enabled
            if (IsVADEnabled)
            {
                _vadDetector.Start();
                _logger?.LogInformation("VAD interrupt detection enabled");
            }            _isInitialized = true;
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
            _vadDetector.Stop();
            
            // Unregister hotkey
            _hotkeyService.UnregisterHotkey();
            
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
                _vadDetector.Start();
                _logger?.LogInformation("VAD interrupt detection enabled");
            }
            else
            {
                _vadDetector.Stop();
                _logger?.LogInformation("VAD interrupt detection disabled");
            }
        }
    }

    /// <summary>
    /// Pause VAD detection temporarily (e.g., during user speech input)
    /// </summary>
    public void PauseVAD()
    {
        if (IsVADEnabled && _vadDetector.IsRunning)
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
        if (IsVADEnabled && _vadDetector.IsPaused)
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

    private void OnHotkeyInterrupt(object? sender, bool pressed)
    {
        if (pressed)
        {
            _ = ProcessInterrupt(AbortReason.KeyboardInterruption, "F3 hotkey pressed");
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
            if (_voiceChatService.IsVoiceChatActive)
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
        _hotkeyService?.Dispose();
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
