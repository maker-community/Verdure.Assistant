using Microsoft.Extensions.Logging.Abstractions;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Models;
using Verdure.Assistant.Core.Services;
using Xunit;

namespace ConversationStateMachine.Tests;

/// <summary>
/// 唤醒词服务的生命周期/并发回归测试。使用真实的 Speech SDK 识别器 + 静音音频，
/// 验证 Start/Pause/Resume/Stop/Dispose 不会死锁、不会误报唤醒。
/// </summary>
public class KeywordSpottingServiceTests
{
    private static string? FindModelsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Verdure.Assistant.WinUI", "Assets", "keywords");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        return null;
    }

    private static VerdureConfig NewConfig(string modelsDir) => new()
    {
        KeywordModels = new KeywordModelConfig { ModelsPath = modelsDir, CurrentModel = "keyword_xiaodian.table" }
    };

    [Fact]
    public async Task Start_ReturnsFalse_WhenModelIsMissing()
    {
        using var recorder = new SilenceRecorder();
        using var service = new KeywordSpottingService(recorder, NullLogger<KeywordSpottingService>.Instance);
        service.SetConfig(new VerdureConfig
        {
            KeywordModels = new KeywordModelConfig { ModelsPath = Path.Combine(Path.GetTempPath(), "no-such-dir-" + Guid.NewGuid()) }
        });

        Assert.False(await service.StartAsync());
        Assert.False(service.IsRunning);
    }

    [Fact]
    public async Task PauseResumeStop_NeverBlock_AndSilenceNeverTriggersWakeWord()
    {
        var modelsDir = FindModelsDirectory();
        if (modelsDir == null)
        {
            return; // 没有模型文件的环境（例如精简的 CI 检出）跳过
        }

        using var recorder = new SilenceRecorder();
        using var service = new KeywordSpottingService(recorder, NullLogger<KeywordSpottingService>.Instance);
        service.SetConfig(NewConfig(modelsDir));

        var detected = 0;
        var errors = new System.Collections.Concurrent.ConcurrentQueue<string>();
        service.KeywordDetected += (_, _) => Interlocked.Increment(ref detected);
        service.ErrorOccurred += (_, error) => errors.Enqueue(error);

        Assert.True(await service.StartAsync());
        Assert.True(await service.StartAsync(), "重复启动应当是幂等的");
        Assert.True(service.IsRunning);

        await Task.Delay(1200);
        Assert.True(recorder.SubscriberCount > 0, "识别器应加载模型并订阅音频");
        Assert.Empty(errors);

        // 反复暂停/恢复：旧实现里 Pause 会等到下一次唤醒词才返回
        for (var i = 0; i < 5; i++)
        {
            service.Pause();
            Assert.True(service.IsPaused);
            Assert.False(service.IsRunning);
            await Task.Delay(150);

            service.Resume();
            Assert.False(service.IsPaused);
            await Task.Delay(150);
        }

        Assert.True(service.IsRunning);

        var stopTask = service.StopAsync();
        var finished = await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(stopTask, finished);
        await stopTask;

        Assert.False(service.IsRunning);
        Assert.Equal(0, detected);
        Assert.Equal(0, recorder.SubscriberCount);
        Assert.Empty(errors);
    }

    [Fact]
    public async Task StopWhilePaused_ReturnsPromptly()
    {
        var modelsDir = FindModelsDirectory();
        if (modelsDir == null)
        {
            return;
        }

        using var recorder = new SilenceRecorder();
        using var service = new KeywordSpottingService(recorder, NullLogger<KeywordSpottingService>.Instance);
        service.SetConfig(NewConfig(modelsDir));

        Assert.True(await service.StartAsync());
        await Task.Delay(500);
        service.Pause();
        await Task.Delay(300);

        var stopTask = service.StopAsync();
        Assert.Same(stopTask, await Task.WhenAny(stopTask, Task.Delay(TimeSpan.FromSeconds(10))));
    }

    [Fact]
    public async Task SwitchModel_WhileRunning_KeepsServiceAlive()
    {
        var modelsDir = FindModelsDirectory();
        if (modelsDir == null)
        {
            return;
        }

        using var recorder = new SilenceRecorder();
        using var service = new KeywordSpottingService(recorder, NullLogger<KeywordSpottingService>.Instance);
        service.SetConfig(NewConfig(modelsDir));

        Assert.True(await service.StartAsync());
        await Task.Delay(500);

        Assert.True(await service.SwitchKeywordModelAsync("keyword_cortana.table"));
        Assert.False(await service.SwitchKeywordModelAsync("does-not-exist.table"));
        await Task.Delay(1000);

        Assert.True(service.IsRunning);
        await service.StopAsync();
    }

    /// <summary>
    /// 以 100ms 为周期持续输出静音 PCM（16kHz/16bit/mono）的假录音机
    /// </summary>
    private sealed class SilenceRecorder : ISharedAudioRecorder, IDisposable
    {
        private readonly List<EventHandler<byte[]>> _handlers = new();
        private readonly object _gate = new();
        private readonly CancellationTokenSource _cts = new();
        private Task? _pump;

        public event EventHandler<byte[]>? DataAvailable;
        public event EventHandler? RecordingStopped;

        public bool IsRecording { get; private set; }

        public int SubscriberCount
        {
            get { lock (_gate) { return _handlers.Count; } }
        }

        public Task StartRecordingAsync(int sampleRate = 16000, int channels = 1)
        {
            if (IsRecording)
            {
                return Task.CompletedTask;
            }

            IsRecording = true;
            _pump = Task.Run(async () =>
            {
                var frame = new byte[3200];
                while (!_cts.IsCancellationRequested)
                {
                    EventHandler<byte[]>[] snapshot;
                    lock (_gate) { snapshot = _handlers.ToArray(); }
                    foreach (var handler in snapshot)
                    {
                        handler(this, frame);
                    }
                    DataAvailable?.Invoke(this, frame);

                    try { await Task.Delay(100, _cts.Token); } catch (OperationCanceledException) { }
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

        public void SubscribeToAudioData(EventHandler<byte[]> handler)
        {
            lock (_gate) { _handlers.Add(handler); }
        }

        public void UnsubscribeFromAudioData(EventHandler<byte[]> handler)
        {
            lock (_gate) { _handlers.Remove(handler); }
        }

        public void ForceCleanup() { }

        public void Dispose()
        {
            _cts.Cancel();
            try { _pump?.Wait(1000); } catch { }
        }
    }
}
