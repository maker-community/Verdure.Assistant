using System.Collections.Concurrent;
using System.Net;
using System.Threading.Channels;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Services.DeepSeek;
using Verdure.Assistant.Core.Services.DeepSeek.Speech;
using Xunit;
using static DeepSeek.Tests.TestData;

namespace DeepSeek.Tests;

public class DeepSeekVoiceChatServiceTests
{
    private const int Rate = 16000;

    #region Fakes

    private sealed class FakeRecorder : ISharedAudioRecorder
    {
        private EventHandler<byte[]>? _handler;
        public event EventHandler<byte[]>? DataAvailable;
        public event EventHandler? RecordingStopped;
        public bool IsRecording { get; private set; }
        public int SubscriberCount => _handler == null ? 0 : 1;

        public Task StartRecordingAsync(int sampleRate = 16000, int channels = 1)
        {
            IsRecording = true;
            return Task.CompletedTask;
        }

        public Task StopRecordingAsync()
        {
            IsRecording = false;
            RecordingStopped?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public void SubscribeToAudioData(EventHandler<byte[]> handler) => _handler += handler;
        public void UnsubscribeFromAudioData(EventHandler<byte[]> handler) => _handler -= handler;
        public void ForceCleanup() { }

        public void Push(byte[] pcm)
        {
            _handler?.Invoke(this, pcm);
            DataAvailable?.Invoke(this, pcm);
        }
    }

    private sealed class FakePlayer : IAudioPlayer
    {
        public ConcurrentQueue<(int Rate, int Bytes)> Played { get; } = new();
        public int StopCount;
        public int ResetCount;
        public int CompleteCount;
        public event EventHandler? PlaybackStopped;
        public bool IsPlaying { get; private set; }

        public Task PlayAsync(byte[] audioData, int sampleRate = 16000, int channels = 1)
        {
            IsPlaying = true;
            Played.Enqueue((sampleRate, audioData.Length));
            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            Interlocked.Increment(ref StopCount);
            IsPlaying = false;
            return Task.CompletedTask;
        }

        public void CompleteAdding()
        {
            Interlocked.Increment(ref CompleteCount);
            Task.Run(async () =>
            {
                await Task.Delay(20);
                IsPlaying = false;
                PlaybackStopped?.Invoke(this, EventArgs.Empty);
            });
        }

        public void Reset() => Interlocked.Increment(ref ResetCount);
    }

    private sealed class FakeTts : IDeepSeekTtsSession
    {
        private readonly Channel<byte[]> _audio = Channel.CreateUnbounded<byte[]>();
        private readonly bool _hang;
        private readonly TaskCompletionSource _finishText = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<string> Texts { get; } = new();
        public bool Aborted;
        public bool Finished;
        public bool Disposed;

        public FakeTts(bool hang = false) => _hang = hang;

        public Task SendTextAsync(string text, CancellationToken ct = default)
        {
            Texts.Enqueue(text);
            _audio.Writer.TryWrite(new byte[2400]);
            return Task.CompletedTask;
        }

        public Task FinishTextAsync(CancellationToken ct = default)
        {
            Finished = true;
            _finishText.TrySetResult();
            if (!_hang)
            {
                _audio.Writer.TryComplete();
            }

            return Task.CompletedTask;
        }

        public async IAsyncEnumerable<byte[]> ReceiveAudioAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            while (true)
            {
                byte[] chunk;
                try
                {
                    if (!await _audio.Reader.WaitToReadAsync(ct))
                    {
                        yield break;
                    }

                    if (!_audio.Reader.TryRead(out chunk!))
                    {
                        continue;
                    }
                }
                catch (OperationCanceledException)
                {
                    yield break;
                }

                yield return chunk;
            }
        }

