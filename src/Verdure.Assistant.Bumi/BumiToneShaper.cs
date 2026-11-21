namespace Verdure.Assistant.Bumi;

/// <summary>
/// 机器人小喇叭的音色链：高通 → 存在感提升 → 增益 → 峰值限幅。
/// </summary>
/// <remarks>
/// 厂家确认音量推高时的失真属于硬件层面，暂时解决不了。既然不能靠加大电平换响度，就只能换频谱：
/// <list type="bullet">
/// <item>削掉小喇叭根本发不出来的低频——省下的振膜行程直接就是失真余量；</item>
/// <item>用高架把 2–4 kHz 抬一点，靠人耳在该频段的敏感度制造"清脆"的感觉；</item>
/// <item>最后用软拐点限幅代替 <c>Math.Clamp</c> 的硬截断，硬削波听起来就是沙沙的杂音。</item>
/// </list>
/// 默认参数与加入本类之前完全一致（250 Hz 单级高通、-6 dB、不提升、不限幅），
/// 每一项都可以独立打开做 A/B 对比。
/// </remarks>
internal sealed class BumiToneShaper
{
    private readonly Biquad? _highPassA;
    private readonly Biquad? _highPassB;
    private readonly Biquad? _presence;
    private readonly Biquad? _denseLowPass;
    private readonly Biquad? _denseHighPass;
    private readonly double _gain;
    private readonly double _ceiling;
    private readonly double _releaseCoeff;

    // 内容自适应：密度 = 最近 500 ms 里"相对自身峰值算有声"的时间占比。
    // 音乐几乎一直有声（约 0.9），语音有停顿与弱音（约 0.6–0.8）；阈值跟着峰值包络走，
    // 因此不受母带音量影响。语音基本不触发，音乐会被明显收敛。
    private readonly bool _protectDenseContent;
    private readonly double _gateLow;
    private readonly double _gateHigh;
    private readonly double _hotGainScale;
    private readonly double _peakDecayCoeff;
    private readonly double _densityCoeff;
    private readonly double _blendCoeff;

    private const double ActiveRelativeToPeak = 0.02; // 比局部峰值低 34 dB 以下不算"有声"
    private const double QuietRmsThreshold = 32.0;    // 约 -60 dBFS（样本域），静音时不做判定

    private double _peakEnvelope;
    private double _density;
    private double _blend = 1.0;

    private double _gainReduction = 1.0;

    public BumiToneShaper(BumiAudioOptions options)
    {
        var sampleRate = options.SampleRate;
        _gain = Math.Pow(10.0, options.OutputGainDb / 20.0);

        _protectDenseContent = options.ProtectDenseContent;
        _gateLow = options.DenseContentGateLow;
        _gateHigh = Math.Max(options.DenseContentGateLow + 0.01, options.DenseContentGateHigh);
        _hotGainScale = Math.Pow(10.0, options.DenseContentGainDb / 20.0);
        _peakDecayCoeff = 1.0 - Math.Exp(-1.0 / Math.Max(1.0, sampleRate * 1.0));
        _densityCoeff = 1.0 - Math.Exp(-1.0 / Math.Max(1.0, sampleRate * 0.5));
        _blendCoeff = 1.0 - Math.Exp(-1.0 / Math.Max(1.0, sampleRate * 0.4));

        if (options.HighPassHz > 0 && options.HighPassHz < sampleRate / 2)
        {
            if (options.HighPassStages >= 2)
            {
                // 四阶 Butterworth = 两级二阶，Q 值固定为这两个数。
                _highPassA = Biquad.HighPass(options.HighPassHz, sampleRate, 0.5412);
                _highPassB = Biquad.HighPass(options.HighPassHz, sampleRate, 1.3066);
            }
            else
            {
                _highPassA = Biquad.HighPass(options.HighPassHz, sampleRate, 0.7071);
            }
        }

        if (options.PresenceGainDb != 0 && options.PresenceHz > 0 && options.PresenceHz < sampleRate / 2)
        {
            _presence = Biquad.HighShelf(options.PresenceHz, sampleRate, options.PresenceGainDb);
        }

        if (_protectDenseContent && options.DenseContentLowPassHz > 0 && options.DenseContentLowPassHz < sampleRate / 2)
        {
            _denseLowPass = Biquad.LowPass(options.DenseContentLowPassHz, sampleRate, 0.7071);
        }

        if (_protectDenseContent && options.DenseContentHighPassHz > options.HighPassHz &&
            options.DenseContentHighPassHz < sampleRate / 2)
        {
            // 音乐的低频吃掉的正是振膜行程：这台小喇叭连 500 Hz 以下都发不出来（实测 180 Hz 谐波
            // 比基波还响），所以密集内容再抬一档高通，把行程让给中频。
            _denseHighPass = Biquad.HighPass(options.DenseContentHighPassHz, sampleRate, 0.7071);
        }

        if (options.LimiterCeilingDb < 0)
        {
            _ceiling = short.MaxValue * Math.Pow(10.0, options.LimiterCeilingDb / 20.0);
            var releaseSamples = Math.Max(1.0, sampleRate * options.LimiterReleaseMs / 1000.0);
            _releaseCoeff = 1.0 - Math.Exp(-1.0 / releaseSamples);
        }
        else
        {
            _ceiling = 0;
        }
    }

