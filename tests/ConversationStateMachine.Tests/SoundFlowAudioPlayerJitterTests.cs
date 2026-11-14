using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Services;
using Xunit;

namespace ConversationStateMachine.Tests;

/// <summary>
/// 用真实的 SoundFlow 播放器验证抖动缓冲：没有音频输出设备的机器上直接跳过
/// </summary>
public class SoundFlowAudioPlayerJitterTests
{
    private const int SampleRate = 16000;
    private const int FrameSamples = 960; // 60ms

    private sealed class CapturingLogger : ILogger<SoundFlowAudioPlayer>
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Enqueue($"{logLevel}: {formatter(state, exception)}");
    }

    private static byte[] Frame(int samples = FrameSamples)
    {
        var data = new byte[samples * 2];
        for (var i = 0; i < samples; i++)
        {
            var v = (short)(Math.Sin(i * 0.05) * 3000);
            data[i * 2] = (byte)(v & 0xFF);
            data[i * 2 + 1] = (byte)(v >> 8);
        }

        return data;
    }

    private static SoundFlowAudioPlayer? TryCreate(out CapturingLogger logger)
    {
        logger = new CapturingLogger();
        try
        {
            return new SoundFlowAudioPlayer(logger);
        }
        catch
        {
            return null;
        }
    }

    [Fact]
    public async Task Playback_Completes_And_Raises_Stopped_Once_With_Jittery_Arrival()
    {
        var player = TryCreate(out var logger);
        if (player == null) return;

        using (player)
        {
            var stopped = new TaskCompletionSource();
            var count = 0;
            player.PlaybackStopped += (_, _) =>
            {
                Interlocked.Increment(ref count);
                stopped.TrySetResult();
            };

            player.Reset();
            var rnd = new Random(7);
            for (var i = 0; i < 25; i++)
            {
                await player.PlayAsync(Frame(), SampleRate, 1);
                await Task.Delay(i % 5 == 4 ? 140 : 30 + rnd.Next(0, 40)); // 突发 + 偶发停顿
            }

            player.CompleteAdding();

            var finished = await Task.WhenAny(stopped.Task, Task.Delay(TimeSpan.FromSeconds(6)));
            Assert.Same(stopped.Task, finished);
            await Task.Delay(300);
            Assert.Equal(1, count);
            Assert.Contains(logger.Messages, m => m.Contains("TTS音频接收完成"));
        }
    }

    [Fact]
    public async Task Utterance_Shorter_Than_Preroll_Still_Plays_And_Completes()
    {
        var player = TryCreate(out _);
        if (player == null) return;

        using (player)
        {
            var stopped = new TaskCompletionSource();
            player.PlaybackStopped += (_, _) => stopped.TrySetResult();

            player.Reset();
            await player.PlayAsync(Frame(), SampleRate, 1); // 60ms，小于预缓冲
            player.CompleteAdding();

            var finished = await Task.WhenAny(stopped.Task, Task.Delay(TimeSpan.FromSeconds(4)));
            Assert.Same(stopped.Task, finished);
        }
    }

    [Fact]
    public async Task Empty_Tts_Raises_Stopped_Immediately()
    {
        var player = TryCreate(out _);
        if (player == null) return;

        using (player)
        {
            var stopped = new TaskCompletionSource();
            player.PlaybackStopped += (_, _) => stopped.TrySetResult();

            player.Reset();
            player.CompleteAdding();

            var finished = await Task.WhenAny(stopped.Task, Task.Delay(TimeSpan.FromSeconds(1)));
            Assert.Same(stopped.Task, finished);
        }
    }

    [Fact]
    public async Task After_Stop_Late_Frames_Are_Dropped_Until_Next_Reset()
    {
        var player = TryCreate(out _);
        if (player == null) return;

        using (player)
        {
            var stoppedCount = 0;
            player.PlaybackStopped += (_, _) => Interlocked.Increment(ref stoppedCount);

            player.Reset();
            for (var i = 0; i < 6; i++)
            {
                await player.PlayAsync(Frame(), SampleRate, 1);
            }

            await player.StopAsync(); // 用户打断
            Assert.False(player.IsPlaying);

            // 服务器还在下发的剩余音频不能让播放器重新开口，也不能产生多余的"播放完成"
            for (var i = 0; i < 6; i++)
            {
                await player.PlayAsync(Frame(), SampleRate, 1);
            }

            player.CompleteAdding();
            await Task.Delay(500);

            Assert.False(player.IsPlaying);
            Assert.Equal(0, stoppedCount);

            // 下一次 TTS 开始后恢复正常
            player.Reset();
            await player.PlayAsync(Frame(), SampleRate, 1);
            Assert.True(player.IsPlaying);
            await player.StopAsync();
        }
    }
}
