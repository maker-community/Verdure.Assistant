namespace Verdure.Assistant.Bumi;

/// <summary>
/// Bumi 音频桥的配置。默认值来自真机实测：播放需 2 声道 / 16 kHz，250 Hz 高通 + -6 dB 后
/// 机身小喇叭才不会破音，音量 120 是 0–200 中的最佳折中。
/// </summary>
public sealed class BumiAudioOptions
{
    /// <summary>传递给 CycloneDDS 的配置路径（机器人上为官方 SDK 的 config/dds.xml）。</summary>
    public string DdsConfigPath { get; set; } = "config/dds.xml";

    /// <summary>机器人语音模块固定的采样率，采集与播放都必须是 16000。</summary>
    public int SampleRate { get; set; } = 16000;

    /// <summary>播放帧长（毫秒）。实测 20 ms 与 100 ms 无差别。</summary>
    public int PlaybackFrameMs { get; set; } = 20;

    /// <summary>播放增益（dB）。默认 -6 dB 用于给音乐留余量。</summary>
    public double OutputGainDb { get; set; } = -6;

    /// <summary>
    /// 采集增益（dB）。机器人头顶麦克风远场电平很低（1–2 m 约 -34 dBFS），
    /// 不加增益服务端识别不出来。默认 +18 dB。
    /// </summary>
    public double InputGainDb { get; set; } = 18;

    /// <summary>播放前的高通截止频率（Hz）。设为 0 关闭。</summary>
    public int HighPassHz { get; set; } = 250;

    /// <summary>
    /// 高通级数：1 = 二阶（12 dB/oct，原来的行为），2 = 四阶（24 dB/oct）。
    /// 小喇叭发不出来的低频越干净，同样的音量下失真越小。
    /// </summary>
    public int HighPassStages { get; set; } = 1;

    /// <summary>存在感高架的截止频率（Hz）。默认 3000。</summary>
    public int PresenceHz { get; set; } = 3000;

    /// <summary>存在感高架的增益（dB）。默认 0（不提升）；+2~+4 会让声音更"清脆"。</summary>
    public double PresenceGainDb { get; set; } = 0;

    /// <summary>
    /// 峰值限幅上限（dBFS）。默认 0（不限幅）；建议 -3，避免推到满幅时硬削波产生沙沙声。
    /// </summary>
    public double LimiterCeilingDb { get; set; }

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

    /// <summary>机器人音量（0–200）。默认 120。</summary>
    public int Volume { get; set; } = 120;

    /// <summary>
    /// 是否静音内置智能体。只关它的输出，不动麦克风输入——关掉输入会连带杀掉扬声器。
    /// </summary>
    public bool MuteBuiltInAgent { get; set; } = true;

    /// <summary>是否在关闭提示音。</summary>
    public bool MuteAudioCue { get; set; } = true;

    /// <summary>退出时是否把改动过的开关恢复原值（强烈建议保持开启）。</summary>
    public bool RestoreOnExit { get; set; } = true;
}