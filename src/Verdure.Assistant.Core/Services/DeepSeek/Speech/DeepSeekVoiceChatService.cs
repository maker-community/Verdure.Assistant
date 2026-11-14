using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Interfaces;

namespace Verdure.Assistant.Core.Services.DeepSeek.Speech;

public enum DeepSeekVoicePhase
{
    Idle,
    Listening,
    Transcribing,
    Thinking,
    Speaking
}

/// <summary>
/// DeepSeek 语音对话：麦克风 → 端点检测 → DeepSeek 语音识别 → DeepSeek 对话（流式、思考、工具）→ DeepSeek 流式语音合成 → 播放，
/// 连续模式下回复结束后自动继续聆听。与小智后端互斥，由 <see cref="VoiceBackendCoordinator"/> 协调。
/// </summary>
public sealed class DeepSeekVoiceChatService : IDisposable
{
    private static readonly TimeSpan PostSpeechGuard = TimeSpan.FromMilliseconds(500);

    private readonly ISharedAudioRecorder _recorder;
    private readonly IDeepSeekSpeechClient _speech;
    private readonly DeepSeekChatSession _session;
    private readonly IAudioPlayer _player;
    private readonly DeepSeekOptions _options;
    private readonly ILogger<DeepSeekVoiceChatService>? _logger;

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _stateLock = new();
    private readonly EventHandler<byte[]> _micHandler;

    private Channel<byte[]> _mic = NewMicChannel();
    private UtteranceDetector _detector;
    private CancellationTokenSource? _conversationCts;
    private CancellationTokenSource? _turnCts;
    private Task? _loopTask;
    private TaskCompletionSource? _pttReleased;
    private volatile DeepSeekVoicePhase _phase = DeepSeekVoicePhase.Idle;
    private long _listenAfterTicks;
    private bool _disposed;

    public DeepSeekVoiceChatService(
        ISharedAudioRecorder recorder,
        IDeepSeekSpeechClient speech,
        DeepSeekChatSession session,
        IAudioPlayer player,
        DeepSeekOptions options,
        ILogger<DeepSeekVoiceChatService>? logger = null)
    {
        _recorder = recorder;
        _speech = speech;
        _session = session;
        _player = player;
        _options = options;
        _logger = logger;
        _detector = new UtteranceDetector(DetectorOptions);
        _micHandler = OnMicData;
    }

    #region 公共接口

    /// <summary>语音合成音色</summary>
    public string VoiceId { get; set; } = DeepSeekTtsVoices.Stella;

    /// <summary>
    /// 语音对话是否使用思考模式。默认关闭：思考会让第一个回答字晚几秒才出现，语音对话更看重响应速度。
    /// </summary>
    public bool UseThinking { get; set; }

    // 延迟统计：从"你说完"到"开始出声"各段耗时
    private long _endOfSpeechTicks;
    private long _detectedTicks;
    private long _asrMs;
    private long _firstAudioTicks;

    /// <summary>开始对话后，多久没有说话就放弃（首句）</summary>
    public TimeSpan NoSpeechTimeout { get; set; } = TimeSpan.FromSeconds(12);

    /// <summary>连续对话中，两轮之间多久没有说话就结束对话</summary>
    public TimeSpan IdleEndTimeout { get; set; } = TimeSpan.FromSeconds(25);

    /// <summary>端点检测参数（在下一次开始对话时生效）</summary>
    public UtteranceDetectorOptions DetectorOptions { get; } = new();

    public DeepSeekVoicePhase Phase => _phase;

    /// <summary>是否有一次语音对话正在进行（包含按住说话）</summary>
    public bool IsActive => _conversationCts != null;

    /// <summary>当前是否为连续对话</summary>
    public bool IsContinuous { get; private set; }

    public event EventHandler<DeepSeekVoicePhase>? PhaseChanged;

    /// <summary>对话开始/结束</summary>
    public event EventHandler<bool>? ActiveChanged;

    /// <summary>识别出用户说的话（一轮对话开始之前）</summary>
    public event EventHandler<string>? UserTranscribed;

    /// <summary>一轮对话开始，参数是用户文本；界面据此创建气泡</summary>
    public event EventHandler<string>? TurnStarted;

