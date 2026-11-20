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