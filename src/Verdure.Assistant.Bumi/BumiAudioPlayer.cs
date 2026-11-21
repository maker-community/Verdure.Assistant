using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Noetix.Sdk.Bumi;
using Verdure.Assistant.Core.Interfaces;

namespace Verdure.Assistant.Bumi;

/// <summary>
/// 把助手输出的 16 kHz PCM 桥接到机器人扬声器。
/// </summary>
/// <remarks>
/// 机器人播放话题只接受 <b>2 声道</b> 16 kHz，单声道会被当成双声道交错来解释，听感就是破音。
/// 所以这里把 mono 上混成立体声，并按 20 ms 一帧、用墙钟节拍发布。
/// 采用与 SoundFlowAudioPlayer 相同的行为：PlayAsync 只入队，后台任务负责节奏。
/// </remarks>
public sealed class BumiAudioPlayer : IAudioPlayer, IDisposable
{
    private readonly BumiMediaSession _session;
    private readonly ILogger<BumiAudioPlayer> _logger;
    private readonly BumiAudioOptions _options;
    private readonly object _gate = new();

    private Channel<short[]>? _channel;
    private CancellationTokenSource? _cts;
    private Task? _pump;
    private ulong _messageId;
    private bool _disposed;

    // TTS 帧是网络突发到达的，边到边放会被抖动切成一段一段。先攒够前导缓冲再开始放（PlaybackLeadMs）。
    private int _queuedSamples;
    // 时间戳跨句单调递增：原生播放侧按时间轴排程，重新从 0 开始会让它以为是回退。
    private long _framesPublished;

    private BumiToneShaper? _shaper;
    private readonly BumiTtsDumper? _dump;

    // 统计 TTS 帧的到达节奏：服务端如果送得比实时慢，任何播放器都补不出连续的声音。
    private readonly Stopwatch _arrivalClock = Stopwatch.StartNew();
    private long _lastArrivalMs = -1;
    private long _firstArrivalMs = -1;
    private long _maxArrivalGapMs;
    private int _arrivalCount;

    public BumiAudioPlayer(BumiMediaSession session, IOptions<BumiAudioOptions> options, ILogger<BumiAudioPlayer> logger)
    {
        _session = session;
        _options = options.Value;
        _logger = logger;
        _shaper = new BumiToneShaper(_options);

        if (!string.IsNullOrWhiteSpace(_options.DumpTtsDirectory))
        {
            _dump = new BumiTtsDumper(_options.DumpTtsDirectory, _options.SampleRate, _logger);
        }
    }

    public event EventHandler? PlaybackStopped;

    public bool IsPlaying { get; private set; }

    public Task PlayAsync(byte[] audioData, int sampleRate = 16000, int channels = 1)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (audioData is null || audioData.Length < 2)
        {
            return Task.CompletedTask;
        }

        if (sampleRate != _options.SampleRate)
        {
            _logger.LogWarning(
                "The robot speaker runs at {RobotRate} Hz; audio at {Requested} Hz will sound wrong",
                _options.SampleRate, sampleRate);
        }

        var inputChannels = Math.Max(1, channels);
        var frames = audioData.Length / 2 / inputChannels;
        if (frames == 0)
        {
            return Task.CompletedTask;
        }

        var stereo = new short[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            var sample = ReadSample(audioData, i * inputChannels);
            var value = Shape(sample);
            stereo[i * 2] = value;
            stereo[(i * 2) + 1] = value;
        }

        EnsureChannel();
        _channel?.Writer.TryWrite(stereo);
        _queuedSamples += stereo.Length;

        TrackArrival();

        // 短句可能永远攒不满前导缓冲，交给 CompleteAdding/StopAsync 兜底放行。
        if (_pump is null && QueuedMs() >= _options.PlaybackLeadMs)
        {
            StartPump();
        }

