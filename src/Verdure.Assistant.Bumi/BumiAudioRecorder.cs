using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Noetix.Sdk.Bumi;
using Verdure.Assistant.Core.Interfaces;

namespace Verdure.Assistant.Bumi;

/// <summary>
/// 把机器人 8 通道 / 16 kHz 的麦克风流桥接成助手需要的单声道 16 kHz PCM。
/// </summary>
/// <remarks>
/// 通道布局（实测）：ch0–3 是头顶四个麦克风，ch4–5 预留（恒为静音），ch6–7 是扬声器回采。
/// 这里只混 ch0–3，避免把扬声器的声音混进上行链路。
/// DDS 回调运行在原生线程上，所以先把数据放进有界通道，再由后台任务派发事件，避免阻塞回调。
/// </remarks>
public sealed class BumiAudioRecorder : ISharedAudioRecorder, IDisposable
{
    private const int MicChannelCount = 4;   // ch0–3
    private const int RobotChannelCount = 8; // 机器人固定 8 通道

    // Opus 编码器要求严格 60 ms 一帧（16 kHz 下 960 样本），长度不符时它会补零或截断，
    // 结果是"语音+静音"的碎片，服务端识别不出来。DDS 回调的块大小并不固定，
    // 所以这里先缓冲，凑满整帧再交出去。
    private const int OutputFrameSamples = 960;
    private const int OutputFrameBytes = OutputFrameSamples * 2;

    private readonly BumiMediaSession _session;
    private readonly ILogger<BumiAudioRecorder> _logger;
    private readonly BumiAudioOptions _options;
    private readonly double _inputGain;
    private readonly byte[] _pendingFrame = new byte[OutputFrameBytes];
    private int _pendingBytes;
    private int _windowPeak;
    private int _windowChunkSamples;
    private long _levelLoggedAt;
    private readonly object _gate = new();
    private readonly List<EventHandler<byte[]>> _subscribers = new();

    private Channel<byte[]>? _channel;
    private CancellationTokenSource? _cts;
    private Task? _pump;
    private bool _disposed;

    public BumiAudioRecorder(BumiMediaSession session, IOptions<BumiAudioOptions> options, ILogger<BumiAudioRecorder> logger)
    {
        _session = session;
        _options = options.Value;
        _logger = logger;
        _inputGain = _options.InputGainDb > 0 ? Math.Pow(10, _options.InputGainDb / 20.0) : 1.0;
    }

    public event EventHandler<byte[]>? DataAvailable;

    public event EventHandler? RecordingStopped;

    public bool IsRecording { get; private set; }

    public void SubscribeToAudioData(EventHandler<byte[]> handler)
    {
        lock (_gate)
        {
            _subscribers.Add(handler);
        }
    }

    public void UnsubscribeFromAudioData(EventHandler<byte[]> handler)
    {
        lock (_gate)
        {
            _subscribers.Remove(handler);
        }
    }

