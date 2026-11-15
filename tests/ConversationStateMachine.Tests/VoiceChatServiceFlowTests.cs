using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Verdure.Assistant.Core.Constants;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Models;
using Verdure.Assistant.Core.Services;
using Verdure.Assistant.Core.Services.MCP;
using Xunit;

namespace ConversationStateMachine.Tests;

/// <summary>
/// VoiceChatService 端到端流程测试：真实的 WebSocketClient + 状态机 + 本地假小智服务器，
/// 覆盖 唤醒→连接→聆听→说话→空闲、连接失败/断线恢复、唤醒词与对话的协调 等此前容易出错的路径。
/// </summary>
public class VoiceChatServiceFlowTests
{
    #region 测试替身

    private sealed class FakeConfig : IConfigurationService
    {
        public FakeConfig(string url) => WebSocketUrl = url;

        public string ClientId => "test-client";
        public string DeviceId => "00:11:22:33:44:55";
        public string UserAgent => "test";
        public string CurrentVersion => "1.0.0";
        public MqttConfiguration? MqttInfo => null;
        public OtaResponse? LatestOtaResponse => null;
        public DateTime? LastOtaCheckTime => null;
        public string OtaVersionUrl => "http://localhost/ota";
        public string WebSocketUrl { get; }
        public string AccessToken => "test-token";
        public Task ReloadSettingsAsync() => Task.CompletedTask;
        public bool IsActivated => true;
        public string? ActivationCode => null;
        public string? ActivationMessage => null;

        public event EventHandler<string>? VerificationCodeReceived { add { } remove { } }
        public event EventHandler<OtaResponse?>? OtaCheckCompleted { add { } remove { } }

        public Task<bool> InitializeMqttInfoAsync() => Task.FromResult(true);
        public Task<OtaResponse?> CheckOtaUpdateAsync() => Task.FromResult<OtaResponse?>(null);
        public Task<DeviceInfo> GetDeviceInfoAsync() => Task.FromResult(new DeviceInfo());
        public Task<NetworkInfo?> GetNetworkInfoAsync() => Task.FromResult<NetworkInfo?>(null);
        public Task<string?> ExtractVerificationCodeAsync(string responseText) => Task.FromResult<string?>(null);
        public Task CopyToClipboardAsync(string text) => Task.CompletedTask;
        public Task OpenBrowserAsync(string url) => Task.CompletedTask;
    }

    private sealed class FakeKeyword : IKeywordSpottingService
    {
        private readonly object _gate = new();
        public List<string> Calls { get; } = new();
        public bool Running { get; private set; }
        public bool Paused { get; private set; }

        public event EventHandler<KeywordDetectedEventArgs>? KeywordDetected;
        public event EventHandler<string>? ErrorOccurred { add { } remove { } }

        public bool IsRunning => Running && !Paused;
        public bool IsPaused => Paused;
        public bool IsEnabled => true;

        public Task<bool> StartAsync(IAudioRecorder? audioRecorder = null)
        {
            lock (_gate) { Calls.Add("start"); Running = true; Paused = false; }
            return Task.FromResult(true);
        }

        public Task StopAsync()
        {
            lock (_gate) { Calls.Add("stop"); Running = false; Paused = false; }
            return Task.CompletedTask;
        }

        public void Pause() { lock (_gate) { Calls.Add("pause"); Paused = true; } }
        public void Resume() { lock (_gate) { Calls.Add("resume"); Paused = false; } }
        public bool UpdateAudioSource(IAudioRecorder audioRecorder) => true;
        public void SetConfig(VerdureConfig config) { }
        public Task<bool> SwitchKeywordModelAsync(string modelFileName) => Task.FromResult(true);
        public void Dispose() { }

        public void Raise(string keyword = "小点")
            => KeywordDetected?.Invoke(this, new KeywordDetectedEventArgs { Keyword = keyword, FullText = keyword });

        public string[] Snapshot() { lock (_gate) { return Calls.ToArray(); } }
    }

