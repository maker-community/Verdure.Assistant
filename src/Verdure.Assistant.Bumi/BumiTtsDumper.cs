using Microsoft.Extensions.Logging;

namespace Verdure.Assistant.Bumi;

/// <summary>
/// 把真正送到扬声器的采样落成 16 kHz 立体声 WAV，用来离线分辨"是服务端给的音频脏"还是"播放路径脏"。
/// </summary>
/// <remarks>
/// 排查音质时最有用的对照：把这里录到的 WAV 丢给 MediaTest 直接播，如果同样脏，问题在内容；
/// 如果干净，问题在播放路径（节拍、开关、电平）。
/// </remarks>
internal sealed class BumiTtsDumper : IDisposable
{
    private const int HeaderBytes = 44;

    private readonly ILogger _logger;
    private readonly string _path;
    private readonly int _sampleRate;
    private readonly object _gate = new();

    private FileStream? _stream;
    private long _samplesW;
    private bool _disposed;

    public BumiTtsDumper(string directory, int sampleRate, ILogger logger)
    {
        _logger = logger;
        _sampleRate = sampleRate;
        _path = Path.Combine(directory, $"tts-{DateTime.Now:yyyyMMdd-HHmmss}.wav");

        try
        {
            Directory.CreateDirectory(directory);
            _stream = File.Create(_path);
            _stream.Write(new byte[HeaderBytes]);
            _logger.LogInformation("TTS 落盘已开启：{Path}", _path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "无法创建 TTS 落盘文件 {Path}，本次不落盘", _path);
            _stream = null;
        }
    }

    /// <summary>写入一帧立体声采样（左右交错）。</summary>
    public void Write(short[] samples)
    {
        if (samples.Length == 0)
        {
            return;
        }

        lock (_gate)
        {
            if (_stream is null || _disposed)
            {
                return;
            }

            try
            {
                var bytes = new byte[samples.Length * 2];
                Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
                _stream.Write(bytes, 0, bytes.Length);
                _samplesW += samples.Length;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "写 TTS 落盘文件失败，停止落盘");
                Close();
            }
        }
    }

    /// <summary>补好 WAV 头，让文件随时可以被播放器打开。</summary>
    public void Flush()
    {
        lock (_gate)
        {
            PatchHeader();
        }
    }

    private void PatchHeader()
    {
        if (_stream is null)
        {
            return;
        }

        try
        {
            var header = BuildHeader((uint)(_samplesW * 2));
            _stream.Seek(0, SeekOrigin.Begin);
            _stream.Write(header, 0, header.Length);
            _stream.Seek(0, SeekOrigin.End);
            _stream.Flush();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "补写 TTS 落盘文件头失败");
        }
    }

    private byte[] BuildHeader(uint dataBytes)
    {
        const ushort channels = 2;
        const ushort bitsPerSample = 16;
        var blockAlign = (ushort)(channels * bitsPerSample / 8);

        using var buffer = new MemoryStream(HeaderBytes);
        using var writer = new BinaryWriter(buffer);
        writer.Write("RIFF"u8.ToArray());
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8.ToArray());
        writer.Write("fmt "u8.ToArray());
        writer.Write(16);
        writer.Write((ushort)1);
        writer.Write(channels);
        writer.Write((uint)_sampleRate);
        writer.Write((uint)(_sampleRate * blockAlign));
        writer.Write(blockAlign);
        writer.Write(bitsPerSample);
        writer.Write("data"u8.ToArray());
        writer.Write(dataBytes);
        writer.Flush();
        return buffer.ToArray();
    }

    private void Close()
    {
        try
        {
            _stream?.Dispose();
        }
        catch
        {
            // 关文件失败没法补救，忽略。
        }

        _stream = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_stream is null)
            {
                return;
            }

            PatchHeader();
            Close();
            _logger.LogInformation("TTS 落盘结束：{Path}（{Seconds:F1}s）", _path, _samplesW / 2.0 / _sampleRate);
        }
    }
}
