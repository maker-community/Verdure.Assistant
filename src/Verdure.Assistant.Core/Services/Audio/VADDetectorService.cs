using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Constants;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Models;

namespace Verdure.Assistant.Core.Services;

/// <summary>
/// Voice Activity Detection service - Voice interruption disabled due to poor echo cancellation optimization
/// 
/// Features:
/// - Voice interruption logic disabled (poor echo cancellation causes false positives)
/// - Other interruption methods remain active (F3 hotkey, manual interrupts)
/// - Uses PortAudio instead of NAudio for better performance
/// - Maintains VAD infrastructure for potential future use
/// </summary>
public class VADDetectorService : IDisposable
{
    private readonly ILogger<VADDetectorService>? _logger;
    private readonly IVoiceChatService _voiceChatService;    private readonly IAudioRecorder? _audioRecorder;

    private bool _isRunning = false;
    private bool _isPaused = false;
    private CancellationTokenSource? _cancellationTokenSource;
    
    // VAD parameters (maintained for future use)
    private const int SampleRate = 16000;
    private const int FrameDurationMs = 20;
    private const int FrameSize = SampleRate * FrameDurationMs / 1000; // 320 samples
    private const double EnergyThreshold = 300.0;
    
    // State tracking
    private DeviceState _lastDeviceState = DeviceState.Idle;
    
    // Voice interruption disabled - events maintained for compatibility
    public event EventHandler<bool>? VoiceInterruptDetected;
    
    public bool IsRunning => _isRunning && !_isPaused;
    public bool IsPaused => _isPaused;

    public VADDetectorService(IVoiceChatService voiceChatService, IAudioRecorder? audioRecorder = null, ILogger<VADDetectorService>? logger = null)
    {
        _voiceChatService = voiceChatService;
        _audioRecorder = audioRecorder;
        _logger = logger;
        
        // Subscribe to device state changes
        _voiceChatService.DeviceStateChanged += OnDeviceStateChanged;
    }

    public void Start()
    {
        if (_isRunning)
        {
            _logger?.LogWarning("VAD detector is already running");
            return;
        }

        try
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _isRunning = true;
            _isPaused = false;
            
            // Voice interruption disabled - no audio monitoring started
            _logger?.LogInformation("VAD detector started (voice interruption disabled)");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to start VAD detector");
            Stop();
        }
    }

    public void Stop()
    {
        _isRunning = false;
        _isPaused = false;
        
        try
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            
            _logger?.LogInformation("VAD detector stopped");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error stopping VAD detector");
        }
    }

    public void Pause()
    {
        _isPaused = true;
        _logger?.LogInformation("VAD detector paused");
    }

    public void Resume()
    {
        _isPaused = false;
        _logger?.LogInformation("VAD detector resumed");
    }

    private void OnDeviceStateChanged(object? sender, DeviceState newState)
    {
        _lastDeviceState = newState;
        
        // Voice interruption disabled - no action taken on state changes
        _logger?.LogDebug("VAD detector notified of device state change: {State} (voice interruption disabled)", newState);
    }

    public void Dispose()
    {
        Stop();
        
        if (_voiceChatService != null)
        {
            _voiceChatService.DeviceStateChanged -= OnDeviceStateChanged;
        }
    }
}
