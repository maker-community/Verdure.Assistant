namespace Verdure.Assistant.Core.Services.DeepSeek.Speech;

public enum UtteranceEventKind
{
    /// <summary>检测到开始说话</summary>
    SpeechStarted,

    /// <summary>一句话结束，<see cref="UtteranceEvent.Pcm"/> 为完整音频（含少量前导静音）</summary>
    UtteranceCompleted,

    /// <summary>声音太短（咳嗽、敲击等），已丢弃</summary>
    UtteranceDiscarded
}

public readonly record struct UtteranceEvent(
    UtteranceEventKind Kind, byte[]? Pcm, int SpeechMs, int TotalMs = 0, int TrailingSilenceMs = 0);

/// <summary>
/// 端点检测参数
/// </summary>
public sealed class UtteranceDetectorOptions
{
    public int SampleRate { get; set; } = 16000;

    /// <summary>分析帧长（毫秒）</summary>
    public int FrameMs { get; set; } = 20;

    /// <summary>判定为"有声"的最低 RMS（0~1，满量程为 1）</summary>
    public double MinThreshold { get; set; } = 0.012;

    /// <summary>有声阈值相对环境噪声底的倍数</summary>
    public double NoiseMultiplier { get; set; } = 3.0;

    /// <summary>连续有声多久才算开始说话（毫秒），用于滤掉瞬时噪声</summary>
    public int StartSpeechMs { get; set; } = 120;

    /// <summary>说话结束后需要多长的静音才判定一句话结束（毫秒）。越小回复越快，但句中停顿会把话切断。</summary>
    public int EndSilenceMs { get; set; } = 700;

    /// <summary>有效语音最短时长（毫秒），短于此值视为噪声</summary>
    public int MinUtteranceMs { get; set; } = 350;

    /// <summary>单句最长时长（毫秒），到达后强制结束（DeepSeek 语音识别上限 120 秒）</summary>
    public int MaxUtteranceMs { get; set; } = 60000;

    /// <summary>保留在语音前面的音频（毫秒），避免吞掉第一个字</summary>
    public int PreRollMs { get; set; } = 300;

    /// <summary>保留在语音后面的静音（毫秒）</summary>
    public int TrailingSilenceMs { get; set; } = 200;
}

/// <summary>
/// 基于能量的语音端点检测：自适应噪声底 + 起始确认 + 迟滞，把连续的麦克风流切成"一句话"。
/// 输入 16 位小端单声道 PCM，可任意大小分块。非线程安全。
/// </summary>
public sealed class UtteranceDetector
{
    private const double InitialNoiseFloor = 0.004;
    private const double MaxNoiseFloor = 0.12;
    private const double HysteresisFactor = 0.7;

    // 噪声底估计：取最近 3 秒帧能量的低分位数；上升只在"平稳噪声"（高低分位数相差不大）时才快速跟随，
    // 这样稳定的风扇/空调声会被学成噪声底，而说话（起伏很大）不会把噪声底抬高
    private const int NoiseWindowFrames = 150;
    private const int NoiseUpdateEveryFrames = 5;
    private const double LowPercentile = 0.15;
    private const double HighPercentile = 0.85;
    private const double SteadyRatio = 2.0;
    private const double NoiseRiseRate = 0.15;
    private const double NoiseRiseRateInSpeech = 0.02;
    private const double NoiseFallRate = 0.3;

    // 开头 0.5 秒只用来学习环境噪声，不会触发"开始说话"，避免稳定的背景声一开始就被当成人声
    private const int CalibrationFrames = 25;
    private const double CalibrationMaxNoise = 0.05;

    private static readonly UtteranceEvent[] NoEvents = Array.Empty<UtteranceEvent>();

    private readonly UtteranceDetectorOptions _o;
    private readonly int _frameSamples;
    private readonly int _startFrames;
    private readonly int _endSilenceFrames;
    private readonly int _preRollFrames;
    private readonly int _trailingFrames;
    private readonly int _maxFrames;

    private readonly short[] _frame;
    private int _frameFill;
    private byte _pendingByte;
    private bool _hasPendingByte;

