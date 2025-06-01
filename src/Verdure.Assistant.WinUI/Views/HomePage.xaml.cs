using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Services;
using Verdure.Assistant.ViewModels;

namespace Verdure.Assistant.WinUI.Views;

/// <summary>
/// 首页 - 语音对话界面
/// </summary>
public sealed partial class HomePage : Page
{
    private readonly ILogger<HomePage>? _logger;
    private readonly HomePageViewModel _viewModel;

    // Expose ViewModel for x:Bind
    public HomePageViewModel ViewModel => _viewModel;

    public HomePage()
    {
        InitializeComponent();        
        try
        {
            _logger = App.GetService<ILogger<HomePage>>();
            _viewModel = App.GetService<HomePageViewModel>() ?? throw new InvalidOperationException("HomePageViewModel not found");
            
            // 设置UI调度器以确保线程安全的UI更新
            var uiDispatcher = App.GetService<IUIDispatcher>();
            if (uiDispatcher != null)
            {
                _viewModel.SetUIDispatcher(uiDispatcher);
            }
        }
        catch (Exception ex)
        {
            // 如果服务获取失败，继续初始化但记录错误
            System.Diagnostics.Debug.WriteLine($"Failed to get services: {ex.Message}");
            throw;
        }

        // 设置DataContext
        this.DataContext = _viewModel;        
        // 绑定ViewModel事件
        BindViewModelEvents();

        // 初始化ViewModel
        _ = _viewModel.InitializeAsync();

        // 页面加载时初始化UI状态
        this.Loaded += OnPageLoaded;
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        // 初始化连接指示器状态
        UpdateConnectionIndicator();
    }

    private void BindViewModelEvents()
    {
        _viewModel.InterruptTriggered += OnInterruptTriggered;
        _viewModel.ScrollToBottomRequested += OnScrollToBottomRequested;
        _viewModel.ManualButtonStateChanged += OnManualButtonStateChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    #region ViewModel事件处理

    private async void OnInterruptTriggered(object? sender, InterruptEventArgs e)
    {
        try
        {
            await _viewModel.HandleInterruptAsync(e.Reason, e.Description);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to handle interrupt in UI thread");
        }
    }

    private void OnScrollToBottomRequested(object? sender, EventArgs e)
    {
        // 滚动到底部
        this.DispatcherQueue.TryEnqueue(() =>
        {
            MessagesScrollViewer.ChangeView(null, MessagesScrollViewer.ScrollableHeight, null);
        });      
    }

    private void OnManualButtonStateChanged(object? sender, ManualButtonStateEventArgs e)
    {
        switch (e.State)
        {
            case ManualButtonState.Normal:
                RestoreManualButtonVisualState();
                break;
            case ManualButtonState.Recording:
                SetManualButtonRecordingVisualState();
                break;
            case ManualButtonState.Processing:
                SetManualButtonProcessingVisualState();
                break;
        }    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // 更新连接状态指示器颜色
        if (e.PropertyName == nameof(HomePageViewModel.IsConnected))
        {
            UpdateConnectionIndicator();
        }
    }

    #endregion    
    #region UI状态更新辅助方法

    private void UpdateConnectionIndicator()
    {
        try
        {
            if (ConnectionIndicator != null)
            {
                // 根据连接状态设置指示器颜色
                var resourceKey = _viewModel.IsConnected
                    ? "SystemFillColorSuccessBrush"  // 绿色 - 已连接
                    : "SystemFillColorCriticalBrush"; // 红色 - 未连接

                if (Application.Current.Resources.TryGetValue(resourceKey, out var brush))
                {
                    ConnectionIndicator.Background = brush as Microsoft.UI.Xaml.Media.Brush;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error updating connection indicator");
        }
    }

    private void RestoreManualButtonVisualState()
    {
        try
        {
            if (ManualButton != null)
            {
                ManualButton.IsEnabled = true;
                ManualButton.Opacity = 1.0;
                ManualButton.ClearValue(BackgroundProperty);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error restoring manual button visual state");
        }
    }

    private void SetManualButtonRecordingVisualState()
    {
        try
        {
            if (ManualButton != null)
            {
                ManualButton.IsEnabled = true;
                ManualButton.Opacity = 0.8;
                ManualButton.Background = Application.Current.Resources["SystemAccentColorBrush"] as Microsoft.UI.Xaml.Media.Brush;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error setting manual button recording visual state");
        }
    }

    private void SetManualButtonProcessingVisualState()
    {
        try
        {
            if (ManualButton != null)
            {
                ManualButton.IsEnabled = false;
                ManualButton.Opacity = 0.6;
                ManualButton.Background = Application.Current.Resources["SystemFillColorCautionBrush"] as Microsoft.UI.Xaml.Media.Brush;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error setting manual button processing visual state");
        }
    }

    #endregion

    #region UI事件处理 - 委托给ViewModel

    private async void ManualButton_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var button = (Button)sender;
        button.CapturePointer(e.Pointer);
        await _viewModel.StartManualRecordingCommand.ExecuteAsync(null);
    }

    private async void ManualButton_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        await _viewModel.StopManualRecordingCommand.ExecuteAsync(null);
    }

    private void ManualButton_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        // 当指针捕获丢失时，也要停止录音
        _ = _viewModel.StopManualRecordingCommand.ExecuteAsync(null);
    }

    private void MessageTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            _ = _viewModel.SendMessageCommand.ExecuteAsync(null);
        }
    }

    #endregion

    #region 页面生命周期

    private void HomePage_Unloaded(object sender, RoutedEventArgs e)
    {
        // 清理ViewModel
        _viewModel.Cleanup();        // 清理UI事件订阅
        _viewModel.InterruptTriggered -= OnInterruptTriggered;
        _viewModel.ScrollToBottomRequested -= OnScrollToBottomRequested;
        _viewModel.ManualButtonStateChanged -= OnManualButtonStateChanged;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }    
    #endregion
}