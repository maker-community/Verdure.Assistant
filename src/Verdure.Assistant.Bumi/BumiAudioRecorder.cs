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

    private readonly BumiMediaSession _session;
    private readonly ILogger<BumiAudioRecorder> _logger;
    private readonly BumiAudioOptions _options;
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
        for (var i = 0; i < frames; i++)
        {
            var sum = 0;
            var offset = i * channels;
            for (var c = 0; c < MicChannelCount; c++)
            {
                sum += data[offset + c];
            }

            var mixed = (short)(sum / MicChannelCount);
            mono[i * 2] = (byte)(mixed & 0xFF);
            mono[(i * 2) + 1] = (byte)((mixed >> 8) & 0xFF);
        }

        _channel?.Writer.TryWrite(mono);
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