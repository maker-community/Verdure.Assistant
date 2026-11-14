using Verdure.Assistant.Core.Services.DeepSeek.Speech;
using Xunit;

namespace DeepSeek.Tests;

public class UtteranceDetectorTests
{
    private const int Rate = 16000;

    // 类语音信号：音节速率（约 4 Hz）的幅度起伏，波谷接近静音。完全平稳的纯音和稳定噪声没有区别，会被正确地学成噪声
    private static byte[] Tone(double seconds, double amplitude, double freq = 440)
    {
        var samples = (int)(seconds * Rate);
        var pcm = new byte[samples * 2];
        for (var i = 0; i < samples; i++)
        {
            var envelope = 0.55 + 0.45 * Math.Sin(2 * Math.PI * 4 * i / Rate);
            var v = (short)(Math.Sin(2 * Math.PI * freq * i / Rate) * amplitude * envelope * short.MaxValue);
            pcm[i * 2] = (byte)(v & 0xFF);
            pcm[i * 2 + 1] = (byte)(v >> 8);
        }

        return pcm;
    }

    private static byte[] Silence(double seconds) => new byte[(int)(seconds * Rate) * 2];

    private static List<UtteranceEvent> FeedAll(UtteranceDetector detector, byte[] pcm, int chunkBytes)
    {
        var events = new List<UtteranceEvent>();
        for (var i = 0; i < pcm.Length; i += chunkBytes)
        {
            events.AddRange(detector.Feed(pcm.AsSpan(i, Math.Min(chunkBytes, pcm.Length - i))));
        }

        return events;
    }

    private static byte[] Concat(params byte[][] parts) => parts.SelectMany(p => p).ToArray();

    [Fact]
    public void Speech_Between_Silence_Produces_Started_Then_Completed()
    {
        var detector = new UtteranceDetector();
        var events = FeedAll(detector, Concat(Silence(0.5), Tone(1.0, 0.3), Silence(1.5)), 640);

        Assert.Equal(UtteranceEventKind.SpeechStarted, events[0].Kind);
        var done = Assert.Single(events, e => e.Kind == UtteranceEventKind.UtteranceCompleted);
        Assert.NotNull(done.Pcm);
        Assert.InRange(done.SpeechMs, 800, 1200);
        // 前导 + 语音 + 少量尾部静音，明显短于整段输入
        Assert.InRange(done.Pcm!.Length / 2.0 / Rate, 1.0, 1.9);
        Assert.False(detector.InSpeech);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(333)]
    [InlineData(641)]
    [InlineData(4096)]
    public void Result_Does_Not_Depend_On_Chunk_Size(int chunkBytes)
    {
        var detector = new UtteranceDetector();
        var events = FeedAll(detector, Concat(Silence(0.3), Tone(0.8, 0.3), Silence(1.2)), chunkBytes);

        Assert.Single(events, e => e.Kind == UtteranceEventKind.UtteranceCompleted);
    }

    [Fact]
    public void Short_Burst_Is_Discarded()
    {
        var detector = new UtteranceDetector();
        var events = FeedAll(detector, Concat(Silence(0.3), Tone(0.16, 0.4), Silence(1.5)), 640);

        Assert.DoesNotContain(events, e => e.Kind == UtteranceEventKind.UtteranceCompleted);
    }

    [Fact]
    public void Background_Noise_Below_Threshold_Never_Triggers()
    {
        var detector = new UtteranceDetector();
        var events = FeedAll(detector, Tone(5, 0.004, 120), 640);

        Assert.Empty(events);
    }

    [Fact]
    public void Constant_Loud_Speech_Is_Cut_At_Max_Length()
    {
        var detector = new UtteranceDetector(new UtteranceDetectorOptions { MaxUtteranceMs = 2000 });
        var events = FeedAll(detector, Tone(3.0, 0.3), 640);

        var done = Assert.Single(events, e => e.Kind == UtteranceEventKind.UtteranceCompleted);
        Assert.InRange(done.Pcm!.Length / 2.0 / Rate, 1.8, 2.1);
    }