    public short Process(short sample)
    {
        // 活动度必须在**高通之前**判断：音乐的能量主要在 100 Hz 以下，先高通会把电平削掉一大半，
        // 判据就永远看不到"密集"。语音的密度在两种测法下都只有 0.5–0.7，所以这样不影响语音。
        var mix = UpdateHotScale(sample);
        mix *= mix;

        var value = (double)sample;

        if (_highPassA is not null)
        {
            value = _highPassA.Process(value);
        }

        if (_highPassB is not null)
        {
            value = _highPassB.Process(value);
        }

        if (_denseHighPass is not null)
        {
            var tight = _denseHighPass.Process(value);
            value += (tight - value) * (1.0 - mix);
        }

        if (_presence is not null)
        {
            // 高架始终跑（保持滤波器状态连续），再在干声与提升之间混合。
            var shaped = _presence.Process(value);
            value += (shaped - value) * mix;
        }

        if (_denseLowPass is not null)
        {
            // 密集内容退回平直时顺便收掉最上面的尖，小喇叭在这段最容易出破音。
            var dull = _denseLowPass.Process(value);
            value += (dull - value) * (1.0 - mix);
        }

        value *= _gain * (1.0 + ((_hotGainScale - 1.0) * (1.0 - mix)));

        if (_ceiling > 0)
        {
            value = Limit(value);
        }

        return (short)Math.Clamp(value, short.MinValue, short.MaxValue);
    }

    /// <summary>
    /// 返回 0–1 的"音色保留度"：1 = 稀疏语音（全量音色处理），0 = 密集内容（退回平直 + 让电平）。
    /// </summary>
    private double UpdateHotScale(double value)
    {
        if (!_protectDenseContent)
        {
            return 1.0;
        }

        // 密度 = 最近 500 ms 里"相对自身峰值算有声"的时间占比。阈值跟着峰值包络走，
        // 所以与母带音量无关：音乐几乎一直有声（约 0.9），语音有停顿与弱音（约 0.6–0.8）。
        var magnitude = Math.Abs(value);
        _peakEnvelope = magnitude > _peakEnvelope
            ? magnitude
            : _peakEnvelope + ((magnitude - _peakEnvelope) * _peakDecayCoeff);

        var threshold = Math.Max(_peakEnvelope * ActiveRelativeToPeak, QuietRmsThreshold);
        var active = magnitude > threshold ? 1.0 : 0.0;
        _density += (active - _density) * _densityCoeff;

        var target = Math.Clamp((_gateHigh - _density) / (_gateHigh - _gateLow), 0.0, 1.0);
        _blend += (target - _blend) * _blendCoeff;
        return _blend;
    }

