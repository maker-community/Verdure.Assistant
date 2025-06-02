using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Verdure.Assistant.ViewModels;

/// <summary>
/// 主窗口ViewModel - 导航和窗口管理
/// </summary>
public partial class MainWindowViewModel : ViewModelBase
{
    #region 可观察属性

    [ObservableProperty]
    private string _title = "绿荫助手";

    [ObservableProperty]
    private bool _isNavigationPaneOpen = true;

    [ObservableProperty]
    private object? _selectedNavigationItem;

    [ObservableProperty]
    private string _currentPageType = "HomePage";

    [ObservableProperty]
    private bool _isBackEnabled = false;

    #endregion

    #region 导航项

    public List<NavigationItem> NavigationItems { get; } = new()
    {
        new NavigationItem
        {
            Tag = "HomePage",
            Title = "主页",
            Icon = "\uE80F", // Home icon
            ToolTip = "语音对话界面"
        },
        new NavigationItem
        {
            Tag = "SettingsPage",
            Title = "设置",
            Icon = "\uE713", // Settings icon
            ToolTip = "应用程序设置"
        }
    };

    #endregion    
    public MainWindowViewModel(ILogger<MainWindowViewModel> logger) : base(logger)
    {
        // 初始化时不设置默认选中项，让NavigationView自己处理
    }

    public override Task InitializeAsync()
    {
        _logger?.LogInformation("MainWindow ViewModel initialized");
        return base.InitializeAsync();
    }

    #region 命令    [RelayCommand]
    private void NavigateToPage(string pageTag)
    {
        if (string.IsNullOrEmpty(pageTag) || CurrentPageType == pageTag)
            return;

        try
        {
            CurrentPageType = pageTag;
            
            _logger?.LogInformation("Navigating to page: {PageTag}", pageTag);
            
            // 触发导航事件
            NavigationRequested?.Invoke(this, new NavigationRequestedEventArgs
            {
                PageTag = pageTag,
                Parameter = null
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to navigate to page: {PageTag}", pageTag);
        }
    }

    [RelayCommand]
    private void ToggleNavigationPane()
    {
        IsNavigationPaneOpen = !IsNavigationPaneOpen;
    }

    [RelayCommand]
    private void GoBack()
    {
        try
        {
            BackRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to go back");
        }
    }

    [RelayCommand]
    private void MinimizeWindow()
    {
        try
        {
            WindowStateChangeRequested?.Invoke(this, new WindowStateChangeEventArgs
            {
                State = WindowState.Minimized
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to minimize window");
        }
    }

    [RelayCommand]
    private void MaximizeWindow()
    {
        try
        {
            WindowStateChangeRequested?.Invoke(this, new WindowStateChangeEventArgs
            {
                State = WindowState.Maximized
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to maximize window");
        }
    }

    [RelayCommand]
    private void CloseWindow()
    {
        try
        {
            WindowStateChangeRequested?.Invoke(this, new WindowStateChangeEventArgs
            {
                State = WindowState.Closed
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to close window");
        }
    }

    #endregion    
    #region 导航处理

    public void OnNavigationSelectionChanged(object? selectedItem)
    {
        // ViewModel应该保持平台无关，所以我们通过Tag来识别导航项
        // 在UI层处理具体的控件类型转换
        if (selectedItem != null)
        {
            // 如果传入的是字符串Tag，直接使用
            if (selectedItem is string pageTag)
            {
                NavigateToPage(pageTag);
            }
            // 如果是自定义NavigationItem，使用其Tag
            else if (selectedItem is NavigationItem customNavItem)
            {
                NavigateToPage(customNavItem.Tag);
            }
        }
    }

    public void UpdateBackButtonState(bool canGoBack)
    {
        IsBackEnabled = canGoBack;
    }

    #endregion

    #region 事件

    public event EventHandler<NavigationRequestedEventArgs>? NavigationRequested;
    public event EventHandler? BackRequested;
    public event EventHandler<WindowStateChangeEventArgs>? WindowStateChangeRequested;

    #endregion
}

/// <summary>
/// 导航项
/// </summary>
public class NavigationItem
{
    public string Tag { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public string ToolTip { get; set; } = string.Empty;
}

/// <summary>
/// 导航请求事件参数
/// </summary>
public class NavigationRequestedEventArgs : EventArgs
{
    public string PageTag { get; set; } = string.Empty;
    public object? Parameter { get; set; }
}

/// <summary>
/// 窗口状态
/// </summary>
public enum WindowState
{
    Normal,
    Minimized,
    Maximized,
    Closed
}

/// <summary>
/// 窗口状态改变事件参数
/// </summary>
public class WindowStateChangeEventArgs : EventArgs
{
    public WindowState State { get; set; }
}
