using PortAudioSharp;
using Verdure.Assistant.Core.Interfaces;

namespace Verdure.Assistant.Core.Services;

/// <summary>
/// PortAudioSharp2实现的音频录制器
/// </summary>
public class PortAudioRecorder : IAudioRecorder, IDisposable
{
    private PortAudioSharp.Stream? _inputStream;
    private bool _isRecording;
    private readonly List<byte> _recordedData = new();
    private readonly object _lock = new();
    private bool _isDisposed = false;

    public event EventHandler<byte[]>? DataAvailable;
    public event EventHandler? RecordingStopped;

    public bool IsRecording => _isRecording;      public async Task StartRecordingAsync(int sampleRate, int channels)
    {
        if (_isRecording || _isDisposed) return;

        try
        {
            // 使用 PortAudioManager 确保正确初始化
            if (!PortAudioManager.Instance.AcquireReference())
            {
                throw new InvalidOperationException("无法初始化 PortAudio");
            }
            
            // 获取默认输入设备
            var defaultInputDevice = PortAudio.DefaultInputDevice;
            if (defaultInputDevice == -1)
            {
                PortAudioManager.Instance.ReleaseReference();
                throw new InvalidOperationException("未找到音频输入设备");
            }
            
            // 计算帧大小 (60ms帧，匹配Python配置)
            uint frameSize = (uint)(sampleRate * 60 / 1000);
            
            // 配置音频流参数
            var inputParameters = new StreamParameters
            {
                device = defaultInputDevice,
                channelCount = channels,
                sampleFormat = SampleFormat.Int16, // 匹配Python paInt16
                suggestedLatency = PortAudio.GetDeviceInfo(defaultInputDevice).defaultLowInputLatency
            };

            // 创建输入流
            _inputStream = new PortAudioSharp.Stream(
                inputParameters,
                null,
                sampleRate,
                frameSize, // 60ms帧大小，匹配Python配置
                StreamFlags.ClipOff, // 匹配Python配置
                OnAudioDataReceived,
                IntPtr.Zero);

            // 开始录制
            _inputStream.Start();
            _isRecording = true;

            Console.WriteLine($"音频录制器初始化成功: {sampleRate}Hz, {channels}声道, 帧大小: {frameSize}");
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _isRecording = false;
            PortAudioManager.Instance.ReleaseReference();
            throw new Exception($"启动音频录制失败: {ex.Message}", ex);
        }
    }    public async Task StopRecordingAsync()
    {
        if (!_isRecording) return;

        try
        {
            _isRecording = false;

            // 安全停止和释放音频流
            if (_inputStream != null)
            {
                try
                {
                    _inputStream.Stop();
                    _inputStream.Close();
                    _inputStream.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"停止音频流时出现警告: {ex.Message}");
                }
                finally
                {
                    _inputStream = null;
                }
            }

            // 释放 PortAudio 引用
            PortAudioManager.Instance.ReleaseReference();

            RecordingStopped?.Invoke(this, EventArgs.Empty);
            Console.WriteLine("音频录制已停止");

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"停止音频录制时出错: {ex.Message}");
        }
    }private StreamCallbackResult OnAudioDataReceived(
        IntPtr input,
        IntPtr output,
        uint frameCount,
        ref StreamCallbackTimeInfo timeInfo,
        StreamCallbackFlags statusFlags,
        IntPtr userData)
    {
        try
        {
            if (input != IntPtr.Zero && frameCount > 0 && _isRecording)
            {
                // 计算数据大小 (16-bit samples * channels)
                // 从流参数获取通道数，这里假设单声道(1通道)匹配Python配置
                int channels = 1; // 应该从流参数获取，但目前假设单声道
                int dataSize = (int)(frameCount * channels * 2); // 16位 = 2字节/样本
                var audioData = new byte[dataSize];

                // 从非托管内存复制数据
                System.Runtime.InteropServices.Marshal.Copy(input, audioData, 0, dataSize);

                // 检查音频数据质量，过滤掉异常数据
                if (IsValidAudioData(audioData))
                {
                    lock (_lock)
                    {
                        // 限制录制数据的大小，避免内存积累
                        const int maxRecordedDataSize = 1024 * 1024; // 1MB限制
                        if (_recordedData.Count > maxRecordedDataSize)
                        {
                            // 保留最近的一半数据
                            var keepSize = maxRecordedDataSize / 2;
                            _recordedData.RemoveRange(0, _recordedData.Count - keepSize);
                        }
                        
                        _recordedData.AddRange(audioData);
                    }

                    // 触发数据可用事件
                    DataAvailable?.Invoke(this, audioData);
                }
            }

            return StreamCallbackResult.Continue;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"音频数据处理错误: {ex.Message}");
            return StreamCallbackResult.Abort;
        }
    }

    private bool IsValidAudioData(byte[] audioData)
    {
        // 简单的音频数据验证，检查是否全为零或包含有效音频信号
        if (audioData == null || audioData.Length == 0) return false;
        
        // 检查是否不全为零（完全静音可能表示设备问题）
        for (int i = 0; i < Math.Min(audioData.Length, 100); i += 2) // 检查前50个样本
        {
            if (audioData[i] != 0 || (i + 1 < audioData.Length && audioData[i + 1] != 0))
            {
                return true; // 发现非零数据
            }
        }
        
        return false; // 数据全为零，可能是设备问题
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
    }    public void Dispose()
    {
        if (_isDisposed) return;
        
        _isDisposed = true;
        
        try
        {
            StopRecordingAsync().Wait(5000); // 5秒超时
        }
        catch (Exception ex)
        {
            Console.WriteLine($"释放音频录制器资源时出现警告: {ex.Message}");
        }
    }
}
