using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace Verdure.Assistant.WinUI.Services;

/// <summary>
/// 使用 Windows 内置语音合成朗读文本（用于朗读 DeepSeek 的回复）
/// </summary>
public sealed partial class WinUISpeechService : IDisposable
{
    private readonly ILogger<WinUISpeechService>? _logger;
    private readonly SpeechSynthesizer _synthesizer = new();
    private readonly MediaPlayer _player = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _version;

    public WinUISpeechService(ILogger<WinUISpeechService>? logger = null)
    {
        _logger = logger;
    }

    public async Task SpeakAsync(string text)
    {
        var plain = ToPlainText(text);
        if (string.IsNullOrWhiteSpace(plain))
        {
            return;
        }

        // 新的朗读请求会取代进行中的合成/播放
        var version = Interlocked.Increment(ref _version);
        await _gate.WaitAsync();
        try
        {
            if (version != Volatile.Read(ref _version))
            {
                return;
            }

            var stream = await _synthesizer.SynthesizeTextToStreamAsync(plain);
            if (version != Volatile.Read(ref _version))
            {
                stream.Dispose();
                return;
            }

            _player.Source = MediaSource.CreateFromStream(stream, stream.ContentType);
            _player.Play();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "语音朗读失败");
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Stop()
    {
        Interlocked.Increment(ref _version);
        try
        {
            _player.Pause();
            _player.Source = null;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "停止朗读时出现警告");
        }
    }

    /// <summary>
    /// 去掉 Markdown 标记，避免把"星号""井号"念出来
    /// </summary>
    internal static string ToPlainText(string markdown)
    {
        var text = CodeBlockRegex().Replace(markdown, " ");
        text = InlineCodeRegex().Replace(text, "$1");
        text = LinkRegex().Replace(text, "$1");
        text = MarkerRegex().Replace(text, string.Empty);
        return text.Trim();
    }

    [GeneratedRegex("```[\\s\\S]*?```")]
    private static partial Regex CodeBlockRegex();

    [GeneratedRegex("`([^`]*)`")]
    private static partial Regex InlineCodeRegex();

    [GeneratedRegex("\\[([^\\]]+)\\]\\([^)]*\\)")]
    private static partial Regex LinkRegex();

    [GeneratedRegex("(?m)^\\s{0,3}(#{1,6}|>|[-*+])\\s+|[*_~]{1,3}")]
    private static partial Regex MarkerRegex();

    public void Dispose()
    {
        Stop();
        _player.Dispose();
        _synthesizer.Dispose();
        _gate.Dispose();
    }
}