    /// <summary>峰值限幅：增益回落立即生效（不会过冲），恢复慢一些避免"抽吸"感。</summary>
    private double Limit(double value)
    {
        var magnitude = Math.Abs(value);
        var target = magnitude > _ceiling ? _ceiling / magnitude : 1.0;

        _gainReduction = target < _gainReduction
            ? target
            : _gainReduction + ((target - _gainReduction) * _releaseCoeff);

        return value * _gainReduction;
    }

    public void Reset()
    {
        _highPassA?.Reset();
        _highPassB?.Reset();
        _presence?.Reset();
        _denseLowPass?.Reset();
        _denseHighPass?.Reset();
        _gainReduction = 1.0;
        _peakEnvelope = 0;
        _density = 0;
        _blend = 1.0;
    }

    /// <summary>RBJ 双二阶滤波器（直接 I 型）。</summary>
    private sealed class Biquad
    {
        private readonly double _b0, _b1, _b2, _a1, _a2;
        private double _x1, _x2, _y1, _y2;

        private Biquad(double b0, double b1, double b2, double a0, double a1, double a2)
        {
            _b0 = b0 / a0;
            _b1 = b1 / a0;
            _b2 = b2 / a0;
            _a1 = a1 / a0;
            _a2 = a2 / a0;
        }

        public static Biquad HighPass(double cutoffHz, int sampleRate, double q)
        {
            var w0 = 2 * Math.PI * cutoffHz / sampleRate;
            var cos = Math.Cos(w0);
            var alpha = Math.Sin(w0) / (2 * q);

            return new Biquad(
                (1 + cos) / 2,
                -(1 + cos),
                (1 + cos) / 2,
                1 + alpha,
                -2 * cos,
                1 - alpha);
        }

        /// <summary>低通：把 <paramref name="cutoffHz"/> 以上的频段压掉，用于给密集内容"去尖"。</summary>
        public static Biquad LowPass(double cutoffHz, int sampleRate, double q)
        {
            var w0 = 2 * Math.PI * cutoffHz / sampleRate;
            var cos = Math.Cos(w0);
            var alpha = Math.Sin(w0) / (2 * q);

            return new Biquad(
                (1 - cos) / 2,
                1 - cos,
                (1 - cos) / 2,
                1 + alpha,
                -2 * cos,
                1 - alpha);
        }

        /// <summary>高架：把 <paramref name="cutoffHz"/> 以上的频段整体抬升 <paramref name="gainDb"/>。</summary>
        public static Biquad HighShelf(double cutoffHz, int sampleRate, double gainDb)        {
            var amplitude = Math.Pow(10.0, gainDb / 40.0);
            var w0 = 2 * Math.PI * cutoffHz / sampleRate;
            var cos = Math.Cos(w0);
            var sin = Math.Sin(w0);
            var alpha = sin / 2 * Math.Sqrt((amplitude + 1 / amplitude) * (1 / 1.0 - 1) + 2);
            var twoSqrtAAlpha = 2 * Math.Sqrt(amplitude) * alpha;

            return new Biquad(
                amplitude * ((amplitude + 1) + ((amplitude - 1) * cos) + twoSqrtAAlpha),
                -2 * amplitude * ((amplitude - 1) + ((amplitude + 1) * cos)),
                amplitude * ((amplitude + 1) + ((amplitude - 1) * cos) - twoSqrtAAlpha),
                (amplitude + 1) - ((amplitude - 1) * cos) + twoSqrtAAlpha,
                2 * ((amplitude - 1) - ((amplitude + 1) * cos)),
                (amplitude + 1) - ((amplitude - 1) * cos) - twoSqrtAAlpha);
        }

        public double Process(double input)
        {
            var output = (_b0 * input) + (_b1 * _x1) + (_b2 * _x2) - (_a1 * _y1) - (_a2 * _y2);
            _x2 = _x1;
            _x1 = input;
            _y2 = _y1;
            _y1 = output;
            return output;
        }

        public void Reset() => _x1 = _x2 = _y1 = _y2 = 0;
    }
}