        public Task AbortAsync()
        {
            Aborted = true;
            _audio.Writer.TryComplete();
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeSpeech : IDeepSeekSpeechClient
    {
        public ConcurrentQueue<string> Transcripts { get; } = new();
        public ConcurrentQueue<int> UploadedBytes { get; } = new();
        public List<FakeTts> Sessions { get; } = new();
        public Exception? AsrFailure { get; set; }
        public TimeSpan AsrDelay { get; set; }
        public bool HangTts { get; set; }

        public async Task<DeepSeekAsrResult> TranscribeAsync(ReadOnlyMemory<byte> pcm, int sampleRate = 16000, CancellationToken ct = default)
        {
            UploadedBytes.Enqueue(pcm.Length);
            if (AsrDelay > TimeSpan.Zero)
            {
                await Task.Delay(AsrDelay, ct);
            }

            if (AsrFailure != null)
            {
                throw AsrFailure;
            }

            Transcripts.TryDequeue(out var text);
            return new DeepSeekAsrResult(text ?? string.Empty, 1000, "req");
        }

        public Task<IDeepSeekTtsSession> OpenTtsAsync(string voiceId = DeepSeekTtsVoices.Stella, CancellationToken ct = default)
        {
            var session = new FakeTts(HangTts);
            lock (Sessions)
            {
                Sessions.Add(session);
            }

            return Task.FromResult<IDeepSeekTtsSession>(session);
        }
    }

    #endregion

    private sealed class Harness : IDisposable
    {
        public FakeRecorder Recorder { get; } = new();
        public FakePlayer Player { get; } = new();
        public FakeSpeech Speech { get; } = new();
        public FakeHandler Handler { get; } = new();
        public DeepSeekOptions Options { get; }
        public DeepSeekChatSession Session { get; }
        public DeepSeekVoiceChatService Service { get; }
        public ConcurrentQueue<string> Errors { get; } = new();
        public ConcurrentQueue<string> Infos { get; } = new();
        public ConcurrentQueue<string> Turns { get; } = new();
        public ConcurrentQueue<bool> TurnResults { get; } = new();
        public ConcurrentQueue<DeepSeekVoicePhase> Phases { get; } = new();
        public ConcurrentQueue<bool> ActiveChanges { get; } = new();

        public Harness(string apiKey = "sk-test")
        {
            Options = new DeepSeekOptions { ApiKey = apiKey, RetryBaseDelayMs = 1, MaxRetries = 0 };
            var client = new DeepSeekClient(new HttpClient(Handler), Options, delay: (_, _) => Task.CompletedTask);
            var session = new DeepSeekChatSession(client, Options);
            Session = session;
            Service = new DeepSeekVoiceChatService(Recorder, Speech, session, Player, Options);
            Service.ErrorOccurred += (_, e) => Errors.Enqueue(e);
            Service.InfoOccurred += (_, e) => Infos.Enqueue(e);
            Service.TurnStarted += (_, t) => Turns.Enqueue(t);
            Service.TurnEnded += (_, ok) => TurnResults.Enqueue(ok);
            Service.PhaseChanged += (_, p) => Phases.Enqueue(p);
            Service.ActiveChanged += (_, a) => ActiveChanges.Enqueue(a);
        }

        public async Task SayAsync(double speechSeconds = 1.0, double silenceSeconds = 1.3)
        {
            var speech = Tone(speechSeconds);
            var silence = new byte[(int)(silenceSeconds * Rate) * 2];
            foreach (var frame in Frames(speech).Concat(Frames(silence)))
            {
                Recorder.Push(frame);
                await Task.Delay(1);
            }
        }

        public void Dispose() => Service.Dispose();
    }

    private static byte[] Tone(double seconds)
    {
        var samples = (int)(seconds * Rate);
        var pcm = new byte[samples * 2];
        for (var i = 0; i < samples; i++)
        {
            var envelope = 0.55 + 0.45 * Math.Sin(2 * Math.PI * 4 * i / Rate); // 类语音的音节起伏
            var v = (short)(Math.Sin(2 * Math.PI * 440 * i / Rate) * 0.3 * envelope * short.MaxValue);
            pcm[i * 2] = (byte)(v & 0xFF);
            pcm[i * 2 + 1] = (byte)(v >> 8);
        }

        return pcm;
    }

    private static IEnumerable<byte[]> Frames(byte[] pcm)
    {
        for (var i = 0; i < pcm.Length; i += 640)
        {
            yield return pcm.AsSpan(i, Math.Min(640, pcm.Length - i)).ToArray();
        }
    }

    private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 8000, string? what = null)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException($"等待条件超时：{what}");
            }

