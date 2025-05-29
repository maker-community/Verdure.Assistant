using Microsoft.Extensions.Logging;
using NAudio.Wave;
using XiaoZhi.Core.Constants;
using XiaoZhi.Core.Interfaces;
using XiaoZhi.Core.Models;

namespace XiaoZhi.Core.Services;

/// <summary>
/// Voice Activity Detection service for automatic interruption detection
/// Based on the Python py-xiaozhi VAD implementation
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
    }

    private void OnAudioDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_isPaused || !_isRunning)
            return;

        try
        {
            // Only process during SPEAKING state (when XiaoZhi is talking)
            if (_lastDeviceState != DeviceState.Speaking)
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

            // Process complete frames
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
    }

    private void ProcessAudioFrames()
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
    }

    private bool DetectSpeechInFrame(float[] frame)
    {
        // Calculate energy (RMS)
        double energy = 0;
        for (int i = 0; i < frame.Length; i++)
        {
            energy += frame[i] * frame[i];
        }
        energy = Math.Sqrt(energy / frame.Length) * 32768; // Convert back to 16-bit scale
        
        // Simple energy-based VAD (could be enhanced with spectral features)
        return energy > EnergyThreshold;
    }

    private void HandleSpeechFrame()
    {
        _speechFrameCount++;
        _silenceFrameCount = 0;

        // Check if we have enough consecutive speech frames to trigger interrupt
        if (_speechFrameCount >= SpeechWindow && !_interruptTriggered)
        {
            _interruptTriggered = true;
            _logger?.LogInformation("Voice interrupt detected - user speaking during XiaoZhi response");
            
            // Trigger the interrupt
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
    }

    private void OnDeviceStateChanged(object? sender, DeviceState newState)
    {
        _lastDeviceState = newState;
        
        // Reset VAD state when device state changes
        if (newState != DeviceState.Speaking)
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