    private readonly Queue<(short[] Frame, bool Voiced)> _preRoll = new();
    private List<short[]>? _utterance;
    private int _voicedRun;
    private int _silenceFrames;
    private int _voicedFrames;
    private double _noiseFloor = InitialNoiseFloor;

    private readonly double[] _recent = new double[NoiseWindowFrames];
    private readonly double[] _sorted = new double[NoiseWindowFrames];
    private int _recentCount;
    private int _recentIndex;
    private int _framesSinceNoiseUpdate;
    private int _framesSeen;

    public UtteranceDetector(UtteranceDetectorOptions? options = null)
    {
        _o = options ?? new UtteranceDetectorOptions();
        _frameSamples = Math.Max(1, _o.SampleRate * _o.FrameMs / 1000);
        _frame = new short[_frameSamples];
        _startFrames = Math.Max(1, _o.StartSpeechMs / _o.FrameMs);
        _endSilenceFrames = Math.Max(1, _o.EndSilenceMs / _o.FrameMs);
        _preRollFrames = Math.Max(0, _o.PreRollMs / _o.FrameMs);
        _trailingFrames = Math.Max(0, _o.TrailingSilenceMs / _o.FrameMs);
        _maxFrames = Math.Max(1, _o.MaxUtteranceMs / _o.FrameMs);
    }

    /// <summary>是否正处于一句话之中</summary>
    public bool InSpeech => _utterance != null;

    /// <summary>最近一帧的音量（RMS，0~1），可用于界面电平显示</summary>
    public double CurrentLevel { get; private set; }

    /// <summary>当前使用的有声阈值</summary>
    public double Threshold => Math.Max(_o.MinThreshold, _noiseFloor * _o.NoiseMultiplier);

    /// <summary>当前估计的环境噪声底（RMS，0~1）</summary>
    public double NoiseFloor => _noiseFloor;

    /// <summary>
    /// 清空状态（保留已学到的噪声底）
    /// </summary>
    public void Reset()
    {
        _utterance = null;
        _preRoll.Clear();
        _voicedRun = 0;
        _silenceFrames = 0;
        _voicedFrames = 0;
        _frameFill = 0;
        _hasPendingByte = false;
        CurrentLevel = 0;
    }

    /// <summary>
    /// 输入一块 PCM，返回这块数据触发的事件（通常为空）
    /// </summary>
    public IReadOnlyList<UtteranceEvent> Feed(ReadOnlySpan<byte> pcm)
    {
        List<UtteranceEvent>? events = null;
        var index = 0;

        if (_hasPendingByte && pcm.Length > 0)
        {
            Push((short)(_pendingByte | (pcm[0] << 8)), ref events);
            _hasPendingByte = false;
            index = 1;
        }

        for (; index + 1 < pcm.Length; index += 2)
        {
            Push((short)(pcm[index] | (pcm[index + 1] << 8)), ref events);
        }

        if (index < pcm.Length)
        {
            _pendingByte = pcm[index];
            _hasPendingByte = true;
        }

        return events ?? (IReadOnlyList<UtteranceEvent>)NoEvents;
    }

    private void Push(short sample, ref List<UtteranceEvent>? events)
    {
        _frame[_frameFill++] = sample;
        if (_frameFill < _frameSamples)
        {
            return;
        }

        _frameFill = 0;
        ProcessFrame((short[])_frame.Clone(), ref events);
    }

    private void ProcessFrame(short[] frame, ref List<UtteranceEvent>? events)
    {
        double sum = 0;
        foreach (var s in frame)
        {
            var v = s / 32768.0;
            sum += v * v;
        }

        var rms = Math.Sqrt(sum / frame.Length);
        CurrentLevel = rms;
        _framesSeen++;
        UpdateNoiseFloor(rms);

        var threshold = Threshold;
        var voiced = _utterance == null ? rms > threshold : rms > threshold * HysteresisFactor;

        if (_utterance == null)
        {
            _preRoll.Enqueue((frame, voiced));
            while (_preRoll.Count > _preRollFrames + _startFrames)
            {
                _preRoll.Dequeue();
            }

            _voicedRun = voiced && _framesSeen > CalibrationFrames ? _voicedRun + 1 : 0;
            if (_voicedRun >= _startFrames)
            {
                // 校准期内的有声帧也计入有效语音时长，音频本身一直在预录缓冲里，不会丢
                _utterance = new List<short[]>(_preRoll.Select(p => p.Frame));
                _voicedFrames = _preRoll.Count(p => p.Voiced);
                _preRoll.Clear();
                _silenceFrames = 0;
                _voicedRun = 0;
                (events ??= new List<UtteranceEvent>()).Add(new UtteranceEvent(UtteranceEventKind.SpeechStarted, null, 0));
            }

            return;
        }

        _utterance.Add(frame);
        if (voiced)
        {
            _silenceFrames = 0;
            _voicedFrames++;
        }
        else
        {
            _silenceFrames++;
        }

        if (_silenceFrames >= _endSilenceFrames || _utterance.Count >= _maxFrames)
        {
            Finish(ref events);
        }
    }