    [Fact]
    public void Quiet_Speaker_Is_Still_Detected_After_Noise_Floor_Settles()
    {
        var detector = new UtteranceDetector();
        // 先一段环境底噪，再来一句音量不大的话
        var events = FeedAll(detector, Concat(Tone(1.0, 0.003, 90), Tone(0.9, 0.05), Silence(1.3)), 640);

        Assert.Single(events, e => e.Kind == UtteranceEventKind.UtteranceCompleted);
    }

    [Fact]
    public void Two_Utterances_Back_To_Back_Are_Both_Reported()
    {
        var detector = new UtteranceDetector();
        var events = FeedAll(detector, Concat(Tone(0.8, 0.3), Silence(1.2), Tone(0.8, 0.3, 300), Silence(1.2)), 640);

        Assert.Equal(2, events.Count(e => e.Kind == UtteranceEventKind.UtteranceCompleted));
    }

    [Fact]
    public void Reset_Drops_Partial_Utterance()
    {
        var detector = new UtteranceDetector();
        FeedAll(detector, Concat(Silence(0.6), Tone(0.5, 0.3)), 640);
        Assert.True(detector.InSpeech);

        detector.Reset();
        Assert.False(detector.InSpeech);
        Assert.Empty(FeedAll(detector, Silence(2), 640));
    }

    private static byte[] NoiseWithSpeech(double noiseAmplitude, double seconds, double speechAmplitude = 0, int seed = 3)
    {
        var rnd = new Random(seed);
        var samples = (int)(seconds * Rate);
        var pcm = new byte[samples * 2];
        for (var i = 0; i < samples; i++)
        {
            var v = (rnd.NextDouble() * 2 - 1) * noiseAmplitude;
            if (speechAmplitude > 0)
            {
                v += Math.Sin(2 * Math.PI * 300 * i / Rate) * speechAmplitude * (0.6 + 0.4 * Math.Sin(2 * Math.PI * 4 * i / Rate));
            }

            var s = (short)(Math.Clamp(v, -1, 1) * short.MaxValue);
            pcm[i * 2] = (byte)(s & 0xFF);
            pcm[i * 2 + 1] = (byte)(s >> 8);
        }

        return pcm;
    }

    [Fact]
    public void Steady_Noise_Louder_Than_Min_Threshold_Is_Learned_And_Speech_Still_Ends_Promptly()
    {
        var detector = new UtteranceDetector();

        // 稳定的嘈杂环境（约 -27 dBFS，远高于最小阈值），先让检测器适应
        var warmup = FeedAll(detector, NoiseWithSpeech(0.07, 8), 640);
        // 之后再说一句话，然后回到嘈杂背景
        var events = FeedAll(detector, Concat(NoiseWithSpeech(0.07, 0.5, seed: 4), NoiseWithSpeech(0.07, 1.2, speechAmplitude: 0.7, seed: 5), NoiseWithSpeech(0.07, 2.0, seed: 6)), 640);

        // 适应之后，纯噪声不应再触发
        Assert.Empty(FeedAll(detector, NoiseWithSpeech(0.07, 4, seed: 7), 640));
        var done = Assert.Single(events, e => e.Kind == UtteranceEventKind.UtteranceCompleted);
        // 句子总长应当接近 "说话 1.2s + 前后少量静音"，而不是被噪声一直拖到上限
        Assert.InRange(done.TotalMs, 1000, 3500);
        Assert.True(detector.NoiseFloor > 0.02, $"noise floor {detector.NoiseFloor}");
        Assert.True(warmup.Count(e => e.Kind == UtteranceEventKind.UtteranceCompleted) <= 1);
    }

    [Fact]
    public void Completed_Event_Reports_Total_And_Trailing_Silence()
    {
        var detector = new UtteranceDetector(new UtteranceDetectorOptions { EndSilenceMs = 600 });
        var events = FeedAll(detector, Concat(Silence(0.3), Tone(1.0, 0.3), Silence(1.5)), 640);

        var done = Assert.Single(events, e => e.Kind == UtteranceEventKind.UtteranceCompleted);
        Assert.InRange(done.TrailingSilenceMs, 580, 660);
        Assert.True(done.TotalMs > done.SpeechMs);
    }

