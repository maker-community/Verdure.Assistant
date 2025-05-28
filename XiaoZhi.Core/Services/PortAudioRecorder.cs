using PortAudioSharp;
using XiaoZhi.Core.Interfaces;

namespace XiaoZhi.Core.Services;

/// <summary>
/// PortAudioSharp2实现的音频录制器
/// </summary>
public class PortAudioRecorder : IAudioRecorder
{
    private PortAudioSharp.Stream? _inputStream;
    private bool _isRecording;
    private readonly List<byte> _recordedData = new();
    private readonly object _lock = new();

    public event EventHandler<byte[]>? DataAvailable;
    public event EventHandler? RecordingStopped;

    public bool IsRecording => _isRecording;    
    public async Task StartRecordingAsync(int sampleRate, int channels)
    {
        if (_isRecording) return;

        try
        {
            // 初始化PortAudio
            PortAudio.Initialize();            
            // 获取默认输入设备
            var defaultInputDevice = PortAudio.DefaultInputDevice;
            if (defaultInputDevice == -1)
                throw new InvalidOperationException("未找到音频输入设备");
            // 配置音频流参数
            var inputParameters = new StreamParameters
            {
                device = defaultInputDevice,
                channelCount = channels,
                sampleFormat = SampleFormat.Int16,
                suggestedLatency = PortAudio.GetDeviceInfo(defaultInputDevice).defaultLowInputLatency
            };

            // 创建输入流
            _inputStream = new PortAudioSharp.Stream(
                inputParameters,
                null,
                sampleRate,
                1024, // 帧大小
                StreamFlags.NoFlag,
                OnAudioDataReceived,
                IntPtr.Zero);

            // 开始录制
            _inputStream.Start();
            _isRecording = true;

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _isRecording = false;
            throw new Exception($"启动音频录制失败: {ex.Message}", ex);
        }
    }    
    public async Task StopRecordingAsync()
    {
        if (!_isRecording) return;

        try
        {
            _inputStream?.Stop();
            _inputStream?.Close();
            _inputStream?.Dispose();
            _inputStream = null;

            PortAudio.Terminate();

            _isRecording = false;
            RecordingStopped?.Invoke(this, EventArgs.Empty);

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"停止音频录制时出错: {ex.Message}");
        }
    }

    private StreamCallbackResult OnAudioDataReceived(
        IntPtr input,
        IntPtr output,
        uint frameCount,
        ref StreamCallbackTimeInfo timeInfo,
        StreamCallbackFlags statusFlags,
        IntPtr userData)
    {
        try
        {
            if (input != IntPtr.Zero && frameCount > 0)
            {
                // 计算数据大小 (16-bit samples)
                int dataSize = (int)(frameCount * 2); // 假设单声道，16位
                var audioData = new byte[dataSize];

                // 从非托管内存复制数据
                System.Runtime.InteropServices.Marshal.Copy(input, audioData, 0, dataSize);

                lock (_lock)
                {
                    _recordedData.AddRange(audioData);
                }

                // 触发数据可用事件
                DataAvailable?.Invoke(this, audioData);
            }

            return StreamCallbackResult.Continue;
        }
        catch (Exception ex)
        {
            System.Console.WriteLine($"音频数据处理错误: {ex.Message}");
            return StreamCallbackResult.Abort;
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
        StopRecordingAsync().Wait();
    }
}
