using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Verdure.Assistant.Core.Services.DeepSeek;

/// <summary>
/// 基于 HttpClient / ClientWebSocket 的 DeepSeek 语音客户端。
/// ApiKey / BaseUrl 每次调用时从共享的 <see cref="DeepSeekOptions"/> 读取，运行时修改会生效。
/// </summary>
public sealed class DeepSeekSpeechClient : IDeepSeekSpeechClient
{
    private const int MaxWavBytes = 8 * 1024 * 1024;
    private static readonly int[] SupportedSampleRates = { 8000, 16000, 22050, 24000, 32000, 44100, 48000 };

    private readonly HttpClient _http;
    private readonly DeepSeekOptions _options;
    private readonly ILogger<DeepSeekSpeechClient>? _logger;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    /// <param name="http">HttpClient；建议 Timeout 设为 Timeout.InfiniteTimeSpan，由 <see cref="DeepSeekOptions.TimeoutSeconds"/> 控制超时</param>
    /// <param name="options">配置（保存引用，运行时修改会生效）</param>
    /// <param name="logger">可选日志，不会记录 API Key 或语音/文本内容</param>
    /// <param name="delay">重试等待函数，默认 Task.Delay；主要用于测试</param>
    public DeepSeekSpeechClient(
        HttpClient http,
        DeepSeekOptions options,
        ILogger<DeepSeekSpeechClient>? logger = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
        _delay = delay ?? ((t, ct) => Task.Delay(t, ct));
    }

