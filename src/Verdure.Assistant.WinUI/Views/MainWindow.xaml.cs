using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Windows.ApplicationModel.Resources;
using WinUIEx;
using Verdure.Assistant.ViewModels;
using Verdure.Assistant.WinUI.Services;

namespace Verdure.Assistant.WinUI.Views;

/// <summary>
/// 主窗口 - Frame导航模式的主窗口
/// </summary>
public sealed partial class MainWindow : WindowEx
{    private readonly ILogger<MainWindow>? _logger;
    private readonly ResourceLoader _resourceLoader = new();
    private readonly MainWindowViewModel _viewModel;

    // Expose ViewModel for x:Bind
    public MainWindowViewModel ViewModel => _viewModel;

    public MainWindow()
    {
        this.InitializeComponent();
        _logger = App.GetService<ILogger<MainWindow>>();
        _viewModel = App.GetService<MainWindowViewModel>() ?? throw new InvalidOperationException("MainWindowViewModel not found");

        // 设置DataContext
        //this.DataContext = _viewModel;

        // 绑定ViewModel事件
        _viewModel.NavigationRequested += OnNavigationRequested;
        _viewModel.BackRequested += OnBackRequested;
        _viewModel.WindowStateChangeRequested += OnWindowStateChangeRequested;

        // 配置窗口：自定义标题栏 + 居中
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets/logo.ico"));
        Title = _resourceLoader.GetString("AppDisplayName");
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        this.CenterOnScreen();
        // 导航到默认页面 (HomePage)
        ContentFrame.Navigate(typeof(HomePage));
        
        // 设置默认选中的导航项
        MainNavigationView.SelectedItem = MainPageNavItem;

        // 启动状态：显示加载进度 / 降级提示 / 失败信息
        App.Startup.Changed += OnStartupChanged;
        Closed += (_, _) => App.Startup.Changed -= OnStartupChanged;
        ApplyStartupStatus();

        // 初始化ViewModel
        _ = InitializeViewModelAsync();
    }

    private async Task InitializeViewModelAsync()
    {
        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "主窗口ViewModel初始化失败");
        }
    }

    private void OnStartupChanged(object? sender, EventArgs e)
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            ApplyStartupStatus();
        }
        else
        {
            DispatcherQueue.TryEnqueue(ApplyStartupStatus);
        }
    }

    private void ApplyStartupStatus()
    {
        var startup = App.Startup;
        StartupProgress.Visibility = startup.Phase == StartupPhase.Starting ? Visibility.Visible : Visibility.Collapsed;

        switch (startup.Phase)
        {
            case StartupPhase.Starting:
                StartupInfoBar.Severity = InfoBarSeverity.Informational;
                StartupInfoBar.Title = "正在启动";
                StartupInfoBar.Message = string.IsNullOrEmpty(startup.Message) ? "正在初始化服务…" : startup.Message;
                StartupInfoBar.IsOpen = true;
                StartupRetryButton.Visibility = Visibility.Collapsed;
                break;

            case StartupPhase.Ready:
                StartupInfoBar.IsOpen = false;
                break;

            case StartupPhase.Degraded:
                StartupInfoBar.Severity = InfoBarSeverity.Warning;
                StartupInfoBar.Title = "部分功能不可用";
                StartupInfoBar.Message = startup.Message;
                StartupInfoBar.IsOpen = true;
                StartupRetryButton.Visibility = Visibility.Visible;
                break;

            case StartupPhase.Failed:
                StartupInfoBar.Severity = InfoBarSeverity.Error;
                StartupInfoBar.Title = "启动失败";
                StartupInfoBar.Message = startup.Message;
                StartupInfoBar.IsOpen = true;
                StartupRetryButton.Visibility = Visibility.Visible;
                break;
        }
    }

    private async void StartupRetryButton_Click(object sender, RoutedEventArgs e)
    {
        StartupRetryButton.IsEnabled = false;
        try
        {
            App.Startup.Report(StartupPhase.Starting, "正在重试…");
            await App.RetryStartupAsync();
        }
        finally
        {
            StartupRetryButton.IsEnabled = true;
        }
    }

    private void OnNavigationRequested(object? sender, NavigationRequestedEventArgs e)
    {
        _logger?.LogInformation($"Navigating to: {e.PageTag}");

        Type? pageType = e.PageTag switch
        {
            "HomePage" => typeof(HomePage),
            "DeepSeekPage" => typeof(DeepSeekChatPage),
            "Settings" => typeof(SettingsPage),
            _ => null
        };

        if (pageType != null && ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType, e.Parameter);
        }
    }

    private void OnBackRequested(object? sender, EventArgs e)
    {
        if (ContentFrame.CanGoBack)
        {
            ContentFrame.GoBack();
        }
    }

    private void OnWindowStateChangeRequested(object? sender, WindowStateChangeEventArgs e)
    {
        switch (e.State)
        {
            case Verdure.Assistant.ViewModels.WindowState.Minimized:
                // 最小化窗口的逻辑
                break;
            case Verdure.Assistant.ViewModels.WindowState.Maximized:
                // 最大化窗口的逻辑
                break;
            case Verdure.Assistant.ViewModels.WindowState.Closed:
                _ = ((App)Application.Current).ShutdownAndCloseAsync();
                break;
        }
    }    
    
    private void MainNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        // 在UI层处理NavigationViewItem，然后将Tag传递给ViewModel
        if (args.SelectedItem is NavigationViewItem navItem && navItem.Tag is string pageTag)
        {
            _viewModel.OnNavigationSelectionChanged(pageTag);
        }
    }    

    private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        _viewModel.UpdateBackButtonState(ContentFrame.CanGoBack);
        
        // 根据导航的页面类型更新选中的导航项
        if (e.SourcePageType == typeof(HomePage))
        {
            MainNavigationView.SelectedItem = MainPageNavItem;
        }
        else if (e.SourcePageType == typeof(DeepSeekChatPage))
        {
            MainNavigationView.SelectedItem = DeepSeekNavItem;
        }
        else if (e.SourcePageType == typeof(SettingsPage))
        {
            // 对于设置页面，将选中项设置为null，这样设置按钮会保持高亮
            MainNavigationView.SelectedItem = null;
        }
    }
}