    public Task StartRecordingAsync(int sampleRate = 16000, int channels = 1)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsRecording)
            {
                return Task.CompletedTask;
            }

            if (sampleRate != _options.SampleRate)
            {
                _logger.LogWarning(
                    "The robot voice module is fixed at {RobotRate} Hz; the requested {Requested} Hz is ignored",
                    _options.SampleRate, sampleRate);
            }

            if (channels != 1)
            {
                _logger.LogWarning("Bumi capture is bridged to mono; the requested {Channels} channels are ignored", channels);
            }

            _channel = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(64)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.DropOldest,
            });

            _cts = new CancellationTokenSource();
            _pump = Task.Run(() => PumpAsync(_channel.Reader, _cts.Token));

            _session.Media.SubscribeInternalAudioCapture(OnAudio);

            IsRecording = true;
            _logger.LogInformation("Bumi capture started ({Rate} Hz, mono from 8ch)", _options.SampleRate);
        }

        return Task.CompletedTask;
    }

    public Task StopRecordingAsync()
    {
        Task? pump;
        CancellationTokenSource? cts;
        lock (_gate)
        {
            if (!IsRecording)
            {
                return Task.CompletedTask;
            }

            IsRecording = false;
            _session.Media.SubscribeInternalAudioCapture(null);
            _channel?.Writer.TryComplete();
            cts = _cts;
            pump = _pump;
            _cts = null;
            _pump = null;
            _channel = null;
        }

        cts?.Cancel();
        cts?.Dispose();
        pump?.Wait(TimeSpan.FromSeconds(2));

        _logger.LogInformation("Bumi capture stopped");
        RecordingStopped?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public void ForceCleanup()
    {
        try
        {
            StopRecordingAsync().Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bumi capture cleanup ran into an error");
        }
    }

    private void OnAudio(in AudioStream stream)
    {
        var data = stream.AudioData;
        if (data.IsEmpty)
        {
            return;
        }

        var channels = (int)(stream.Channels == 0 ? RobotChannelCount : stream.Channels);
        if (channels < MicChannelCount)
        {
            return;
        }

        var frames = data.Length / channels;
        if (frames == 0)
        {
            return;
        }

        var mono = new byte[frames * 2];
        var peak = 0;
        for (var i = 0; i < frames; i++)
        {
            var sum = 0;
            var offset = i * channels;
            for (var c = 0; c < MicChannelCount; c++)
            {
                sum += data[offset + c];
            }

            var mixed = sum / MicChannelCount;
            var amplified = (int)Math.Clamp(mixed * _inputGain, short.MinValue, short.MaxValue);
            var magnitude = Math.Abs(amplified);
            if (magnitude > peak)
            {
                peak = magnitude;
            }

            mono[i * 2] = (byte)(amplified & 0xFF);
            mono[(i * 2) + 1] = (byte)((amplified >> 8) & 0xFF);
        }

        LogLevel(peak, frames);
        EmitFrames(mono);
    }

    /// <summary>把单声道数据攒成固定的 60 ms 帧再交给上层。</summary>
    private void EmitFrames(byte[] mono)
    {
        var offset = 0;
        while (offset < mono.Length)
        {
            var take = Math.Min(OutputFrameBytes - _pendingBytes, mono.Length - offset);
            Buffer.BlockCopy(mono, offset, _pendingFrame, _pendingBytes, take);
            _pendingBytes += take;
            offset += take;

            if (_pendingBytes == OutputFrameBytes)
            {
                _channel?.Writer.TryWrite((byte[])_pendingFrame.Clone());
                _pendingBytes = 0;
            }
        }
    }

    /// <summary>每两秒报一次采集峰值和回调块大小，便于确认增益与分帧是否合适。</summary>
    private void LogLevel(int peak, int chunkSamples)
    {
        if (peak > _windowPeak)
        {
            _windowPeak = peak;
        }

        if (chunkSamples > _windowChunkSamples)
        {
            _windowChunkSamples = chunkSamples;
        }

        var now = Environment.TickCount64;
        if (now - _levelLoggedAt < 2000)
        {
            return;
        }

        _levelLoggedAt = now;
        var dbfs = _windowPeak > 0 ? 20 * Math.Log10(_windowPeak / 32768.0) : -96.0;
        _logger.LogDebug(
            "采集峰值 {Peak}（约 {Dbfs:F1} dBFS），回调块最大 {Samples} 样本",
            _windowPeak, dbfs, _windowChunkSamples);
        _windowPeak = 0;
        _windowChunkSamples = 0;
    }

    private async Task PumpAsync(ChannelReader<byte[]> reader, CancellationToken token)
    {
        try
        {
            await foreach (var chunk in reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                try
                {
                    DataAvailable?.Invoke(this, chunk);
                    EventHandler<byte[]>[] handlers;
                    lock (_gate)
                    {
                        handlers = _subscribers.ToArray();
                    }

                    foreach (var handler in handlers)
                    {
                        handler(this, chunk);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "An audio consumer threw while handling Bumi capture data");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        ForceCleanup();
        _disposed = true;
        lock (_gate)
        {
            _subscribers.Clear();
        }
    }
}