    public async Task<DeepSeekAsrResult> TranscribeAsync(
        ReadOnlyMemory<byte> pcm, int sampleRate = 16000, CancellationToken ct = default)
    {
        // 丢弃末尾不完整的采样字节
        pcm = pcm[..(pcm.Length & ~1)];
        if (pcm.IsEmpty)
        {
            throw new ArgumentException("音频数据不能为空", nameof(pcm));
        }

        if (!SupportedSampleRates.Contains(sampleRate))
        {
            throw new ArgumentException($"不支持的采样率: {sampleRate}", nameof(sampleRate));
        }

        if (pcm.Length + 44 > MaxWavBytes)
        {
            throw new ArgumentException("音频数据超过 8 MiB 上限", nameof(pcm));
        }

        var wav = WavEncoder.FromPcm16(pcm.Span, sampleRate);

        return await WithRetryAsync(async token =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BuildUri("/v1/tools/asr", null));
            var apiKey = GetApiKey();
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var file = new ByteArrayContent(wav);
            file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
            request.Content = new MultipartFormDataContent { { file, "file", "audio.wav" } };

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token)
                .ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw CreateException(response, body);
            }

            return ParseAsrResult(body);
        }, ct).ConfigureAwait(false);
    }

    public async Task<IDeepSeekTtsSession> OpenTtsAsync(
        string voiceId = DeepSeekTtsVoices.Stella, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(voiceId))
        {
            voiceId = DeepSeekTtsVoices.Stella;
        }

        return await WithRetryAsync<IDeepSeekTtsSession>(async token =>
        {
            var apiKey = GetApiKey();
            var uri = BuildUri("/v1/tools/tts",
                $"voice_id={Uri.EscapeDataString(voiceId)}&format=pcm&filter_markdown=true", webSocket: true);

            var ws = new ClientWebSocket();
            try
            {
                ws.Options.CollectHttpResponseDetails = true;
                ws.Options.SetRequestHeader("Authorization", "Bearer " + apiKey);
                await ws.ConnectAsync(uri, token).ConfigureAwait(false);
                return new DeepSeekTtsSession(ws, _logger);
            }
            catch (WebSocketException ex)
            {
                var status = ws.HttpStatusCode;
                ws.Dispose();
                throw CreateConnectException(status, ex);
            }
            catch
            {
                ws.Dispose();
                throw;
            }
        }, ct).ConfigureAwait(false);
    }

    private string GetApiKey()
    {
        var apiKey = _options.ApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("DeepSeek ApiKey 未配置");
        }

        return apiKey;
    }

    private Uri BuildUri(string path, string? query, bool webSocket = false)
    {
        var baseUri = new Uri(_options.BaseUrl.TrimEnd('/'));
        var scheme = webSocket
            ? (baseUri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws")
            : baseUri.Scheme;

        var builder = new UriBuilder(baseUri)
        {
            Scheme = scheme,
            Port = baseUri.IsDefaultPort ? -1 : baseUri.Port,
            Path = baseUri.AbsolutePath.TrimEnd('/') + path,
            Query = query ?? string.Empty
        };
        return builder.Uri;
    }

    private static DeepSeekAsrResult ParseAsrResult(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("响应不是 JSON 对象");
            }

            var text = root.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String
                ? t.GetString() ?? string.Empty
                : string.Empty;
            var duration = root.TryGetProperty("duration_ms", out var d) && d.TryGetInt32(out var ms) ? ms : 0;
            var requestId = root.TryGetProperty("request_id", out var r) && r.ValueKind == JsonValueKind.String
                ? r.GetString()
                : null;
            return new DeepSeekAsrResult(text, duration, requestId);
        }
        catch (JsonException ex)
        {
            throw new DeepSeekApiException($"DeepSeek 语音识别响应无法解析: {ex.Message}",
                HttpStatusCode.OK, responseBody: Truncate(body), innerException: ex);
        }
    }

    private static DeepSeekApiException CreateConnectException(HttpStatusCode? status, Exception inner)
    {
        if (status.HasValue)
        {
            return new DeepSeekApiException(
                $"DeepSeek 语音合成连接被拒绝 {(int)status.Value} ({DeepSeekApiException.Describe(status.Value)})",
                status, isRetryable: DeepSeekApiException.IsRetryableStatus(status.Value), innerException: inner);
        }

        return new DeepSeekApiException($"DeepSeek 语音合成网络错误: {inner.Message}", isRetryable: true, innerException: inner);
    }

    private static DeepSeekApiException CreateException(HttpResponseMessage response, string body)
    {
        string? message = null, type = null, code = null, param = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                if (root.TryGetProperty("error", out var error))
                {
                    if (error.ValueKind == JsonValueKind.Object)
                    {
                        message = GetString(error, "message");
                        type = GetString(error, "type");
                        code = GetString(error, "code");
                        param = GetString(error, "param");
                    }
                    else if (error.ValueKind == JsonValueKind.String)
                    {
                        message = error.GetString();
                    }
                }
                else
                {
                    message = GetString(root, "message");
                }
            }
        }
        catch (JsonException)
        {
            // 非 JSON 响应体：使用状态码描述
        }

        var status = response.StatusCode;
        var text = $"DeepSeek 语音识别错误 {(int)status} ({DeepSeekApiException.Describe(status)})" +
                   (string.IsNullOrWhiteSpace(message) ? string.Empty : $": {message}");

        return new DeepSeekApiException(
            text, status, type, code, param,
            DeepSeekApiException.IsRetryableStatus(status), ParseRetryAfter(response), Truncate(body));
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => value.ToString()
        };
    }

    private static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header == null)
        {
            return null;
        }

        if (header.Delta.HasValue)
        {
            return header.Delta.Value < TimeSpan.Zero ? TimeSpan.Zero : header.Delta.Value;
        }

        if (header.Date.HasValue)
        {
            var delta = header.Date.Value - DateTimeOffset.UtcNow;
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }

        return null;
    }

    private static string Truncate(string text) => text.Length <= 4096 ? text : text[..4096];

    /// <summary>仅重试 429、503 与网络错误/单次超时；其他错误立即抛出</summary>
    private static bool ShouldRetry(DeepSeekApiException failure) =>
        failure.StatusCode is null or HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;

    private async Task<T> WithRetryAsync<T>(Func<CancellationToken, Task<T>> attemptAsync, CancellationToken ct)
    {
        var timeout = TimeSpan.FromSeconds(Math.Max(1, _options.TimeoutSeconds));

        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            DeepSeekApiException failure;
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attemptCts.CancelAfter(timeout);

            try
            {
                return await attemptAsync(attemptCts.Token).ConfigureAwait(false);
            }
            catch (DeepSeekApiException ex)
            {
                failure = ex;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException ex)
            {
                failure = new DeepSeekApiException($"DeepSeek 语音请求超时（{timeout.TotalSeconds:0}s）", isRetryable: true, innerException: ex);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                failure = new DeepSeekApiException($"DeepSeek 网络错误: {ex.Message}", isRetryable: true, innerException: ex);
            }

            if (!ShouldRetry(failure) || attempt >= _options.MaxRetries)
            {
                throw failure;
            }

            var wait = ComputeDelay(attempt, failure.RetryAfter);
            _logger?.LogWarning("DeepSeek 语音请求失败 (状态 {Status})，{Delay}ms 后进行第 {Attempt}/{Max} 次重试",
                failure.StatusCode?.ToString() ?? "network", (int)wait.TotalMilliseconds, attempt + 1, _options.MaxRetries);
            await _delay(wait, ct).ConfigureAwait(false);
        }
    }

    private TimeSpan ComputeDelay(int attempt, TimeSpan? retryAfter)
    {
        var max = TimeSpan.FromSeconds(Math.Max(0, _options.RetryMaxDelaySeconds));
        if (retryAfter.HasValue)
        {
            return retryAfter.Value > max ? max : retryAfter.Value;
        }

        var exponential = _options.RetryBaseDelayMs * Math.Pow(2, Math.Min(attempt, 20));
        var jittered = exponential * (0.5 + Random.Shared.NextDouble() * 0.5);
        var delay = TimeSpan.FromMilliseconds(jittered);
        return delay > max ? max : delay;
    }
}

/// <summary>把 16 位 PCM 封装成标准 44 字节头的 WAV</summary>
public static class WavEncoder
{
    public const int HeaderSize = 44;

    public static byte[] FromPcm16(ReadOnlySpan<byte> pcm, int sampleRate, int channels = 1)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        if (channels <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(channels));
        }

        var wav = new byte[HeaderSize + pcm.Length];
        var span = wav.AsSpan();
        var blockAlign = (short)(channels * 2);

        "RIFF"u8.CopyTo(span);
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], 36 + pcm.Length);
        "WAVE"u8.CopyTo(span[8..]);
        "fmt "u8.CopyTo(span[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(span[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(span[22..], (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], sampleRate * blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(span[32..], blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(span[34..], 16);
        "data"u8.CopyTo(span[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(span[40..], pcm.Length);
        pcm.CopyTo(span[HeaderSize..]);
        return wav;
    }
}
