using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Verdure.Assistant.Core.Services.DeepSeek;

/// <summary>基于 ClientWebSocket 的流式 TTS 会话</summary>
internal sealed class DeepSeekTtsSession : IDeepSeekTtsSession
{
    private const int MaxTextChunkChars = 4096;
    private const int ReceiveBufferSize = 16 * 1024;

    private readonly ClientWebSocket _ws;
    private readonly ILogger? _logger;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _abortCts = new();
    private int _receiveStarted;
    private int _aborted;
    private int _disposed;

    public DeepSeekTtsSession(ClientWebSocket ws, ILogger? logger = null)
    {
        _ws = ws ?? throw new ArgumentNullException(nameof(ws));
        _logger = logger;
    }

    private bool IsAborted => Volatile.Read(ref _aborted) != 0;

    public async Task SendTextAsync(string text, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var start = 0;
        while (start < text.Length)
        {
            var length = Math.Min(MaxTextChunkChars, text.Length - start);
            // 避免把代理对拆到两个帧里
            if (start + length < text.Length && char.IsHighSurrogate(text[start + length - 1]))
            {
                length--;
            }

            var chunk = text.Substring(start, length);
            start += length;
            if (string.IsNullOrWhiteSpace(chunk))
            {
                continue;
            }

            await SendJsonAsync(JsonSerializer.Serialize(new { @event = "text", text = chunk }), ct).ConfigureAwait(false);
        }
    }

    public Task FinishTextAsync(CancellationToken ct = default) =>
        SendJsonAsync("{\"event\":\"finish_text\"}", ct);

    private async Task SendJsonAsync(string json, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (IsAborted)
        {
            return;
        }

        var bytes = Encoding.UTF8.GetBytes(json);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _abortCts.Token);
        var acquired = false;
        try
        {
            await _sendLock.WaitAsync(linked.Token).ConfigureAwait(false);
            acquired = true;
            if (IsAborted)
            {
                return;
            }

            await _ws.SendAsync(bytes, WebSocketMessageType.Text, true, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (IsAborted && !ct.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is WebSocketException or InvalidOperationException or ObjectDisposedException)
        {
            if (IsAborted)
            {
                return;
            }

            throw new DeepSeekSpeechException("connection_closed", $"DeepSeek 语音合成连接已断开: {ex.Message}", ex);
        }
        finally
        {
            if (acquired)
            {
                _sendLock.Release();
            }
        }
    }

    public IAsyncEnumerable<byte[]> ReceiveAudioAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _receiveStarted, 1) != 0)
        {
            throw new InvalidOperationException("ReceiveAudioAsync 只能消费一次");
        }

        return ReceiveCoreAsync(ct);
    }

    private async IAsyncEnumerable<byte[]> ReceiveCoreAsync([EnumeratorCancellation] CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _abortCts.Token);
        var buffer = new byte[ReceiveBufferSize];
        var textMessage = new MemoryStream();
        byte? carry = null;
        string? finishCode;
        string? finishMessage;

        while (true)
        {
            ValueWebSocketReceiveResult result = default;
            Exception? failure = null;
            var aborted = false;

            try
            {
                result = await _ws.ReceiveAsync(buffer.AsMemory(), linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (IsAborted && !ct.IsCancellationRequested)
            {
                aborted = true;
            }
            catch (Exception ex) when (ex is WebSocketException or ObjectDisposedException or InvalidOperationException)
            {
                if (IsAborted)
                {
                    aborted = true;
                }
                else
                {
                    failure = ex;
                }
            }

            if (aborted)
            {
                yield break;
            }

            if (failure != null)
            {
                throw new DeepSeekSpeechException("connection_closed", $"DeepSeek 语音合成连接异常中断: {failure.Message}", failure);
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                if (IsAborted)
                {
                    yield break;
                }

                var description = _ws.CloseStatusDescription;
                throw new DeepSeekSpeechException(
                    "connection_closed",
                    string.IsNullOrWhiteSpace(description)
                        ? "DeepSeek 语音合成连接在完成前被关闭"
                        : $"DeepSeek 语音合成连接在完成前被关闭: {description}");
            }

            if (result.MessageType == WebSocketMessageType.Binary)
            {
                var output = Realign(buffer.AsSpan(0, result.Count), ref carry);
                if (output != null)
                {
                    yield return output;
                }

                continue;
            }

            textMessage.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage)
            {
                continue;
            }

            var json = Encoding.UTF8.GetString(textMessage.GetBuffer(), 0, (int)textMessage.Length);
            textMessage.SetLength(0);
            if (TryParseFinish(json, out finishCode, out finishMessage))
            {
                break;
            }
        }

        if (carry.HasValue)
        {
            _logger?.LogWarning("DeepSeek TTS 结束时丢弃了 1 个不完整的采样字节");
        }

        if (!string.Equals(finishCode, "success", StringComparison.OrdinalIgnoreCase))
        {
            var message = string.IsNullOrWhiteSpace(finishMessage)
                ? $"DeepSeek 语音合成失败: {finishCode}"
                : $"DeepSeek 语音合成失败 ({finishCode}): {finishMessage}";
            throw new DeepSeekSpeechException(finishCode ?? "service_error", message);
        }
    }

    /// <summary>把上一帧遗留的半个采样点并入当前帧，并保证输出长度为偶数</summary>
    private static byte[]? Realign(ReadOnlySpan<byte> frame, ref byte? carry)
    {
        var total = frame.Length + (carry.HasValue ? 1 : 0);
        if (total == 0)
        {
            return null;
        }

        var combined = new byte[total];
        var offset = 0;
        if (carry.HasValue)
        {
            combined[0] = carry.Value;
            offset = 1;
        }

        frame.CopyTo(combined.AsSpan(offset));

        if ((total & 1) == 0)
        {
            carry = null;
            return combined;
        }

        carry = combined[total - 1];
        if (total == 1)
        {
            return null;
        }

        Array.Resize(ref combined, total - 1);
        return combined;
    }

    private bool TryParseFinish(string json, out string? code, out string? message)
    {
        code = null;
        message = null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("event", out var evt) ||
                evt.ValueKind != JsonValueKind.String ||
                evt.GetString() != "finish")
            {
                return false;
            }

            code = root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString()
                : null;
            message = root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String
                ? m.GetString()
                : null;
            code = string.IsNullOrWhiteSpace(code) ? "service_error" : code;
            return true;
        }
        catch (JsonException)
        {
            _logger?.LogWarning("DeepSeek TTS 收到无法解析的文本帧，已忽略");
            return false;
        }
    }

    public Task AbortAsync()
    {
        if (Interlocked.Exchange(ref _aborted, 1) == 0)
        {
            try
            {
                _abortCts.Cancel();
            }
            catch
            {
            }

            try
            {
                _ws.Abort();
            }
            catch
            {
            }
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await AbortAsync().ConfigureAwait(false);
        try
        {
            _ws.Dispose();
        }
        catch
        {
        }

        _abortCts.Dispose();
    }
}
