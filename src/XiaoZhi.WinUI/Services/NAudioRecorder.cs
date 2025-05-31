using NAudio.Wave;
using XiaoZhi.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace XiaoZhi.WinUI.Services;

/// <summary>
/// NAudio实现的音频录制器
/// </summary>
public class NAudioRecorder : IAudioRecorder, IDisposable
{
    private WaveInEvent? _waveIn;
    private bool _isRecording;
    private readonly List<byte> _recordedData = new();
    private readonly object _lock = new();
    private readonly ILogger<NAudioRecorder>? _logger;
    private int _sampleRate;
    private int _channels;

    public event EventHandler<byte[]>? DataAvailable;
    public event EventHandler? RecordingStopped;

    public bool IsRecording => _isRecording;

    public NAudioRecorder(ILogger<NAudioRecorder>? logger = null)
    {
        _logger = logger;
    }

    public async Task StartRecordingAsync(int sampleRate = 16000, int channels = 1)
    {
        if (_isRecording) return;

        try
        {
            _sampleRate = sampleRate;
            _channels = channels;

            // 创建WaveInEvent实例
            _waveIn = new WaveInEvent
            {
                WaveFormat = new WaveFormat(sampleRate, 16, channels), // 16位深度
                BufferMilliseconds = 60 // 60ms缓冲区，匹配Python配置
            };

            // 订阅数据可用事件
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;

            // 开始录制
            _waveIn.StartRecording();
            _isRecording = true;

            _logger?.LogInformation("音频录制已启动: {SampleRate}Hz, {Channels}声道", sampleRate, channels);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _isRecording = false;
            _logger?.LogError(ex, "启动音频录制失败");
            throw new Exception($"启动音频录制失败: {ex.Message}", ex);
        }
    }

    public async Task StopRecordingAsync()
    {
        if (!_isRecording) return;

        try
        {
            _waveIn?.StopRecording();
            _isRecording = false;

            _logger?.LogInformation("音频录制已停止");

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "停止音频录制时出错");
            Console.WriteLine($"停止音频录制时出错: {ex.Message}");
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        try
        {
            if (e.Buffer != null && e.BytesRecorded > 0)
            {
                // 复制音频数据
                var audioData = new byte[e.BytesRecorded];
                Array.Copy(e.Buffer, 0, audioData, 0, e.BytesRecorded);

                lock (_lock)
                {
                    _recordedData.AddRange(audioData);
                }

                // 触发数据可用事件
                DataAvailable?.Invoke(this, audioData);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "音频数据处理错误");
            Console.WriteLine($"音频数据处理错误: {ex.Message}");
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        try
        {
            _isRecording = false;
            RecordingStopped?.Invoke(this, EventArgs.Empty);

            if (e.Exception != null)
            {
                _logger?.LogError(e.Exception, "录制过程中发生错误");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理录制停止事件时出错");
        }
    }

    public byte[] GetRecordedData()
    {
        lock (_lock)
        {
            return _recordedData.ToArray();
        }
    }

    public void ClearRecordedData()
    {
        lock (_lock)
        {
            _recordedData.Clear();
        }
    }    
    public void Dispose()
    {
        try
        {
            // 停止录制并等待完成
            try
            {
                StopRecordingAsync().Wait(1000); // 最多等待1秒
            }
            catch (TimeoutException)
            {
                _logger?.LogWarning("停止音频录制超时");
            }
            
            lock (_lock)
            {
                if (_waveIn != null)
                {
                    try
                    {
                        // 取消事件订阅
                        _waveIn.DataAvailable -= OnDataAvailable;
                        _waveIn.RecordingStopped -= OnRecordingStopped;
                        
                        // 强制停止录制
                        if (_isRecording)
                        {
                            _waveIn.StopRecording();
                        }
                        
                        _waveIn.Dispose();
                        _waveIn = null;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "释放WaveIn时出错");
                    }
                }
                
                // 清理录制数据
                try
                {
                    _recordedData.Clear();
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "清理录制数据时出错");
                }
            }
            
            _isRecording = false;
            _logger?.LogDebug("NAudioRecorder资源已释放");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "释放NAudioRecorder资源时出错");
        }
    }
}
