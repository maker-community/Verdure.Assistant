using Microsoft.Extensions.Logging;
using NAudio.Wave;
using XiaoZhi.Core.Constants;
using XiaoZhi.Core.Interfaces;
using XiaoZhi.Core.Models;

namespace XiaoZhi.Core.Services;

/// <summary>
/// Voice Activity Detection service for automatic interruption detection and voice activation
/// Based on the Python py-xiaozhi VAD implementation with continuous monitoring capabilities
/// 
/// Features:
/// - Speaking State: Monitors for user voice to interrupt AI response immediately
/// - Idle State: Monitors for user voice to auto-start listening (when KeepListening enabled)
/// - Continuous monitoring during playback for immediate conversation interruption
/// </summary>
public class VADDetectorService : IDisposable
{
    private readonly ILogger<VADDetectorService>? _logger;
    private readonly IVoiceChatService _voiceChatService;
    
    // Audio capture components
    private WaveInEvent? _waveIn;
    private bool _isRunning = false;
    private bool _isPaused = false;
      // VAD parameters (based on Python implementation)
    private const int SampleRate = 16000;
    private const int FrameDurationMs = 20;
    private const int FrameSize = SampleRate * FrameDurationMs / 1000; // 320 samples
    private const int SpeechWindow = 5; // Consecutive speech frames to trigger interrupt
    private const double EnergyThreshold = 300.0;
    
    // Enhanced thresholds for different states
    private const double IdleStateEnergyThreshold = 500.0; // Higher threshold for idle state activation
    private const int IdleStateSpeechWindow = 8; // Longer window for idle state to avoid false positives
    
    // State tracking
    private int _speechFrameCount = 0;
    private int _silenceFrameCount = 0;
    private bool _interruptTriggered = false;
    private DeviceState _lastDeviceState = DeviceState.Idle;
    
    // Audio buffer for processing
    private readonly Queue<float> _audioBuffer = new();
    private readonly object _bufferLock = new();

    public event EventHandler<bool>? VoiceInterruptDetected;
    
    public bool IsRunning => _isRunning && !_isPaused;
    public bool IsPaused => _isPaused;

    public VADDetectorService(IVoiceChatService voiceChatService, ILogger<VADDetectorService>? logger = null)
    {
        _voiceChatService = voiceChatService;
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
            InitializeAudioCapture();
            _isRunning = true;
            _isPaused = false;
            ResetState();
            
            _logger?.LogInformation("VAD detector started successfully");
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
            _waveIn?.StopRecording();
            _waveIn?.Dispose();
            _waveIn = null;
            
            ResetState();
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
        ResetState();
        _logger?.LogInformation("VAD detector paused");
    }

    public void Resume()
    {
        _isPaused = false;
        ResetState();
        _logger?.LogInformation("VAD detector resumed");
    }

