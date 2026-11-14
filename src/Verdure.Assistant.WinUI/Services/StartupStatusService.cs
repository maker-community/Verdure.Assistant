namespace Verdure.Assistant.WinUI.Services;

public enum StartupPhase
{
    Starting,
    Ready,
    Degraded,
    Failed
}

/// <summary>
/// 启动阶段状态：App 在后台逐步初始化各服务，主窗口订阅此状态展示"加载中 / 部分功能不可用 / 启动失败"，
/// 让用户看到发生了什么，而不是面对一个无响应或不完整的界面。
/// </summary>
public sealed class StartupStatusService
{
    public StartupPhase Phase { get; private set; } = StartupPhase.Starting;

    public string Message { get; private set; } = string.Empty;

    /// <summary>
    /// 状态变化事件（可能在任意线程触发，订阅方负责切换到 UI 线程）
    /// </summary>
    public event EventHandler? Changed;

    public void Report(StartupPhase phase, string message)
    {
        Phase = phase;
        Message = message;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
