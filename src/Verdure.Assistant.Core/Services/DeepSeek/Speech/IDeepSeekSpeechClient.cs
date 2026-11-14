namespace Verdure.Assistant.Core.Services.DeepSeek;

/// <summary>
/// DeepSeek 语音服务客户端：语音识别（ASR，非流式）与流式语音合成（TTS，WebSocket）
/// </summary>
public interface IDeepSeekSpeechClient
{
    /// <summary>
    /// 把 16 位小端单声道 PCM 封装为 WAV 并上传识别。
    /// API 错误抛出 <see cref="DeepSeekApiException"/>；空输入或超过 8 MiB 的 WAV 抛出 <see cref="ArgumentException"/>；
    /// 取消抛出 <see cref="OperationCanceledException"/>。429/503 与瞬时网络错误按 <see cref="DeepSeekOptions"/> 的重试配置自动重试。
    /// </summary>
    Task<DeepSeekAsrResult> TranscribeAsync(ReadOnlyMemory<byte> pcm, int sampleRate = 16000, CancellationToken ct = default);

    /// <summary>
    /// 打开一个流式 TTS WebSocket 会话；连接被拒绝（如 401 API Key 无效）时抛出 <see cref="DeepSeekApiException"/>。
    /// </summary>
    Task<IDeepSeekTtsSession> OpenTtsAsync(string voiceId = DeepSeekTtsVoices.Stella, CancellationToken ct = default);
}

/// <summary>语音识别结果</summary>
public sealed record DeepSeekAsrResult(string Text, int DurationMs, string? RequestId);

/// <summary>TTS 可用音色</summary>
public static class DeepSeekTtsVoices
{
    public const string Stella = "stella";
    public const string Tide = "tide";
}

/// <summary>TTS 输出的 PCM 格式：24 kHz、16 位、单声道</summary>
public static class DeepSeekTts
{
    public const int SampleRate = 24000;
    public const int Channels = 1;
}

/// <summary>
/// 流式 TTS 会话：持续发送文本，同时接收 PCM 音频。
/// </summary>
public interface IDeepSeekTtsSession : IAsyncDisposable
{
    /// <summary>发送一段文本（忽略空白；超过 16 KiB 会自动拆分）。可与 <see cref="ReceiveAudioAsync"/> 并发调用。</summary>
    Task SendTextAsync(string text, CancellationToken ct = default);

    /// <summary>告知服务端不会再有文本。</summary>
    Task FinishTextAsync(CancellationToken ct = default);

    /// <summary>
    /// 产出 24 kHz 16 位单声道 PCM 块（每块字节数为偶数，不会拆分采样点），直到服务端发送 finish。
    /// finish.code 不是 success 时在产出全部音频之后抛出 <see cref="DeepSeekSpeechException"/>；
    /// 连接在没有 finish 的情况下关闭时抛出 Code 为 connection_closed 的异常（客户端主动 Abort 除外）。只能消费一次。
    /// </summary>
    IAsyncEnumerable<byte[]> ReceiveAudioAsync(CancellationToken ct = default);

    /// <summary>立即终止会话（用户打断）。幂等、不抛异常，并让正在等待的 <see cref="ReceiveAudioAsync"/> 安静结束。</summary>
    Task AbortAsync();
}

/// <summary>TTS 服务端通过 finish 消息报告的失败，或连接异常关闭</summary>
public sealed class DeepSeekSpeechException : Exception
{
    public DeepSeekSpeechException(string code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    /// <summary>invalid_input / content_filter / input_timeout / service_error / connection_closed 等</summary>
    public string Code { get; }
}
