using System.Text;

namespace Verdure.Assistant.Core.Services.DeepSeek.Speech;

/// <summary>
/// 把流式回复文本切成适合送入语音合成的短句：遇到句末标点就送出（降低首字延迟），过长时在逗号/空格处强制切分。
/// </summary>
public sealed class SpeechTextChunker
{
    private const string SentenceEnds = "。！？!?；;\n";
    private const string SoftBreaks = "，,、：: ";

    // 第一个短句遇到逗号就送出（首字出声更快）；之后仍按完整句子送，保证语调自然
    private const int FirstChunkSoftBreakMinChars = 6;

    private readonly StringBuilder _buffer = new();
    private readonly int _minChunkChars;
    private readonly int _maxChunkChars;
    private bool _emittedAny;

    public SpeechTextChunker(int minChunkChars = 4, int maxChunkChars = 80)
    {
        _minChunkChars = Math.Max(1, minChunkChars);
        _maxChunkChars = Math.Max(_minChunkChars + 1, maxChunkChars);
    }

    /// <summary>追加一段增量文本，返回此刻可以送出的完整短句（可能为空）</summary>
    public IReadOnlyList<string> Add(string delta)
    {
        if (string.IsNullOrEmpty(delta))
        {
            return Array.Empty<string>();
        }

        var ready = new List<string>();
        foreach (var ch in delta)
        {
            _buffer.Append(ch);

            if (IsSentenceEnd(ch) && _buffer.Length >= _minChunkChars)
            {
                Emit(ready, _buffer.Length);
            }
            else if (!_emittedAny && ch is '，' or ',' or '、' && _buffer.Length >= FirstChunkSoftBreakMinChars)
            {
                Emit(ready, _buffer.Length);
            }
            else if (_buffer.Length >= _maxChunkChars)
            {
                var cut = LastSoftBreak();
                Emit(ready, cut > 0 ? cut : _buffer.Length);
            }
        }

        return ready;
    }

    /// <summary>回复结束时取出剩余文本</summary>
    public string? Flush()
    {
        var rest = _buffer.ToString().Trim();
        _buffer.Clear();
        return rest.Length == 0 ? null : rest;
    }

    public void Reset()
    {
        _buffer.Clear();
        _emittedAny = false;
    }

    private bool IsSentenceEnd(char ch)
    {
        if (SentenceEnds.Contains(ch))
        {
            return true;
        }

        // 英文句号：前一个字符是数字时视为小数点，不切分
        if (ch == '.' && _buffer.Length >= 2)
        {
            return !char.IsDigit(_buffer[^2]);
        }

        return false;
    }

    private int LastSoftBreak()
    {
        for (var i = _buffer.Length - 1; i >= _minChunkChars; i--)
        {
            if (SoftBreaks.Contains(_buffer[i]))
            {
                return i + 1;
            }
        }

        return 0;
    }

    private void Emit(List<string> ready, int length)
    {
        var text = _buffer.ToString(0, length).Trim();
        _buffer.Remove(0, length);
        if (text.Length > 0)
        {
            ready.Add(text);
            _emittedAny = true;
        }
    }
}