    [Fact]
    public void Shorter_End_Silence_Finishes_Sooner()
    {
        int FramesUntilDone(int endSilenceMs)
        {
            var detector = new UtteranceDetector(new UtteranceDetectorOptions { EndSilenceMs = endSilenceMs });
            FeedAll(detector, Tone(1.0, 0.3), 640);
            var fed = 0;
            foreach (var chunk in Frames(Silence(2.0)))
            {
                fed++;
                if (detector.Feed(chunk).Any(e => e.Kind == UtteranceEventKind.UtteranceCompleted))
                {
                    break;
                }
            }

            return fed;
        }

        Assert.True(FramesUntilDone(500) < FramesUntilDone(1000));
    }

    private static IEnumerable<byte[]> Frames(byte[] pcm)
    {
        for (var i = 0; i < pcm.Length; i += 640)
        {
            yield return pcm.AsSpan(i, Math.Min(640, pcm.Length - i)).ToArray();
        }
    }
}

public class SpeechTextChunkerTests
{
    private static List<string> Run(string[] deltas, int min = 4, int max = 80)
    {
        var chunker = new SpeechTextChunker(min, max);
        var result = new List<string>();
        foreach (var delta in deltas)
        {
            result.AddRange(chunker.Add(delta));
        }

        var rest = chunker.Flush();
        if (rest != null)
        {
            result.Add(rest);
        }

        return result;
    }

    [Fact]
    public void Splits_On_Chinese_Sentence_Ends_Across_Deltas()
    {
        var chunks = Run(new[] { "你好", "呀。今天", "天气不错", "！出去", "走走" });

        Assert.Equal(new[] { "你好呀。", "今天天气不错！", "出去走走" }, chunks);
    }

    [Fact]
    public void Decimal_Point_Is_Not_A_Sentence_End()
    {
        // 小数点不当作句号；第一个短句在逗号处先送出以便更快出声
        var chunks = Run(new[] { "现在气温3.5度，", "湿度不高。" });

        Assert.Equal(new[] { "现在气温3.5度，", "湿度不高。" }, chunks);
    }

    [Fact]
    public void First_Chunk_Is_Sent_At_A_Comma_But_Later_Chunks_Wait_For_Sentence_Ends()
    {
        var chunker = new SpeechTextChunker();

        var first = chunker.Add("我是绿荫助手，一个运行在");
        Assert.Equal(new[] { "我是绿荫助手，" }, first);

        // 之后的逗号不再切分，保持整句语调
        Assert.Empty(chunker.Add("语音设备上的助手，可以帮你"));
        Assert.Equal(new[] { "一个运行在语音设备上的助手，可以帮你聊天。" }, chunker.Add("聊天。"));
    }

    [Fact]
    public void Very_Short_First_Clause_Does_Not_Trigger_Early_Send()
    {
        var chunker = new SpeechTextChunker();

        Assert.Empty(chunker.Add("好，"));
        Assert.Equal(new[] { "好，我马上去查一下。" }, chunker.Add("我马上去查一下。"));
    }

    [Fact]
    public void Very_Long_Text_Without_Punctuation_Is_Split_At_Soft_Breaks()
    {
        var text = string.Join("，", Enumerable.Repeat("这是一段很长的没有句号的话", 12));
        var chunks = Run(new[] { text }, min: 4, max: 40);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => Assert.True(c.Length <= 41, c));
        Assert.Equal(text, string.Concat(chunks));
    }

    [Fact]
    public void Blank_Input_Produces_Nothing()
    {
        Assert.Empty(Run(new[] { "", "  ", "\n" }));
    }

    [Fact]
    public void Short_Sentence_Waits_For_Min_Length_And_Is_Flushed_At_End()
    {
        var chunker = new SpeechTextChunker(minChunkChars: 4);

        Assert.Empty(chunker.Add("好。"));
        Assert.Equal("好。", chunker.Flush());
    }
}