    private void InitializeAudioCapture()
    {
        _waveIn = new WaveInEvent
        {
            WaveFormat = new WaveFormat(SampleRate, 16, 1), // 16kHz, 16-bit, mono
            BufferMilliseconds = FrameDurationMs
        };

        _waveIn.DataAvailable += OnAudioDataAvailable;
        _waveIn.RecordingStopped += OnRecordingStopped;
        
        _waveIn.StartRecording();
        _logger?.LogDebug("Audio capture initialized for VAD detection");
    }    private void OnAudioDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_isPaused || !_isRunning)
            return;

        try
        {
            // Enable continuous monitoring during Speaking and Idle states
            // Speaking: Monitor for user interruption during AI response
            // Idle: Monitor for voice activity to trigger automatic listening (if in auto mode)
            if (_lastDeviceState != DeviceState.Speaking && _lastDeviceState != DeviceState.Idle)
            {
                ResetState();
                return;
            }

            // Convert byte array to float array
            var floatData = ConvertBytesToFloat(e.Buffer, e.BytesRecorded);
            
            lock (_bufferLock)
            {
                foreach (var sample in floatData)
                {
                    _audioBuffer.Enqueue(sample);
                }
            }

            // Process complete frames with state-aware handling
            ProcessAudioFrames();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error processing audio data in VAD detector");
        }
    }

    private float[] ConvertBytesToFloat(byte[] buffer, int bytesRecorded)
    {
        var samples = bytesRecorded / 2; // 16-bit samples
        var floatData = new float[samples];
        
        for (int i = 0; i < samples; i++)
        {
            var sample = BitConverter.ToInt16(buffer, i * 2);
            floatData[i] = sample / 32768.0f; // Normalize to [-1, 1]
        }
        
        return floatData;
    }    private void ProcessAudioFrames()
    {
        lock (_bufferLock)
        {
            while (_audioBuffer.Count >= FrameSize)
            {
                // Extract one frame
                var frame = new float[FrameSize];
                for (int i = 0; i < FrameSize; i++)
                {
                    frame[i] = _audioBuffer.Dequeue();
                }

                // Analyze the frame
                bool isSpeech = DetectSpeechInFrame(frame);
                
                if (isSpeech)
                {
                    HandleSpeechFrame();
                }
                else
                {
                    HandleSilenceFrame();
                }
            }
        }
    }    private bool DetectSpeechInFrame(float[] frame)
    {
        // Calculate energy (RMS)
        double energy = 0;
        for (int i = 0; i < frame.Length; i++)
        {
            energy += frame[i] * frame[i];
        }
        energy = Math.Sqrt(energy / frame.Length) * 32768; // Convert back to 16-bit scale
        
        // Use different thresholds based on current device state
        double threshold = _lastDeviceState switch
        {
            DeviceState.Speaking => EnergyThreshold, // Lower threshold for interruption during speaking
            DeviceState.Idle => IdleStateEnergyThreshold, // Higher threshold for activation from idle
            _ => EnergyThreshold
        };
        
        return energy > threshold;
    }    private void HandleSpeechFrame()
    {
        _speechFrameCount++;
        _silenceFrameCount = 0;

        // Use different speech windows based on current device state
        int requiredFrames = _lastDeviceState switch
        {
            DeviceState.Speaking => SpeechWindow, // Fast response for interruption
            DeviceState.Idle => IdleStateSpeechWindow, // Longer window to avoid false activation
            _ => SpeechWindow
        };

        // Check if we have enough consecutive speech frames to trigger action
        if (_speechFrameCount >= requiredFrames && !_interruptTriggered)
        {
            _interruptTriggered = true;

            // Handle different behaviors based on current device state
            switch (_lastDeviceState)
            {
                case DeviceState.Speaking:
                    // User is interrupting AI response - immediate stop
                    _logger?.LogInformation("Voice interrupt detected - user speaking during XiaoZhi response");
                    VoiceInterruptDetected?.Invoke(this, true);
                    
                    // Automatically stop the voice chat
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await _voiceChatService.StopVoiceChatAsync();
                            _logger?.LogInformation("Voice chat stopped due to VAD interrupt");
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "Failed to stop voice chat after VAD interrupt");
                        }
                    });
                    break;

                case DeviceState.Idle:
                    // User started speaking while idle - potentially start listening if in auto mode
                    if (_voiceChatService.KeepListening)
                    {
                        _logger?.LogInformation("Voice activity detected in idle state - auto-starting listening");
                        VoiceInterruptDetected?.Invoke(this, true);
                        
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await _voiceChatService.StartVoiceChatAsync();
                                _logger?.LogInformation("Voice chat started due to voice activity in idle state");
                            }
                            catch (Exception ex)
                            {
                                _logger?.LogError(ex, "Failed to start voice chat after voice activity detection");
                            }
                        });
                    }
                    else
                    {
                        _logger?.LogDebug("Voice activity detected in idle state, but auto-listening is disabled");
                    }
                    break;

                default:
                    _logger?.LogDebug("Voice activity detected in state {State}, no action taken", _lastDeviceState);
                    break;
            }
        }
    }

    private void HandleSilenceFrame()
    {
        _silenceFrameCount++;
        _speechFrameCount = 0;
    }

    private void ResetState()
    {
        _speechFrameCount = 0;
        _silenceFrameCount = 0;
        _interruptTriggered = false;
        
        lock (_bufferLock)
        {
            _audioBuffer.Clear();
        }
    }    private void OnDeviceStateChanged(object? sender, DeviceState newState)
    {
        _lastDeviceState = newState;
        
        // Reset VAD state when transitioning to non-monitored states
        // We monitor Speaking (for interruption) and Idle (for auto-activation)
        if (newState != DeviceState.Speaking && newState != DeviceState.Idle)
        {
            ResetState();
        }
        
        _logger?.LogDebug("VAD detector notified of device state change: {State}", newState);
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            _logger?.LogError(e.Exception, "VAD audio recording stopped with error");
        }
        else
        {
            _logger?.LogDebug("VAD audio recording stopped normally");
        }
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