            await Task.Delay(10);
        }
    }

    [Fact]
    public async Task Start_Without_ApiKey_Reports_Error_And_Does_Not_Start()
    {
        using var h = new Harness(apiKey: "");

        Assert.False(await h.Service.StartAsync());
        Assert.Contains(h.Errors, e => e.Contains("API Key"));
        Assert.False(h.Service.IsActive);
        Assert.False(h.Recorder.IsRecording);
    }

    [Fact]
    public async Task Continuous_Turn_Transcribes_Streams_Reply_To_Tts_Plays_At_24k_And_Keeps_Listening()
    {
        using var h = new Harness();
        h.Speech.Transcripts.Enqueue("今天天气怎么样");
        h.Handler.EnqueueSse(TextSse("天气不错。适合出门"));

        Assert.True(await h.Service.StartAsync(continuous: true));
        await WaitForAsync(() => h.Service.Phase == DeepSeekVoicePhase.Listening, what: "进入聆听");

        await h.SayAsync();
        await WaitForAsync(() => !h.TurnResults.IsEmpty, what: "一轮结束");

        Assert.Equal("今天天气怎么样", Assert.Single(h.Turns));
        Assert.True(h.TurnResults.TryPeek(out var ok) && ok);
        Assert.InRange(h.Speech.UploadedBytes.Single(), Rate * 2, Rate * 2 * 3);

        var tts = Assert.Single(h.Speech.Sessions);
        Assert.Equal(new[] { "天气不错。", "适合出门" }, tts.Texts.ToArray());
        Assert.True(tts.Finished);
        Assert.NotEmpty(h.Player.Played);
        Assert.All(h.Player.Played, p => Assert.Equal(DeepSeekTts.SampleRate, p.Rate));
        Assert.Equal(1, h.Player.CompleteCount);

        // 连续对话：说完之后回到聆听
        await WaitForAsync(() => h.Service.Phase == DeepSeekVoicePhase.Listening, what: "回到聆听");
        Assert.True(h.Service.IsActive);

        await h.Service.StopAsync();
        Assert.False(h.Service.IsActive);
        Assert.Equal(DeepSeekVoicePhase.Idle, h.Service.Phase);
        Assert.Equal(new[] { true, false }, h.ActiveChanges.ToArray());
        Assert.Equal(0, h.Recorder.SubscriberCount);
    }

    [Fact]
    public async Task Phases_Follow_Listening_Transcribing_Thinking_Speaking_Order()
    {
        using var h = new Harness();
        h.Speech.Transcripts.Enqueue("你好");
        h.Handler.EnqueueSse(TextSse("你好呀。"));

        await h.Service.StartAsync(continuous: false);
        await WaitForAsync(() => h.Service.Phase == DeepSeekVoicePhase.Listening);
        await h.SayAsync();
        await WaitForAsync(() => !h.ActiveChanges.IsEmpty && h.ActiveChanges.Count == 2, what: "单轮对话结束");

        var phases = h.Phases.ToList();
        var order = new[]
        {
            DeepSeekVoicePhase.Listening, DeepSeekVoicePhase.Transcribing,
            DeepSeekVoicePhase.Thinking, DeepSeekVoicePhase.Speaking, DeepSeekVoicePhase.Idle
        };
        Assert.Equal(order, phases.Distinct().ToArray());
        Assert.Equal(DeepSeekVoicePhase.Idle, h.Service.Phase);
    }

    [Fact]
    public async Task Microphone_Frames_Are_Ignored_While_Not_Listening()
    {
        using var h = new Harness();
        h.Speech.Transcripts.Enqueue("第一句");
        h.Speech.HangTts = true; // 保持"朗读中"
        h.Handler.EnqueueSse(TextSse("好的。"));

        await h.Service.StartAsync(continuous: true);
        await WaitForAsync(() => h.Service.Phase == DeepSeekVoicePhase.Listening);
        await h.SayAsync();
        await WaitForAsync(() => h.Service.Phase == DeepSeekVoicePhase.Speaking, what: "进入朗读");

        // 朗读期间喇叭回声不能被当成用户说话
        await h.SayAsync(speechSeconds: 1.0, silenceSeconds: 1.2);
        Assert.Single(h.Speech.UploadedBytes);

        await h.Service.StopAsync();
    }

    [Fact]
    public async Task Interrupt_During_Speaking_Aborts_Tts_Stops_Player_And_Returns_To_Listening()
    {
        using var h = new Harness();
        h.Speech.Transcripts.Enqueue("讲个长故事");
        h.Speech.HangTts = true;
        h.Handler.EnqueueSse(TextSse("从前有座山。山里有座庙"));

        await h.Service.StartAsync(continuous: true);
        await WaitForAsync(() => h.Service.Phase == DeepSeekVoicePhase.Listening);
        await h.SayAsync();
        await WaitForAsync(() => h.Service.Phase == DeepSeekVoicePhase.Speaking, what: "进入朗读");

        await h.Service.InterruptAsync();

        await WaitForAsync(() => !h.TurnResults.IsEmpty, what: "本轮被打断");
        Assert.False(h.TurnResults.First());
        Assert.True(h.Speech.Sessions.Single().Aborted);
        Assert.True(h.Player.StopCount >= 1);
        await WaitForAsync(() => h.Service.Phase == DeepSeekVoicePhase.Listening, what: "打断后回到聆听");
        Assert.True(h.Service.IsActive);

        await h.Service.StopAsync();
    }

    [Fact]
    public async Task Empty_Transcript_Does_Not_Start_A_Turn_And_Keeps_Listening()
    {
        using var h = new Harness();
        h.Speech.Transcripts.Enqueue("   ");

        await h.Service.StartAsync(continuous: true);
        await WaitForAsync(() => h.Service.Phase == DeepSeekVoicePhase.Listening);
        await h.SayAsync();
        await WaitForAsync(() => !h.Speech.UploadedBytes.IsEmpty, what: "已上传识别");
        await WaitForAsync(() => h.Service.Phase == DeepSeekVoicePhase.Listening, what: "继续聆听");

        Assert.Empty(h.Turns);
        Assert.Empty(h.Handler.Requests);
        Assert.True(h.Service.IsActive);

        await h.Service.StopAsync();
    }

    [Fact]
    public async Task Invalid_ApiKey_During_Transcription_Ends_Conversation_With_Clear_Error()
    {
        using var h = new Harness();
        h.Speech.AsrFailure = new DeepSeekApiException("invalid key", HttpStatusCode.Unauthorized);

        await h.Service.StartAsync(continuous: true);
        await WaitForAsync(() => h.Service.Phase == DeepSeekVoicePhase.Listening);
        await h.SayAsync();
        await WaitForAsync(() => h.ActiveChanges.Count == 2, what: "对话结束");

        Assert.Contains(h.Errors, e => e.Contains("API Key 无效"));
        Assert.False(h.Service.IsActive);
    }

    [Fact]
    public async Task Transient_Asr_Failure_Reports_Error_But_Keeps_Listening()
    {
        using var h = new Harness();
        h.Speech.AsrFailure = new HttpRequestException("网络断开");

        await h.Service.StartAsync(continuous: true);
        await WaitForAsync(() => h.Service.Phase == DeepSeekVoicePhase.Listening);
        await h.SayAsync();
        await WaitForAsync(() => !h.Errors.IsEmpty, what: "上报错误");
        await WaitForAsync(() => h.Service.Phase == DeepSeekVoicePhase.Listening, what: "继续聆听");

        Assert.True(h.Service.IsActive);
        await h.Service.StopAsync();
    }

    [Fact]
    public async Task No_Speech_Within_Timeout_Ends_Conversation_With_Info()
    {
        using var h = new Harness();
        h.Service.NoSpeechTimeout = TimeSpan.FromMilliseconds(300);

        await h.Service.StartAsync(continuous: true);
        await WaitForAsync(() => h.ActiveChanges.Count == 2, what: "超时结束");

        Assert.Contains(h.Infos, i => i.Contains("长时间没有听到声音"));
        Assert.False(h.Service.IsActive);
    }

    [Fact]
    public async Task PushToTalk_Records_While_Held_Then_Runs_Single_Turn()
    {
        using var h = new Harness();
        h.Speech.Transcripts.Enqueue("按住说的话");
        h.Handler.EnqueueSse(TextSse("收到。"));

        Assert.True(await h.Service.BeginPushToTalkAsync());
        await WaitForAsync(() => h.Service.Phase == DeepSeekVoicePhase.Listening);

        foreach (var frame in Frames(Tone(1.0)))
        {
            h.Recorder.Push(frame);
        }

        await Task.Delay(100);
        await h.Service.EndPushToTalkAsync();
        await WaitForAsync(() => h.ActiveChanges.Count == 2, what: "单轮结束");

        Assert.Equal("按住说的话", Assert.Single(h.Turns));
        Assert.InRange(h.Speech.UploadedBytes.Single(), Rate * 2 * 9 / 10, Rate * 2 * 11 / 10);
        Assert.False(h.Service.IsActive);
    }

    [Fact]
    public async Task PushToTalk_Too_Short_Is_Treated_As_Accidental_Touch()
    {
        using var h = new Harness();

        await h.Service.BeginPushToTalkAsync();
        await WaitForAsync(() => h.Service.Phase == DeepSeekVoicePhase.Listening);
        h.Recorder.Push(new byte[640]);
        await h.Service.EndPushToTalkAsync();
        await WaitForAsync(() => h.ActiveChanges.Count == 2);

        Assert.Empty(h.Speech.UploadedBytes);
        Assert.Contains(h.Infos, i => i.Contains("录音太短"));
    }

    [Fact]
    public async Task Voice_Turns_Disable_Thinking_By_Default_And_Reset_It_Afterwards()
    {
        using var h = new Harness();
        h.Speech.Transcripts.Enqueue("你好");
        h.Handler.EnqueueSse(TextSse("你好呀。"));

        await h.Service.StartAsync(continuous: false);
        await WaitForAsync(() => h.Service.Phase == DeepSeekVoicePhase.Listening);
        await h.SayAsync();
        await WaitForAsync(() => h.ActiveChanges.Count == 2, what: "单轮结束");

        Assert.Equal("disabled", (string?)h.Handler.Requests[0].Json["thinking"]!["type"]);
        Assert.Null(h.Session.ThinkingOverride);
    }

    [Fact]
    public async Task Voice_Turns_Can_Enable_Thinking()
    {
        using var h = new Harness();
        h.Service.UseThinking = true;
        h.Speech.Transcripts.Enqueue("你好");
        h.Handler.EnqueueSse(TextSse("你好呀。"));

        await h.Service.StartAsync(continuous: false);
        await WaitForAsync(() => h.Service.Phase == DeepSeekVoicePhase.Listening);
        await h.SayAsync();
        await WaitForAsync(() => h.ActiveChanges.Count == 2, what: "单轮结束");

        Assert.Equal("enabled", (string?)h.Handler.Requests[0].Json["thinking"]!["type"]);
    }

    [Fact]
    public async Task Tts_Connection_Is_Opened_While_Recognition_Is_Still_Running()
    {
        using var h = new Harness();
        h.Speech.AsrDelay = TimeSpan.FromMilliseconds(600);
        h.Speech.Transcripts.Enqueue("你好");
        h.Handler.EnqueueSse(TextSse("你好呀。"));

        await h.Service.StartAsync(continuous: false);
        await WaitForAsync(() => h.Service.Phase == DeepSeekVoicePhase.Listening);
        await h.SayAsync();
        await WaitForAsync(() => h.Service.Phase == DeepSeekVoicePhase.Transcribing, what: "进入识别");

        // 识别还没返回，TTS 连接就应该已经建好（省掉握手耗时）
        await WaitForAsync(() => h.Speech.Sessions.Count == 1, 400, "识别期间预建立 TTS 连接");
        Assert.Equal(DeepSeekVoicePhase.Transcribing, h.Service.Phase);

        await WaitForAsync(() => h.ActiveChanges.Count == 2, what: "单轮结束");
        Assert.Single(h.Speech.Sessions); // 预建立的连接被实际使用，没有再开第二个
        Assert.NotEmpty(h.Speech.Sessions[0].Texts);
    }

    [Fact]
    public async Task Unused_Prewarmed_Tts_Connection_Is_Released_When_Nothing_Was_Recognized()
    {
        using var h = new Harness();
        h.Speech.Transcripts.Enqueue("  ");

        await h.Service.StartAsync(continuous: true);
        await WaitForAsync(() => h.Service.Phase == DeepSeekVoicePhase.Listening);
        await h.SayAsync();
        await WaitForAsync(() => h.Speech.Sessions.Count == 1, what: "预建立 TTS 连接");

        await WaitForAsync(() => h.Speech.Sessions[0].Disposed, 3000, "未使用的预建立连接被释放");
        await h.Service.StopAsync();
    }

    [Fact]
    public async Task Starting_Twice_Is_Rejected_And_Does_Not_Double_Subscribe()
    {
        using var h = new Harness();

        Assert.True(await h.Service.StartAsync());
        Assert.False(await h.Service.StartAsync());
        Assert.Equal(1, h.Recorder.SubscriberCount);

        await h.Service.StopAsync();
    }
}