    /// <summary>该轮对话的流式事件（思考、回答增量、工具调用、完成、失败）</summary>
    public event EventHandler<DeepSeekChatEvent>? ChatEventReceived;

    /// <summary>一轮对话完全结束（包含语音播放完毕或被打断）</summary>
    public event EventHandler<bool>? TurnEnded;

    public event EventHandler<string>? ErrorOccurred;

    /// <summary>提示信息（如"长时间没有听到声音，已结束语音对话"）</summary>
    public event EventHandler<string>? InfoOccurred;

    /// <summary>
    /// 开始语音对话。continuous 为 true 时回复结束后自动继续听，直到长时间无人说话或手动停止。
    /// 返回 false 表示未能开始（未配置 Key、已在对话中等）。
    /// </summary>
    public Task<bool> StartAsync(bool continuous = true) => StartCoreAsync(continuous, pushToTalk: false);

    /// <summary>按下说话键：开始录音，松开（<see cref="EndPushToTalkAsync"/>）后识别并回复一次</summary>
    public Task<bool> BeginPushToTalkAsync() => StartCoreAsync(continuous: false, pushToTalk: true);

    /// <summary>松开说话键</summary>
    public Task EndPushToTalkAsync()
    {
        _pttReleased?.TrySetResult();
        return Task.CompletedTask;
    }

    /// <summary>结束整个语音对话</summary>
    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        Task? loop;
        lock (_stateLock)
        {
            cts = _conversationCts;
            loop = _loopTask;
        }

        if (cts == null)
        {
            return;
        }

        try { cts.Cancel(); } catch (ObjectDisposedException) { }
        _pttReleased?.TrySetResult();