    private void UpdateNoiseFloor(double rms)
    {
        _recent[_recentIndex] = rms;
        _recentIndex = (_recentIndex + 1) % NoiseWindowFrames;
        if (_recentCount < NoiseWindowFrames)
        {
            _recentCount++;
        }

        if (++_framesSinceNoiseUpdate < NoiseUpdateEveryFrames)
        {
            return;
        }

        _framesSinceNoiseUpdate = 0;
        Array.Copy(_recent, _sorted, _recentCount);
        Array.Sort(_sorted, 0, _recentCount);

        var low = _sorted[(int)((_recentCount - 1) * LowPercentile)];
        var high = _sorted[(int)((_recentCount - 1) * HighPercentile)];

        if (_framesSeen <= CalibrationFrames)
        {
            // 校准期：窗口里至少要有 0.4 秒的数据才能判断"平稳还是起伏"（太短会把说话的一小段误判为平稳噪声）；
            // 只在平稳时采用低分位数作为噪声底并限制上限，避免一开口就说话的人被当成噪声
            if (_recentCount >= 20 && high <= Math.Max(low * SteadyRatio, _o.MinThreshold * 0.5))
            {
                _noiseFloor = Math.Clamp(low, 0.0005, CalibrationMaxNoise);
            }

            return;
        }

        if (low < _noiseFloor)
        {
            // 变安静：快速跟随
            _noiseFloor += (low - _noiseFloor) * NoiseFallRate;
        }
        else if (high <= Math.Max(low * SteadyRatio, _o.MinThreshold * 0.5))
        {
            // 起伏很小 = 平稳的环境噪声，把它学成噪声底；说话过程中只允许慢速上升，避免把持续的大声说话学成噪声
            _noiseFloor += (low - _noiseFloor) * (_utterance != null ? NoiseRiseRateInSpeech : NoiseRiseRate);
        }

        _noiseFloor = Math.Clamp(_noiseFloor, 0.0005, MaxNoiseFloor);
    }

    private void Finish(ref List<UtteranceEvent>? events)
    {
        var frames = _utterance!;
        _utterance = null;

        var speechMs = _voicedFrames * _o.FrameMs;
        var trailingMs = _silenceFrames * _o.FrameMs;
        var totalMs = frames.Count * _o.FrameMs;
        var silentTail = Math.Max(0, _silenceFrames - _trailingFrames);
        var keep = Math.Max(1, frames.Count - silentTail);

        _silenceFrames = 0;
        _voicedFrames = 0;
        _voicedRun = 0;
        _preRoll.Clear();

        if (speechMs < _o.MinUtteranceMs)
        {
            (events ??= new List<UtteranceEvent>()).Add(new UtteranceEvent(UtteranceEventKind.UtteranceDiscarded, null, speechMs, totalMs, trailingMs));
            return;
        }

        var pcm = new byte[keep * _frameSamples * 2];
        var offset = 0;
        for (var i = 0; i < keep; i++)
        {
            foreach (var s in frames[i])
            {
                pcm[offset++] = (byte)(s & 0xFF);
                pcm[offset++] = (byte)((s >> 8) & 0xFF);
            }
        }

        (events ??= new List<UtteranceEvent>()).Add(new UtteranceEvent(UtteranceEventKind.UtteranceCompleted, pcm, speechMs, totalMs, trailingMs));
    }
}
