namespace Verdure.Assistant.MAUI;

public partial class App : Application
{
    private readonly ILogger<App>? _logger;

    public App(ILogger<App>? logger = null)
    {
        InitializeComponent();
        _logger = logger;

        _logger?.LogInformation("Verdure Assistant MAUI应用程序已启动");
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new AppShell() { Title = "绿荫助手" });
        //var window = base.CreateWindow(activationState);

        //// 设置窗口标题
        //window.Title = "绿荫助手";

        //// 设置主页面
        //window.Page = new AppShell();

        //return window;
    }
}
