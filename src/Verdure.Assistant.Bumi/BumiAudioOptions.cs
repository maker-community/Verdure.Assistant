namespace Verdure.Assistant.Bumi;

/// <summary>
/// Bumi 音频桥的配置。默认值来自真机实测：播放需 2 声道 / 16 kHz；服务端 TTS 是 26 kbps 的 Opus，
/// 峰值只有 -5 dBFS 且 2–4 kHz 空得很厉害，所以默认把电平补回来、用 2.5 kHz 高架抬"清脆"、
/// 150 Hz 高通省掉小喇叭发不出的低频、再用 -3 dBFS 限幅兜住削波；模块音量 100 是响度与失真的折中。
/// </summary>
public sealed class BumiAudioOptions
{
    /// <summary>传递给 CycloneDDS 的配置路径（机器人上为官方 SDK 的 config/dds.xml）。</summary>
    public string DdsConfigPath { get; set; } = "config/dds.xml";

    /// <summary>机器人语音模块固定的采样率，采集与播放都必须是 16000。</summary>
    public int SampleRate { get; set; } = 16000;

    /// <summary>播放帧长（毫秒）。实测 20 ms 与 100 ms 无差别。</summary>
    public int PlaybackFrameMs { get; set; } = 20;

    /// <summary>
    /// 播放增益（dB）。服务端 TTS 的峰值本来就在 -5 dBFS 左右，默认 +1 dB 把它推到近满幅，
    /// 再由限幅兜底；放音乐这类已经接近满幅的素材时请改回负值。
    /// </summary>
    public double OutputGainDb { get; set; } = 1;

    /// <summary>
    /// 采集增益（dB）。机器人头顶麦克风远场电平很低（1–2 m 约 -34 dBFS），
    /// 不加增益服务端识别不出来。默认 +18 dB。
    /// </summary>
    public double InputGainDb { get; set; } = 18;

    /// <summary>
    /// 播放前的高通截止频率（Hz）。设为 0 关闭。默认 150：小喇叭根本发不出这个频率以下的能量，
    /// 削掉它等于把振膜行程还给中频。
    /// </summary>
    public int HighPassHz { get; set; } = 150;

    /// <summary>
    /// 高通级数：1 = 二阶（12 dB/oct），2 = 四阶（24 dB/oct）。
    /// 小喇叭发不出来的低频越干净，同样的音量下失真越小。
    /// </summary>
    public int HighPassStages { get; set; } = 1;

    /// <summary>存在感高架的截止频率（Hz）。默认 2500。</summary>
    public int PresenceHz { get; set; } = 2500;

    /// <summary>
    /// 存在感高架的增益（dB）。默认 +6：服务端 TTS 在 2–4 kHz 只有 -17 dBc，抬高这一段的
    /// 听感提升最大；再加到 +8（3 kHz）在音量 100 时齿音会开始刺。
    /// </summary>
    public double PresenceGainDb { get; set; } = 6;

    /// <summary>
    /// 峰值限幅上限（dBFS）。默认 -3：留 3 dB 给高架提升后的瞬态，避免硬削波的沙沙声。
    /// </summary>
    public double LimiterCeilingDb { get; set; } = -3;

    /// <summary>限幅后的恢复时间（毫秒）。太短会有"抽吸"感，默认 120。</summary>
    public double LimiterReleaseMs { get; set; } = 120;

    /// <summary>
    /// 播放前导缓冲（毫秒）。TTS 帧是网络突发到达的，缓冲越足越不容易出现空洞；默认 300。
    /// </summary>
    public int PlaybackLeadMs { get; set; } = 300;

    /// <summary>
    /// 是否按墙钟节奏推送。默认 true（机器人上实测这个节奏最稳）。
    /// 设为 false 时会把音频尽快灌给原生播放侧，实测反而更断。
    /// </summary>
    public bool PlaybackPaceRealtime { get; set; } = true;

    /// <summary>机器人音量（0–200）。实测 60 最干净、100 略脏但明显更响；100 是响度与失真的折中。</summary>
    public int Volume { get; set; } = 100;

    /// <summary>
    /// 是否静音内置智能体。只关它的输出，不动麦克风输入——关掉输入会连带杀掉扬声器。
    /// </summary>
    public bool MuteBuiltInAgent { get; set; } = true;

    /// <summary>是否在关闭提示音。</summary>
    public bool MuteAudioCue { get; set; } = true;

    /// <summary>
    /// 退出时是否把开关恢复到"可用"状态（内置智能体输出打开、提示音打开、音量还原）。
    /// 强烈建议保持开启：关掉后机器人自己的助手会在服务退出后一直是哑的。
    /// </summary>
    public bool RestoreOnExit { get; set; } = true;

    /// <summary>
    /// 非空时把真正推给扬声器的采样（已过音色链、立体声 16 kHz）落成 WAV，便于离线比对音质。
    /// 默认关闭；只在排查音质时打开。
    /// </summary>
    public string DumpTtsDirectory { get; set; } = string.Empty;
}