using Microsoft.UI.Xaml;

namespace Verdure.Assistant.WinUI.Converters;

/// <summary>
/// 供 x:Bind 函数绑定使用的界面格式化辅助方法
/// </summary>
public static class UiFormat
{
    public static string OrDash(string? value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

    public static Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility Hide(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public static Visibility HasText(string? value) => string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;

    public static string VoiceButtonText(bool active) => active ? "结束语音" : "语音对话";

    public static string VoiceButtonGlyph(bool active) => active ? "\uE71A" : "\uE720";

    public static Visibility ShowWhenZero(int count) => count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility ShowWhenAny(int count) => count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public static string Time(DateTime value) => value.ToString("HH:mm:ss");

    /// <summary>
    /// 连续对话按钮文案：空闲时开始，其他阶段结束
    /// </summary>
    public static string AutoDialogText(string phase) => phase is "Idle" or "Offline" ? "连续对话" : "结束对话";

    public static string AutoDialogGlyph(string phase) => phase is "Idle" or "Offline" ? "\uE8F2" : "\uE71A";

    public static string ThinkingChip(bool enabled, string effort) => enabled ? $"思考 · {effort}" : "非思考模式";

    /// <summary>
    /// 主按钮下方的操作提示
    /// </summary>
    public static string TalkHint(string phase) => phase switch
    {
        "Offline" => "语音服务未启动，请先点击右上角“启动”",
        "Idle" => "按住麦克风说话，或直接说出唤醒词",
        "Connecting" => "正在连接服务器…",
        "Listening" => "正在聆听，松开即发送",
        "Speaking" => "正在回复，可随时点击“打断”",
        _ => string.Empty
    };
}