        if (loop != null)
        {
            try
            {
                await loop.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _logger?.LogWarning("等待 DeepSeek 语音对话退出超时");
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "停止 DeepSeek 语音对话时出现警告");
            }
        }
    }

    /// <summary>
    /// 打断当前这一轮（停止思考/朗读）；连续对话会回到聆听，其他模式结束
    /// </summary>
    public async Task InterruptAsync()
    {
        CancellationTokenSource? turn;
        lock (_stateLock)
        {
            turn = _turnCts;
        }

        if (turn == null)
        {
            return;
        }

        _logger?.LogInformation("用户打断 DeepSeek 当前回复");
        try { turn.Cancel(); } catch (ObjectDisposedException) { }
        _session.CancelCurrent();
        try { await _player.StopAsync().ConfigureAwait(false); } catch (Exception ex) { _logger?.LogDebug(ex, "打断时停止播放出现警告"); }
    }

    #endregion

    #region 对话循环

    private async Task<bool> StartCoreAsync(bool continuous, bool pushToTalk)
    {
        if (_disposed)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(_options.ApiKey))
        {
            ErrorOccurred?.Invoke(this, "请先在 DeepSeek 页面配置 API Key，再使用 DeepSeek 语音对话");
            return false;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_conversationCts != null)
            {
                // 已在对话：正在说话/思考时再次触发视为打断
                if (_phase is DeepSeekVoicePhase.Speaking or DeepSeekVoicePhase.Thinking)
                {
                    await InterruptAsync().ConfigureAwait(false);
                }

                return false;
            }

            try
            {
                if (!_recorder.IsRecording)
                {
                    await _recorder.StartRecordingAsync(16000, 1).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "启动麦克风失败");
                ErrorOccurred?.Invoke(this, $"无法启动麦克风: {ex.Message}");
                return false;
            }

            _detector = new UtteranceDetector(DetectorOptions);
            _mic = NewMicChannel();
            _pttReleased = pushToTalk ? new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously) : null;
            IsContinuous = continuous;

            var cts = new CancellationTokenSource();
            _recorder.SubscribeToAudioData(_micHandler);
            lock (_stateLock)
            {
                _conversationCts = cts;
                _loopTask = Task.Run(() => RunConversationAsync(continuous, pushToTalk, cts));
            }
        }
        finally
        {
            _gate.Release();
        }

        _logger?.LogInformation("DeepSeek 语音对话开始（{Mode}，音色 {Voice}）",
            pushToTalk ? "按住说话" : continuous ? "连续对话" : "单轮对话", VoiceId);
        ActiveChanged?.Invoke(this, true);
        return true;
    }

    private async Task RunConversationAsync(bool continuous, bool pushToTalk, CancellationTokenSource cts)
    {
        var ct = cts.Token;
        try
        {
            var first = true;
            while (!ct.IsCancellationRequested)
            {
                var pcm = pushToTalk
                    ? await CapturePushToTalkAsync(ct).ConfigureAwait(false)
                    : await ListenForUtteranceAsync(first ? NoSpeechTimeout : IdleEndTimeout, ct).ConfigureAwait(false);
                first = false;

                if (pcm == null)
                {
                    if (!ct.IsCancellationRequested)
                    {
                        InfoOccurred?.Invoke(this, pushToTalk ? "录音太短，没有听清" : "长时间没有听到声音，已结束 DeepSeek 语音对话");
                    }

                    break;
                }

                var keepGoing = await ProcessUtteranceAsync(pcm, ct).ConfigureAwait(false);
                if (!continuous || !keepGoing)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常结束
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "DeepSeek 语音对话异常退出");
            ErrorOccurred?.Invoke(this, $"语音对话异常: {ex.Message}");
        }
        finally
        {
            try { _recorder.UnsubscribeFromAudioData(_micHandler); } catch (Exception ex) { _logger?.LogDebug(ex, "取消麦克风订阅出现警告"); }

            lock (_stateLock)
            {
                _conversationCts = null;
                _loopTask = null;
                _turnCts = null;
            }

            _pttReleased = null;
            IsContinuous = false;
            try { await _player.StopAsync().ConfigureAwait(false); } catch (Exception ex) { _logger?.LogDebug(ex, "结束时停止播放出现警告"); }
            SetPhase(DeepSeekVoicePhase.Idle);
            cts.Dispose();
            ActiveChanged?.Invoke(this, false);
            _logger?.LogInformation("DeepSeek 语音对话结束");
        }
    }

    private static Channel<byte[]> NewMicChannel() =>
        Channel.CreateBounded<byte[]>(new BoundedChannelOptions(300)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

    private void OnMicData(object? sender, byte[] data)
    {
        // 思考/朗读期间不收音：扬声器回声会被识别成用户说话
        if (_phase != DeepSeekVoicePhase.Listening || Environment.TickCount64 < Interlocked.Read(ref _listenAfterTicks))
        {
            return;
        }

        _mic.Writer.TryWrite(data.ToArray());
    }

    private void BeginListening()
    {
        while (_mic.Reader.TryRead(out _))
        {
        }

        _detector.Reset();
        SetPhase(DeepSeekVoicePhase.Listening);
    }

    private async Task<byte[]?> ListenForUtteranceAsync(TimeSpan noSpeechTimeout, CancellationToken ct)
    {
        BeginListening();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(noSpeechTimeout);

        try
        {
            await foreach (var chunk in _mic.Reader.ReadAllAsync(timeoutCts.Token).ConfigureAwait(false))
            {
                foreach (var ev in _detector.Feed(chunk))
                {
                    switch (ev.Kind)
                    {
                        case UtteranceEventKind.SpeechStarted:
                            timeoutCts.CancelAfter(Timeout.InfiniteTimeSpan);
                            _logger?.LogDebug("检测到开始说话（阈值 {Threshold:0.000}）", _detector.Threshold);
                            break;

                        case UtteranceEventKind.UtteranceDiscarded:
                            _logger?.LogDebug("忽略过短的声音（有效语音 {Ms}ms）", ev.SpeechMs);
                            timeoutCts.CancelAfter(noSpeechTimeout);
                            break;

                        case UtteranceEventKind.UtteranceCompleted:
                            _detectedTicks = Environment.TickCount64;
                            _endOfSpeechTicks = _detectedTicks - ev.TrailingSilenceMs;
                            _logger?.LogInformation(
                                "一句话结束：有效语音 {Speech}ms，整句 {Total}ms（句内停顿/杂音 {Gap}ms），句尾静音 {Tail}ms，音频 {Bytes} 字节；噪声底 {Noise:0.0000}，有声阈值 {Threshold:0.0000}",
                                ev.SpeechMs, ev.TotalMs, Math.Max(0, ev.TotalMs - ev.SpeechMs - ev.TrailingSilenceMs), ev.TrailingSilenceMs,
                                ev.Pcm!.Length, _detector.NoiseFloor, _detector.Threshold);
                            return ev.Pcm;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }

        return null;
    }

    private async Task<byte[]?> CapturePushToTalkAsync(CancellationToken ct)
    {
        BeginListening();
        var released = _pttReleased!.Task;
        using var buffer = new MemoryStream();

        while (!released.IsCompleted)
        {
            ct.ThrowIfCancellationRequested();

            var readTask = _mic.Reader.WaitToReadAsync(ct).AsTask();
            var winner = await Task.WhenAny(readTask, released).ConfigureAwait(false);
            if (winner == released)
            {
                break;
            }

            if (!await readTask.ConfigureAwait(false))
            {
                break;
            }

            while (_mic.Reader.TryRead(out var chunk))
            {
                buffer.Write(chunk, 0, chunk.Length);
            }
        }

        while (_mic.Reader.TryRead(out var tail))
        {
            buffer.Write(tail, 0, tail.Length);
        }

        // 16 kHz 16 位单声道：不到 0.3 秒当作误触
        const int MinBytes = 16000 * 2 * 3 / 10;
        _detectedTicks = _endOfSpeechTicks = Environment.TickCount64;
        return buffer.Length < MinBytes ? null : buffer.ToArray();
    }

    /// <summary>
    /// 识别并完成一轮对话。返回 false 表示出现了继续对话也没有意义的错误（如 Key 无效）。
    /// </summary>
    private async Task<bool> ProcessUtteranceAsync(byte[] pcm, CancellationToken ct)
    {
        SetPhase(DeepSeekVoicePhase.Transcribing);

        var prewarm = StartTtsPrewarm(ct);

        string text;
        try
        {
            var started = Environment.TickCount64;
            var result = await _speech.TranscribeAsync(pcm, 16000, ct).ConfigureAwait(false);
            text = result.Text?.Trim() ?? string.Empty;
            _asrMs = Environment.TickCount64 - started;
            _logger?.LogInformation("语音识别完成：{Chars} 字，耗时 {Ms}ms", text.Length, _asrMs);
        }
        catch (OperationCanceledException)
        {
            DisposePrewarm(prewarm);
            throw;
        }
        catch (Exception ex)
        {
            DisposePrewarm(prewarm);
            return HandleFailure("语音识别失败", ex);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            DisposePrewarm(prewarm);
            _logger?.LogInformation("没有识别出内容，继续聆听");
            return true;
        }

        UserTranscribed?.Invoke(this, text);
        return await RunTurnAsync(text, prewarm, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 在上传识别的同时提前建立 TTS 连接，等模型的第一句话出来时直接可用，省掉握手耗时。
    /// TTS 按文字计费，没有发送文字的空会话不产生费用。
    /// </summary>
    private Task<IDeepSeekTtsSession>? StartTtsPrewarm(CancellationToken ct)
    {
        try
        {
            return _speech.OpenTtsAsync(VoiceId, ct);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "预建立语音合成连接失败，稍后按需重试");
            return null;
        }
    }

    private void DisposePrewarm(Task<IDeepSeekTtsSession>? prewarm)
    {
        if (prewarm == null)
        {
            return;
        }

        _ = prewarm.ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully)
            {
                _ = t.Result.AbortAsync().ContinueWith(_ => t.Result.DisposeAsync().AsTask(), TaskScheduler.Default);
            }
            else if (t.Exception != null)
            {
                _logger?.LogDebug("未使用的预建立语音合成连接已失败：{Error}", t.Exception.GetBaseException().Message);
            }
        }, TaskScheduler.Default);
    }

    private async Task<bool> RunTurnAsync(string text, Task<IDeepSeekTtsSession>? prewarm, CancellationToken conversationCt)
    {
        var turnCts = CancellationTokenSource.CreateLinkedTokenSource(conversationCt);
        lock (_stateLock)
        {
            _turnCts = turnCts;
        }

        var turnCt = turnCts.Token;
        var chunker = new SpeechTextChunker();
        var playbackDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler onPlaybackStopped = (_, _) => playbackDone.TrySetResult();
        _player.PlaybackStopped += onPlaybackStopped;

        IDeepSeekTtsSession? tts = null;
        Task? pump = null;
        var ttsFailed = false;
        var succeeded = true;
        var continueConversation = true;
        long audioBytes = 0;
        var turnStartTicks = Environment.TickCount64;
        long firstTokenTicks = 0;
        Interlocked.Exchange(ref _firstAudioTicks, 0);
        _session.ThinkingOverride = UseThinking;

        async Task SendToTtsAsync(string chunk)
        {
            if (ttsFailed)
            {
                return;
            }

            try
            {
                if (tts == null)
                {
                    tts = prewarm != null
                        ? await prewarm.WaitAsync(turnCt).ConfigureAwait(false)
                        : await _speech.OpenTtsAsync(VoiceId, turnCt).ConfigureAwait(false);
                    prewarm = null; // 已被使用，不能再被当作"未使用"释放
                    var session = tts;
                    pump = Task.Run(() => PumpAudioAsync(session, turnCt, n => audioBytes = n), CancellationToken.None);
                }

                await tts.SendTextAsync(chunk, turnCt).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                ttsFailed = true;
                HandleFailure("语音合成失败（本轮只显示文字）", ex);
            }
        }

        try
        {
            SetPhase(DeepSeekVoicePhase.Thinking);
            TurnStarted?.Invoke(this, text);

            await foreach (var ev in _session.SendAsync(text, turnCt).ConfigureAwait(false))
            {
                ChatEventReceived?.Invoke(this, ev);

                switch (ev)
                {
                    case DeepSeekChatEvent.ContentDelta delta:
                        if (firstTokenTicks == 0)
                        {
                            firstTokenTicks = Environment.TickCount64;
                        }

                        foreach (var chunk in chunker.Add(delta.Text))
                        {
                            await SendToTtsAsync(chunk).ConfigureAwait(false);
                        }

                        break;

                    case DeepSeekChatEvent.Completed:
                        var rest = chunker.Flush();
                        if (rest != null)
                        {
                            await SendToTtsAsync(rest).ConfigureAwait(false);
                        }

                        break;

                    case DeepSeekChatEvent.Failed failed:
                        succeeded = false;
                        if (!failed.IsCancelled)
                        {
                            continueConversation = HandleFailure("DeepSeek 对话失败", failed.Exception);
                        }

                        break;
                }
            }

            turnCt.ThrowIfCancellationRequested();

            if (tts != null && !ttsFailed)
            {
                await tts.FinishTextAsync(turnCt).ConfigureAwait(false);
                if (pump != null)
                {
                    await pump.WaitAsync(turnCt).ConfigureAwait(false);
                }

                // 音频已全部入队，等播放器把缓冲播完（按音频时长 + 余量兜底，避免卡在 Speaking）
                var audioSeconds = audioBytes / (double)(DeepSeekTts.SampleRate * 2);
                await playbackDone.Task.WaitAsync(TimeSpan.FromSeconds(audioSeconds + 5), turnCt).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!conversationCt.IsCancellationRequested)
        {
            // 仅这一轮被打断：回到聆听
            succeeded = false;
            _logger?.LogInformation("本轮对话已被打断");
        }
        catch (TimeoutException)
        {
            _logger?.LogWarning("等待语音播放结束超时，继续下一轮");
        }
        catch (OperationCanceledException)
        {
            succeeded = false;
            throw;
        }
        finally
        {
            _session.ThinkingOverride = null;
            DisposePrewarm(prewarm); // 整轮都没用到 TTS（出错/被打断/只有工具调用）时释放预建立的连接
            _player.PlaybackStopped -= onPlaybackStopped;
            LogLatency(turnStartTicks, firstTokenTicks);

            if (tts != null)
            {
                try { await tts.AbortAsync().ConfigureAwait(false); } catch (Exception ex) { _logger?.LogDebug(ex, "关闭语音合成会话出现警告"); }
                try { await tts.DisposeAsync().ConfigureAwait(false); } catch (Exception ex) { _logger?.LogDebug(ex, "释放语音合成会话出现警告"); }
            }

            if (turnCt.IsCancellationRequested)
            {
                try { await _player.StopAsync().ConfigureAwait(false); } catch (Exception ex) { _logger?.LogDebug(ex, "停止播放出现警告"); }
            }

            lock (_stateLock)
            {
                if (ReferenceEquals(_turnCts, turnCts))
                {
                    _turnCts = null;
                }
            }

            turnCts.Dispose();
            Interlocked.Exchange(ref _listenAfterTicks, Environment.TickCount64 + (long)PostSpeechGuard.TotalMilliseconds);
            TurnEnded?.Invoke(this, succeeded);
        }

        return continueConversation;
    }

    private async Task PumpAudioAsync(IDeepSeekTtsSession tts, CancellationToken ct, Action<long> reportBytes)
    {
        long total = 0;
        var started = false;

        try
        {
            _player.Reset();

            await foreach (var chunk in tts.ReceiveAudioAsync(ct).ConfigureAwait(false))
            {
                if (!started)
                {
                    started = true;
                    Interlocked.CompareExchange(ref _firstAudioTicks, Environment.TickCount64, 0);
                    SetPhase(DeepSeekVoicePhase.Speaking);
                }

                total += chunk.Length;
                reportBytes(total);
                await _player.PlayAsync(chunk, DeepSeekTts.SampleRate, DeepSeekTts.Channels).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // 被打断
        }
        catch (Exception ex)
        {
            HandleFailure("语音合成失败", ex);
        }
        finally
        {
            if (!ct.IsCancellationRequested)
            {
                _player.CompleteAdding();
            }
        }
    }

    /// <summary>
    /// 输出"从你说完到开始出声"的各段耗时，便于判断慢在哪一段（判停等待 / 识别 / 模型首字 / 语音合成）
    /// </summary>
    private void LogLatency(long turnStartTicks, long firstTokenTicks)
    {
        var firstAudio = Interlocked.Read(ref _firstAudioTicks);
        if (firstTokenTicks == 0 || _endOfSpeechTicks == 0)
        {
            return;
        }

        var endWait = _detectedTicks - _endOfSpeechTicks;
        var toFirstToken = firstTokenTicks - turnStartTicks;
        var toFirstAudio = firstAudio == 0 ? -1 : firstAudio - turnStartTicks;
        var total = firstAudio == 0 ? -1 : firstAudio - _endOfSpeechTicks;

        _logger?.LogInformation(
            "延迟拆分（从你说完算起）：判停等待 {EndWait}ms → 上传+识别 {Asr}ms → 模型首字 {FirstToken}ms → 首段语音 {FirstAudio}ms；说完到开口共 {Total}ms（思考模式：{Thinking}）",
            endWait, _asrMs, toFirstToken, toFirstAudio, total, UseThinking ? "开" : "关");
    }

    /// <summary>
    /// 上报错误；返回 false 表示继续对话没有意义（Key 无效/余额不足）
    /// </summary>
    private bool HandleFailure(string what, Exception ex)
    {
        _logger?.LogWarning(ex, "{What}", what);

        var message = ex switch
        {
            DeepSeekApiException { IsAuthenticationError: true } => $"{what}：API Key 无效，请在 DeepSeek 页面检查",
            DeepSeekApiException { IsInsufficientBalance: true } => $"{what}：账户余额不足",
            DeepSeekApiException { IsRateLimited: true } => $"{what}：请求太频繁，请稍后再试",
            DeepSeekSpeechException speech => $"{what}：{speech.Message}",
            _ => $"{what}：{ex.Message}"
        };

        ErrorOccurred?.Invoke(this, message);
        return ex is not DeepSeekApiException { IsAuthenticationError: true } and not DeepSeekApiException { IsInsufficientBalance: true };
    }

    private void SetPhase(DeepSeekVoicePhase phase)
    {
        if (_phase == phase)
        {
            return;
        }

        _phase = phase;
        _logger?.LogDebug("DeepSeek 语音阶段: {Phase}", phase);
        PhaseChanged?.Invoke(this, phase);
    }

    #endregion

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try { StopAsync().GetAwaiter().GetResult(); } catch (Exception ex) { _logger?.LogDebug(ex, "释放时停止对话出现警告"); }
        _gate.Dispose();
    }
}