        return Task.CompletedTask;
    }

    private double QueuedMs() => _queuedSamples * 1000.0 / (_options.SampleRate * 2);

    private void TrackArrival()
    {
        var now = _arrivalClock.ElapsedMilliseconds;
        if (_firstArrivalMs < 0)
        {
            _firstArrivalMs = now;
        }

        if (_lastArrivalMs >= 0)
        {
            var gap = now - _lastArrivalMs;
            if (gap > _maxArrivalGapMs)
            {
                _maxArrivalGapMs = gap;
            }
        }

        _lastArrivalMs = now;
        _arrivalCount++;

        if (_arrivalCount % 50 != 0)
        {
            return;
        }

        _logger.LogDebug(
            "TTS 帧到达统计：{Count} 帧，最大间隔 {Gap} ms（帧长 {Frame} ms，前导缓冲 {Lead} ms）",
            _arrivalCount, _maxArrivalGapMs, _options.PlaybackFrameMs, _options.PlaybackLeadMs);
    }

    /// <summary>一句话结束时收尾统计，下一句重新计数（句间的空档不属于抖动）。</summary>
    private void FinishArrivalWindow()
    {
        if (_arrivalCount == 0)
        {
            return;
        }

        _logger.LogDebug(
            "本句 TTS：{Count} 帧（音频约 {Seconds:F1}s），实际跨度 {Span} ms，帧内最大间隔 {Gap} ms",
            _arrivalCount, _arrivalCount * 0.06,
            _lastArrivalMs - _firstArrivalMs, _maxArrivalGapMs);

        _arrivalCount = 0;
        _maxArrivalGapMs = 0;
        _lastArrivalMs = -1;
        _firstArrivalMs = -1;
    }

    public Task StopAsync()
    {
        Channel<short[]>? channel;
        Task? pump;
        CancellationTokenSource? cts;
        lock (_gate)
        {
            channel = _channel;
            pump = _pump;
            cts = _cts;
            _channel = null;
            _pump = null;
            _cts = null;
            _queuedSamples = 0;
            IsPlaying = false;
        }

        FinishArrivalWindow();

        channel?.Writer.TryComplete();
        cts?.Cancel();
        pump?.Wait(TimeSpan.FromSeconds(2));
        cts?.Dispose();
        ResetFilter();
        _dump?.Flush();
        return Task.CompletedTask;
    }

    public void CompleteAdding()
    {
        FinishArrivalWindow();

        // 不足一个前导缓冲的短句也要放出来。
        if (_pump is null && _channel is not null)
        {
            StartPump();
        }

        _channel?.Writer.TryComplete();
    }

    public void Reset()
    {
        try
        {
            StopAsync().Wait(TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Bumi playback reset ran into an error");
        }

        ResetFilter();
    }

    private void ResetFilter()
    {
        _shaper?.Reset();
    }

    private short Shape(short sample) => _shaper?.Process(sample) ?? sample;

    private static short ReadSample(byte[] data, int index)
    {
        var offset = index * 2;
        return (short)(data[offset] | (data[offset + 1] << 8));
    }

    private void EnsureChannel()
    {
        if (_channel is not null || _disposed)
        {
            return;
        }

        lock (_gate)
        {
            if (_channel is not null || _disposed)
            {
                return;
            }

            _channel = Channel.CreateUnbounded<short[]>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
            });
        }
    }

    private void StartPump()
    {
        lock (_gate)
        {
            if (_pump is not null || _channel is null || _disposed)
            {
                return;
            }

            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            var reader = _channel.Reader;
            _pump = Task.Run(() => PumpAsync(reader, token));
        }
    }

    private async Task PumpAsync(ChannelReader<short[]> reader, CancellationToken token)
    {
        var frameMs = Math.Clamp(_options.PlaybackFrameMs, 10, 500);
        var samplesPerFrame = _options.SampleRate * frameMs / 1000 * 2; // 立体声：样本数含左右
        var paceRealtime = _options.PlaybackPaceRealtime;
        var clock = Stopwatch.StartNew();
        var framesThisRun = 0L;
        IsPlaying = true;

        try
        {
            await foreach (var chunk in reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                Interlocked.Add(ref _queuedSamples, -chunk.Length);

                for (var offset = 0; offset < chunk.Length; offset += samplesPerFrame)
                {
                    token.ThrowIfCancellationRequested();
                    var count = Math.Min(samplesPerFrame, chunk.Length - offset);
                    var frame = new short[count];
                    Array.Copy(chunk, offset, frame, 0, count);

                    Publish(frame, _framesPublished * frameMs, frameMs);
                    _framesPublished++;
                    framesThisRun++;

                    if (!paceRealtime)
                    {
                        continue;
                    }

                    var target = TimeSpan.FromMilliseconds(framesThisRun * (double)frameMs);
                    var wait = target - clock.Elapsed;
                    if (wait > TimeSpan.Zero)
                    {
                        await Task.Delay(wait, token).ConfigureAwait(false);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Bumi playback pump failed");
        }
        finally
        {
            IsPlaying = false;
            lock (_gate)
            {
                _pump = null;
                _cts = null;
            }

            PlaybackStopped?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Publish(short[] samples, long elapsedMs, int frameMs)
    {
        _dump?.Write(samples);

        var stream = new AudioStream
        {
            Header = new MediaHeader(++_messageId, (ulong)(elapsedMs * 1000), string.Empty),
            TimestampUs = (ulong)(elapsedMs * 1000),
            Channels = 2,
            SampleRate = (uint)_options.SampleRate,
            Format = 2,
            DurationMs = (uint)frameMs,
            AudioData = samples,
        };

        _session.Media.PublishExternalAudioPlaybackStream(in stream);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            StopAsync().Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
        }

        _disposed = true;
        _dump?.Dispose();
    }
}