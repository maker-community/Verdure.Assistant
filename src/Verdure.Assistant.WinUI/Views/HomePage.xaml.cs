using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.IO;
using System.Threading.Tasks;
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
    }    private void BindViewModelEvents()
    {
        _viewModel.InterruptTriggered += OnInterruptTriggered;
        _viewModel.ScrollToBottomRequested += OnScrollToBottomRequested;
        _viewModel.ManualButtonStateChanged += OnManualButtonStateChanged;
        _viewModel.EmotionGifPathChanged += OnEmotionGifPathChanged;
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
        }    
    }

    private async void OnEmotionGifPathChanged(object? sender, EmotionGifPathEventArgs e)
    {
        try
        {
            await UpdateEmotionDisplayAsync(e.GifPath, e.EmotionName);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to update emotion display: {EmotionName}", e.EmotionName);
        }
    }    
    
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // 更新连接状态指示器颜色
        if (e.PropertyName == nameof(HomePageViewModel.IsConnected))
        {
            UpdateConnectionIndicator();
        }
        
        // 更新音乐播放按钮图标
        if (e.PropertyName == nameof(HomePageViewModel.MusicStatus))
        {
            UpdatePlayPauseButtonIcon();
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

    private void UpdatePlayPauseButtonIcon()
    {
        try
        {
            if (PlayPauseIcon != null)
            {
                // 根据音乐状态设置播放/暂停图标
                var glyph = _viewModel.MusicStatus == "播放中" 
                    ? "&#xE769;" // 暂停图标
                    : "&#xE768;"; // 播放图标
                
                PlayPauseIcon.Glyph = glyph;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error updating play/pause button icon");
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


    #region 音乐控制事件处理
    
    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // 根据当前播放状态决定执行播放还是暂停
            if (_viewModel.MusicStatus == "播放中")
            {
                _ = _viewModel.PauseMusicCommand.ExecuteAsync(null);
            }
            else
            {
                _ = _viewModel.ResumeMusicCommand.ExecuteAsync(null);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "播放/暂停按钮点击处理失败");
        }
    }

    private void MusicProgressSlider_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            if (sender is Slider slider)
            {
                // 当用户释放进度条时，跳转到指定位置
                _ = _viewModel.SeekMusicCommand.ExecuteAsync(slider.Value);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "音乐进度条跳转失败");
        }
    }    
    
    private void VolumeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        //try
        //{
        //    // 设置音乐音量
        //    _ = _viewModel.SetMusicVolumeCommand.ExecuteAsync(e.NewValue);
        //}
        //catch (Exception ex)
        //{
        //    _logger?.LogError(ex, "音量设置失败");
        //}
    }

    private void MusicSearchTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            PerformMusicSearch();
        }
    }

    private void SearchMusicButton_Click(object sender, RoutedEventArgs e)
    {
        PerformMusicSearch();
    }

    private void PerformMusicSearch()
    {
        try
        {
            var searchQuery = MusicSearchTextBox?.Text?.Trim();
            if (!string.IsNullOrEmpty(searchQuery))
            {
                _ = _viewModel.PlayMusicCommand.ExecuteAsync(searchQuery);
                // 清空搜索框
                if (MusicSearchTextBox != null)
                {
                    MusicSearchTextBox.Text = string.Empty;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "音乐搜索失败");
        }
    }

    #endregion

    #endregion

    #region 页面生命周期    
    private void HomePage_Unloaded(object sender, RoutedEventArgs e)
    {
        // 清理ViewModel
        _viewModel.Cleanup();        // 清理UI事件订阅
        _viewModel.InterruptTriggered -= OnInterruptTriggered;
        _viewModel.ScrollToBottomRequested -= OnScrollToBottomRequested;
        _viewModel.ManualButtonStateChanged -= OnManualButtonStateChanged;
        _viewModel.EmotionGifPathChanged -= OnEmotionGifPathChanged;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }    
    
    #endregion

    #region 表情动画处理    
    /// <summary>
    /// 更新表情显示，支持GIF动画切换，类似py-xiaozhi的表情切换效果
    /// </summary>
    private async Task UpdateEmotionDisplayAsync(string? gifPath, string? emotionName)
    {
        try
        {
            // 使用TaskCompletionSource确保在UI线程上执行
            var tcs = new TaskCompletionSource<bool>();
            
            this.DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(gifPath) && File.Exists(gifPath))
                    {
                        // 显示GIF动画
                        try
                        {
                            var bitmapImage = new BitmapImage();
                            bitmapImage.UriSource = new Uri(gifPath);
                            
                            //EmotionImage.Source = bitmapImage;
                            //EmotionImage.Visibility = Visibility.Visible;
                            //DefaultEmotionText.Visibility = Visibility.Collapsed;
                            
                            _logger?.LogDebug($"Switched to GIF emotion: {emotionName} -> {gifPath}");
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "Failed to load GIF emotion: {GifPath}", gifPath);
                            
                            // 回退到文本显示
                            //EmotionImage.Visibility = Visibility.Collapsed;
                            //DefaultEmotionText.Visibility = Visibility.Visible;
                        }
                    }
                    else
                    {
                        // 显示文本表情
                        //EmotionImage.Visibility = Visibility.Collapsed;
                        //DefaultEmotionText.Visibility = Visibility.Visible;
                        
                        _logger?.LogDebug($"Switched to text emotion: {emotionName}");
                    }
                    
                    tcs.SetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
            
            await tcs.Task;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in UpdateEmotionDisplayAsync");
        }
    }

    #endregion
}