    private sealed class FakePlayer : IAudioPlayer
    {
        public event EventHandler? PlaybackStopped;
        public bool IsPlaying { get; private set; }
        public int StopCount;
        public Task PlayAsync(byte[] audioData, int sampleRate = 16000, int channels = 1) { IsPlaying = true; return Task.CompletedTask; }
        public Task StopAsync() { Interlocked.Increment(ref StopCount); IsPlaying = false; return Task.CompletedTask; }
        public void CompleteAdding() { }
        public void Reset() { }
        public void FinishPlayback() { IsPlaying = false; PlaybackStopped?.Invoke(this, EventArgs.Empty); }
    }

    private sealed class FakeRecorder : ISharedAudioRecorder, IDisposable
    {
        private readonly List<EventHandler<byte[]>> _handlers = new();
        private readonly object _gate = new();
        private readonly CancellationTokenSource _cts = new();

        public event EventHandler<byte[]>? DataAvailable;
        public event EventHandler? RecordingStopped;
        public bool IsRecording { get; private set; }

        public Task StartRecordingAsync(int sampleRate = 16000, int channels = 1)
        {
            if (IsRecording) return Task.CompletedTask;
            IsRecording = true;
            _ = Task.Run(async () =>
            {
                var tone = new byte[640]; // 20ms @16k mono s16
                for (var i = 0; i < tone.Length; i += 2)
                {
                    var s = (short)(Math.Sin(i / 2 * 0.2) * 3000);
                    tone[i] = (byte)(s & 0xFF);
                    tone[i + 1] = (byte)(s >> 8);
                }
                while (!_cts.IsCancellationRequested)
                {
                    DataAvailable?.Invoke(this, (byte[])tone.Clone());
                    EventHandler<byte[]>[] snapshot;
                    lock (_gate) { snapshot = _handlers.ToArray(); }
                    foreach (var h in snapshot) h(this, tone);
                    try { await Task.Delay(20, _cts.Token); } catch (OperationCanceledException) { }
                }
            });
            return Task.CompletedTask;
        }

        public Task StopRecordingAsync()
        {
            IsRecording = false;
            RecordingStopped?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }

        public void SubscribeToAudioData(EventHandler<byte[]> handler) { lock (_gate) { _handlers.Add(handler); } }
        public void UnsubscribeFromAudioData(EventHandler<byte[]> handler) { lock (_gate) { _handlers.Remove(handler); } }
        public void ForceCleanup() { }
        public void Dispose() => _cts.Cancel();
    }

    /// <summary>
    /// 极简小智协议服务器：收到 hello 回 hello，记录所有文本/二进制消息，可主动下发 TTS 消息或断开连接
    /// </summary>
    private sealed class FakeServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly ConcurrentQueue<WebSocket> _sockets = new();
        private readonly CancellationTokenSource _cts = new();

        public string Url { get; }
        public ConcurrentQueue<string> Texts { get; } = new();
        public int BinaryFrames;
        public bool ReplyToHello { get; set; } = true;
        public int ConnectionCount;

        public FakeServer(int? port = null)
        {
            var p = port ?? GetFreePort();
            Url = $"ws://localhost:{p}/";
            _listener.Prefixes.Add($"http://localhost:{p}/");
            _listener.Start();
            _ = Task.Run(AcceptLoopAsync);
        }

        public static int GetFreePort()
        {
            var l = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            l.Start();
            var port = ((IPEndPoint)l.LocalEndpoint).Port;
            l.Stop();
            return port;
        }

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                try
                {
                    var ctx = await _listener.GetContextAsync();
                    if (!ctx.Request.IsWebSocketRequest)
                    {
                        ctx.Response.StatusCode = 400;
                        ctx.Response.Close();
                        continue;
                    }
                    var ws = (await ctx.AcceptWebSocketAsync(null)).WebSocket;
                    Interlocked.Increment(ref ConnectionCount);
                    _sockets.Enqueue(ws);
                    _ = Task.Run(() => ServeAsync(ws));
                }
                catch when (_cts.IsCancellationRequested) { return; }
                catch { /* 监听器被关闭 */ return; }
            }
        }

        private async Task ServeAsync(WebSocket ws)
        {
            var buffer = new byte[16 * 1024];
            try
            {
                while (ws.State == WebSocketState.Open)
                {
                    var result = await ws.ReceiveAsync(buffer, _cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close) break;
                    if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        Interlocked.Increment(ref BinaryFrames);
                        continue;
                    }

                    var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    Texts.Enqueue(text);
                    using var doc = JsonDocument.Parse(text);
                    if (ReplyToHello && doc.RootElement.TryGetProperty("type", out var type) && type.GetString() == "hello")
                    {
                        await SendTextAsync(ws,
                            "{\"type\":\"hello\",\"transport\":\"websocket\",\"session_id\":\"sess-1\",\"audio_params\":{\"format\":\"opus\",\"sample_rate\":16000,\"channels\":1,\"frame_duration\":60}}");
                    }
                }
            }
            catch { /* 连接被中断 */ }
        }

        private static Task SendTextAsync(WebSocket ws, string json)
            => ws.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None);

        public async Task PushAsync(string json)
        {
            foreach (var ws in _sockets.Where(s => s.State == WebSocketState.Open))
            {
                await SendTextAsync(ws, json);
            }
        }

        public void DropAll()
        {
            foreach (var ws in _sockets) { try { ws.Abort(); } catch { } }
        }

        public bool Received(string fragment) => Texts.Any(t => t.Contains(fragment, StringComparison.Ordinal));

        public bool HasListen(string state) => Texts.Any(t =>
        {
            using var doc = JsonDocument.Parse(t);
            var root = doc.RootElement;
            return root.TryGetProperty("type", out var type) && type.GetString() == "listen"
                && root.TryGetProperty("state", out var s) && s.GetString() == state;
        });

        public void Dispose()
        {
            _cts.Cancel();
            DropAll();
            try { _listener.Close(); } catch { }
        }
    }

    private sealed class Harness : IDisposable
    {
        public FakeServer Server { get; }
        public FakeKeyword Keyword { get; } = new();
        public FakePlayer Player { get; } = new();
        public FakeRecorder Recorder { get; } = new();
        public VoiceChatService Service { get; }
        public List<DeviceState> States { get; } = new();
        public List<string> Errors { get; } = new();
        private readonly object _gate = new();

        public Harness(bool enableVoice = false, string? overrideUrl = null)
        {
            Server = new FakeServer();
            Service = new VoiceChatService(
                new FakeConfig(overrideUrl ?? Server.Url), Keyword, Recorder, Player,
                new McpServer(NullLogger<McpServer>.Instance),
                musicPlayerService: null,
                logger: NullLogger<VoiceChatService>.Instance,
                loggerFactory: NullLoggerFactory.Instance)
            {
                ConnectTimeout = TimeSpan.FromSeconds(4)
            };
            Service.StateMachine!.StateChanged += (_, e) => { lock (_gate) States.Add(e.ToState); };
            Service.ErrorOccurred += (_, e) => { lock (_gate) Errors.Add(e); };
            EnableVoice = enableVoice;
        }

        public bool EnableVoice { get; }

        public VerdureConfig Config => new()
        {
            EnableVoice = EnableVoice,
            AudioSampleRate = 16000,
            AudioChannels = 1,
            AudioFormat = "opus"
        };

        public DeviceState[] StateSnapshot() { lock (_gate) { return States.ToArray(); } }

        public void Dispose()
        {
            Service.Dispose();
            Recorder.Dispose();
            Server.Dispose();
        }
    }

    private static async Task<bool> WaitFor(Func<bool> condition, int timeoutMs = 8000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            await Task.Delay(20);
        }
        return condition();
    }

    #endregion

    [Fact]
    public async Task WakeWord_Connects_WaitsForHello_ThenListens()
    {
        using var h = new Harness();
        await h.Service.InitializeAsync(h.Config);

        h.Keyword.Raise();

        Assert.True(await WaitFor(() => h.Service.CurrentState == DeviceState.Listening), "应进入 Listening");
        Assert.Equal(new[] { DeviceState.Connecting, DeviceState.Listening }, h.StateSnapshot().Take(2).ToArray());
        Assert.True(h.Service.IsConnected);
        Assert.True(await WaitFor(() => h.Server.HasListen("start")), "握手完成后应发送 listen start");

        // 协议顺序：hello 一定在 listen 之前
        var texts = h.Server.Texts.ToArray();
        var helloIndex = Array.FindIndex(texts, t => t.Contains("hello"));
        var listenIndex = Array.FindIndex(texts, t => t.Contains("listen"));
        Assert.True(helloIndex >= 0 && listenIndex > helloIndex);
    }

    [Fact]
    public async Task KeywordRecognizer_IsPausedWhileConversing_AndResumedWhenIdle()
    {
        using var h = new Harness();
        await h.Service.InitializeAsync(h.Config);
        Assert.True(h.Keyword.IsRunning);

        h.Keyword.Raise();
        Assert.True(await WaitFor(() => h.Service.CurrentState == DeviceState.Listening));
        Assert.True(h.Keyword.IsPaused, "聆听期间唤醒词识别应暂停，避免抢占音频流");

        await h.Service.StopVoiceChatAsync();
        Assert.True(await WaitFor(() => h.Service.CurrentState == DeviceState.Idle));
        Assert.True(await WaitFor(() => h.Keyword.IsRunning), "回到空闲后应恢复唤醒词识别");
        Assert.DoesNotContain("stop", h.Keyword.Snapshot().Skip(1)); // 不再用 Stop+Start 反复重建
    }

    [Fact]
    public async Task WakeWord_WhileListening_IsIgnored_NoReconnect()
    {
        using var h = new Harness();
        await h.Service.InitializeAsync(h.Config);
        h.Keyword.Raise();
        Assert.True(await WaitFor(() => h.Service.CurrentState == DeviceState.Listening));
        var transitions = h.StateSnapshot().Length;
        var connections = h.Server.ConnectionCount;

        await Task.Delay(1400); // 超过去抖窗口
        h.Keyword.Raise();
        await Task.Delay(300);

        Assert.Equal(DeviceState.Listening, h.Service.CurrentState);
        Assert.Equal(transitions, h.StateSnapshot().Length);
        Assert.Equal(connections, h.Server.ConnectionCount);
    }

    [Fact]
    public async Task Tts_MovesToSpeaking_AndReturnsToListening_WhenKeepListening()
    {
        using var h = new Harness();
        await h.Service.InitializeAsync(h.Config);
        h.Keyword.Raise(); // 唤醒会启用 KeepListening
        Assert.True(await WaitFor(() => h.Service.CurrentState == DeviceState.Listening));

        await h.Server.PushAsync("{\"type\":\"tts\",\"state\":\"start\"}");
        Assert.True(await WaitFor(() => h.Service.CurrentState == DeviceState.Speaking));

        await h.Server.PushAsync("{\"type\":\"tts\",\"state\":\"stop\"}");
        h.Player.FinishPlayback();

        Assert.True(await WaitFor(() => h.Service.CurrentState == DeviceState.Listening));
    }

    [Fact]
    public async Task PushToTalk_StartsViaConnecting_AndReplyReturnsToIdle()
    {
        using var h = new Harness();
        await h.Service.InitializeAsync(h.Config);

        await h.Service.StartVoiceChatAsync();   // 手动按住说话：KeepListening=false
        Assert.True(await WaitFor(() => h.Service.CurrentState == DeviceState.Listening));
        Assert.True(h.Service.IsConnected, "不能在没有连接的情况下停在 Listening");
        Assert.True(await WaitFor(() => h.Server.HasListen("start")));

        await h.Service.StopVoiceChatAsync();
        Assert.True(await WaitFor(() => h.Service.CurrentState == DeviceState.Idle));
        Assert.True(await WaitFor(() => h.Server.HasListen("stop")));

        await h.Server.PushAsync("{\"type\":\"tts\",\"state\":\"start\"}");
        Assert.True(await WaitFor(() => h.Service.CurrentState == DeviceState.Speaking));
        await h.Server.PushAsync("{\"type\":\"tts\",\"state\":\"stop\"}");
        h.Player.FinishPlayback();
        Assert.True(await WaitFor(() => h.Service.CurrentState == DeviceState.Idle));
    }

    [Fact]
    public async Task ConnectFailure_ReturnsToIdle_InsteadOfSticking()
    {
        var deadPort = FakeServer.GetFreePort();
        using var h = new Harness(overrideUrl: $"ws://localhost:{deadPort}/");
        await h.Service.InitializeAsync(h.Config);

        h.Keyword.Raise();

        Assert.True(await WaitFor(() => h.Service.CurrentState == DeviceState.Idle && h.StateSnapshot().Length >= 2, 12000),
            "连接失败后必须回到 Idle，而不是一直停在 Connecting");
        Assert.Contains(DeviceState.Connecting, h.StateSnapshot());
        Assert.NotEmpty(h.Errors);
        Assert.True(await WaitFor(() => h.Keyword.IsRunning), "失败后应恢复唤醒词识别，用户可再次唤醒");
    }

    [Fact]
    public async Task ServerDisconnect_WhileListening_GoesIdle_AndCanReconnectOnNextWake()
    {
        using var h = new Harness();
        await h.Service.InitializeAsync(h.Config);
        h.Keyword.Raise();
        Assert.True(await WaitFor(() => h.Service.CurrentState == DeviceState.Listening));

        h.Server.DropAll();
        Assert.True(await WaitFor(() => h.Service.CurrentState == DeviceState.Idle), "断线后应回到 Idle");
        Assert.False(h.Service.IsConnected);

        await Task.Delay(1400); // 过唤醒去抖窗口
        h.Keyword.Raise();
        Assert.True(await WaitFor(() => h.Service.CurrentState == DeviceState.Listening), "再次唤醒应能重新连接");
        Assert.True(h.Server.ConnectionCount >= 2);
    }

    [Fact]
    public async Task Disconnect_WhileListening_StopsListeningBeforeDisconnecting()
    {
        using var h = new Harness(enableVoice: true);
        await h.Service.InitializeAsync(h.Config);
        h.Keyword.Raise();
        Assert.True(await WaitFor(() => h.Service.CurrentState == DeviceState.Listening));
        Assert.True(await WaitFor(() => h.Server.HasListen("start")));

        await h.Service.DisconnectAsync();

        Assert.True(await WaitFor(() => h.Server.HasListen("stop")), "断开连接前应发送停止聆听消息");
        Assert.Equal(DeviceState.Idle, h.Service.CurrentState);
        Assert.False(h.Recorder.IsRecording);
        Assert.False(h.Keyword.Running);
    }

    [Fact]
    public async Task Disconnect_ThenInitialize_Again_Works()
    {
        using var h = new Harness();
        await h.Service.InitializeAsync(h.Config);
        h.Keyword.Raise();
        Assert.True(await WaitFor(() => h.Service.CurrentState == DeviceState.Listening));

        await h.Service.DisconnectAsync();
        Assert.Equal(DeviceState.Idle, h.Service.CurrentState);
        Assert.False(h.Service.IsConnected);
        Assert.False(h.Keyword.Running, "断开后不应继续监听麦克风");

        await h.Service.InitializeAsync(h.Config);
        Assert.True(await WaitFor(() => h.Keyword.IsRunning));

        await Task.Delay(1400);
        h.Keyword.Raise();
        Assert.True(await WaitFor(() => h.Service.CurrentState == DeviceState.Listening));
    }

    [Fact]
    public async Task InitializeAsync_IsIdempotent()
    {
        using var h = new Harness();
        await h.Service.InitializeAsync(h.Config);
        await h.Service.InitializeAsync(h.Config);
        await h.Service.InitializeAsync(h.Config);

        Assert.Equal(1, h.Keyword.Snapshot().Count(c => c == "start"));
    }

    [Fact]
    public async Task Microphone_Frames_AreSentOnlyWhileListening()
    {
        using var h = new Harness(enableVoice: true);
        await h.Service.InitializeAsync(h.Config);

        await Task.Delay(300);
        Assert.Equal(0, h.Server.BinaryFrames); // Idle：不上传

        h.Keyword.Raise();
        Assert.True(await WaitFor(() => h.Service.CurrentState == DeviceState.Listening));
        Assert.True(await WaitFor(() => h.Server.BinaryFrames > 5), "聆听时应持续上传 Opus 帧");

        await h.Service.StopVoiceChatAsync();
        Assert.True(await WaitFor(() => h.Service.CurrentState == DeviceState.Idle));
        await Task.Delay(200);
        var afterStop = h.Server.BinaryFrames;
        await Task.Delay(300);
        Assert.Equal(afterStop, h.Server.BinaryFrames);
    }

    [Fact]
    public async Task Dispose_IsIdempotent_AndStopsEverything()
    {
        var h = new Harness();
        await h.Service.InitializeAsync(h.Config);
        h.Keyword.Raise();
        Assert.True(await WaitFor(() => h.Service.CurrentState == DeviceState.Listening));

        h.Service.Dispose();
        h.Service.Dispose();
        h.Dispose();
    }
}
