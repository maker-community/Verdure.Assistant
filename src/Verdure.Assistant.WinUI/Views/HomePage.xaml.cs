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
using Verdure.Assistant.WinUI.Services;

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

        // 订阅新的渲染器事件
        BindRendererEvents();

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
    
    private void BindRendererEvents()
    {
        // 订阅GIF渲染器事件
        WinUIGifEmotionRenderer.GifRenderRequested += OnGifRenderRequested;
        WinUIGifEmotionRenderer.GifRenderStopped += OnGifRenderStopped;
        
        // 订阅Emoji渲染器事件
        WinUIEmojiEmotionRenderer.EmojiRenderRequested += OnEmojiRenderRequested;
        WinUIEmojiEmotionRenderer.EmojiRenderStopped += OnEmojiRenderStopped;
    }    private void BindViewModelEvents()
    {
        _viewModel.InterruptTriggered += OnInterruptTriggered;
        _viewModel.ScrollToBottomRequested += OnScrollToBottomRequested;
        _viewModel.ManualButtonStateChanged += OnManualButtonStateChanged;
        _viewModel.EmotionGifPathChanged += OnEmotionGifPathChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        
        // 绑定情感状态变化事件以触发动画
        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(HomePageViewModel.CurrentEmotion))
            {
                this.DispatcherQueue.TryEnqueue(() => TriggerEmotionAnimation());
            }
        };
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
        // 滚动到底部 - 由于移除了消息面板，这个方法现在为空
        // 将来如果需要其他滚动操作可以在这里实现
        this.DispatcherQueue.TryEnqueue(() =>
        {
            // MessagesScrollViewer.ChangeView(null, MessagesScrollViewer.ScrollableHeight, null);
            _logger?.LogDebug("Scroll to bottom requested - currently disabled");
        });      
    }    
    
    private void OnManualButtonStateChanged(object? sender, ManualButtonStateEventArgs e)
    {
        // 手动按钮已被移除，这个方法现在为空
        // 如果将来需要类似功能可以在这里重新实现
        /*
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
        */
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
        // 在UI线程上执行
        this.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                // 更新连接状态指示器颜色
                if (e.PropertyName == nameof(HomePageViewModel.IsConnected) || 
                    e.PropertyName == nameof(HomePageViewModel.ConnectionStatusText))
                {
                    UpdateConnectionIndicator();
                    _logger?.LogDebug("Connection indicator updated for property: {PropertyName}", e.PropertyName);
                }
                
                // 更新音乐播放按钮图标
                if (e.PropertyName == nameof(HomePageViewModel.MusicStatus))
                {
                    UpdatePlayPauseButtonIcon();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error handling property change for: {PropertyName}", e.PropertyName);
            }
        });
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
        // 手动按钮已被移除，此方法现在为空
        /*
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
        */
    }

    private void SetManualButtonRecordingVisualState()
    {
        // 手动按钮已被移除，此方法现在为空
        /*
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
        */
    }

    private void SetManualButtonProcessingVisualState()
    {
        // 手动按钮已被移除，此方法现在为空
        /*
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
        */
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
        // 音乐搜索功能已被移除，此方法现在为空
        /*
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            PerformMusicSearch();
        }
        */
    }

    private void SearchMusicButton_Click(object sender, RoutedEventArgs e)
    {
        // 音乐搜索功能已被移除，此方法现在为空
        // PerformMusicSearch();
    }

    private void PerformMusicSearch()
    {
        // 音乐搜索功能已被移除，此方法现在为空
        /*
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
        */
    }

    #endregion

    #endregion

    #region 页面生命周期    
    private void HomePage_Unloaded(object sender, RoutedEventArgs e)
    {
        // 清理ViewModel
        _viewModel.Cleanup();
        
        // 清理UI事件订阅
        _viewModel.InterruptTriggered -= OnInterruptTriggered;
        _viewModel.ScrollToBottomRequested -= OnScrollToBottomRequested;
        _viewModel.ManualButtonStateChanged -= OnManualButtonStateChanged;
        _viewModel.EmotionGifPathChanged -= OnEmotionGifPathChanged;
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        
        // 清理静态渲染器事件订阅（修复内存泄漏）
        WinUIGifEmotionRenderer.GifRenderRequested -= OnGifRenderRequested;
        WinUIGifEmotionRenderer.GifRenderStopped -= OnGifRenderStopped;
        WinUIEmojiEmotionRenderer.EmojiRenderRequested -= OnEmojiRenderRequested;
        WinUIEmojiEmotionRenderer.EmojiRenderStopped -= OnEmojiRenderStopped;
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

    #region 新的渲染器事件处理

    private void OnGifRenderRequested(object? sender, GifRenderEventArgs e)
    {
        this.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                _logger?.LogDebug($"Displaying GIF emotion: {e.EmotionType} -> {e.AssetPath}");
                
                // 这里需要根据实际的XAML控件来更新UI
                // 假设有一个名为EmotionImage的Image控件
                if (EmotionImage != null)
                {
                    EmotionImage.Source = e.GifSource;
                    EmotionImage.Visibility = Visibility.Visible;
                }
                
                // 隐藏文本表情
                if (DefaultEmotionText != null)
                {
                    DefaultEmotionText.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to display GIF emotion: {EmotionType}", e.EmotionType);
            }
        });
    }

    private void OnGifRenderStopped(object? sender, EventArgs e)
    {
        this.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                _logger?.LogDebug("Stopping GIF emotion display");
                
                // 隐藏GIF显示
                if (EmotionImage != null)
                {
                    EmotionImage.Visibility = Visibility.Collapsed;
                    EmotionImage.Source = null;
                }
                
                // 显示默认文本表情
                if (DefaultEmotionText != null)
                {
                    DefaultEmotionText.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to stop GIF emotion display");
            }
        });
    }

    private void OnEmojiRenderRequested(object? sender, EmojiRenderEventArgs e)
    {
        this.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                _logger?.LogDebug($"Displaying Emoji emotion: {e.EmotionType} -> {e.EmojiText}");
                
                // 隐藏GIF显示
                if (EmotionImage != null)
                {
                    EmotionImage.Visibility = Visibility.Collapsed;
                    EmotionImage.Source = null;
                }
                
                // 显示表情符号
                if (DefaultEmotionText != null)
                {
                    DefaultEmotionText.Text = e.EmojiText;
                    DefaultEmotionText.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to display Emoji emotion: {EmotionType}", e.EmotionType);
            }
        });
    }

    private void OnEmojiRenderStopped(object? sender, EventArgs e)
    {
        this.DispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                _logger?.LogDebug("Stopping Emoji emotion display");
                
                // 恢复默认表情
                if (DefaultEmotionText != null)
                {
                    DefaultEmotionText.Text = "😊";
                    DefaultEmotionText.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to stop Emoji emotion display");
            }
        });
    }

    #endregion

    #region 表情测试按钮事件处理

    private async void TestHappyButton_Click(object sender, RoutedEventArgs e)
    {
        await TestEmotionAsync("happy");
        await ViewModel.UpdateEmotionDisplayAsync("happy");
    }

    private async void TestSadButton_Click(object sender, RoutedEventArgs e)
    {
        await TestEmotionAsync("sad");
        await ViewModel.UpdateEmotionDisplayAsync("sad");
    }

    private async void TestAngryButton_Click(object sender, RoutedEventArgs e)
    {
        await TestEmotionAsync("angry");
        await ViewModel.UpdateEmotionDisplayAsync("angry");
    }

    private async void TestNeutralButton_Click(object sender, RoutedEventArgs e)
    {
        await TestEmotionAsync("neutral");
        await ViewModel.UpdateEmotionDisplayAsync("neutral");
    }

    private async Task TestEmotionAsync(string emotionType)
    {
        try
        {
            _logger?.LogInformation($"测试播放表情: {emotionType}");
            
            // 简单的表情映射
            string emoji = emotionType switch
            {
                "happy" => "😊",
                "sad" => "😢", 
                "angry" => "😠",
                "neutral" => "😐",
                _ => "🤔"
            };
            
            // 在UI线程上更新表情
            this.DispatcherQueue.TryEnqueue(() =>
            {
                try
                {
                    // 更新ViewModel的CurrentEmotion属性
                    _viewModel.CurrentEmotion = emoji;
                    _logger?.LogInformation($"成功显示表情: {emotionType} -> {emoji}");
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "更新表情显示失败");
                }
            });
            
            // 模拟表情持续2秒，然后恢复默认
            //await Task.Delay(2000);
            
            this.DispatcherQueue.TryEnqueue(() =>
            {
                _viewModel.CurrentEmotion = "😊";
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, $"播放表情失败: {emotionType}");
        }
    }

    #region 情感动画控制方法

    /// <summary>
    /// 触发情感动画
    /// </summary>
    private void TriggerEmotionAnimation()
    {
        try
        {
            // 获取动画资源
            var pulseAnimation = Resources["EmotionPulseAnimation"] as Microsoft.UI.Xaml.Media.Animation.Storyboard;
            var bounceAnimation = Resources["EmotionBounceAnimation"] as Microsoft.UI.Xaml.Media.Animation.Storyboard;

            // 停止当前动画
            pulseAnimation?.Stop();
            bounceAnimation?.Stop();

            // 根据当前情感状态选择动画
            var currentEmotion = _viewModel.CurrentEmotion;
            var emotionStatus = _viewModel.EmotionStatusText;

            if (emotionStatus.Contains("聆听") || emotionStatus.Contains("交流"))
            {
                // 活跃状态使用脉冲动画
                pulseAnimation?.Begin();
            }
            else if (!emotionStatus.Contains("待机") && !emotionStatus.Contains("离线"))
            {
                // 状态变化时使用弹跳动画
                bounceAnimation?.Begin();
            }
            // 待机状态不播放动画

            _logger?.LogDebug("情感动画触发: {Emotion} - {Status}", currentEmotion, emotionStatus);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "触发情感动画失败");
        }
    }

    /// <summary>
    /// 停止所有情感动画
    /// </summary>
    private void StopEmotionAnimations()
    {
        try
        {
            var pulseAnimation = Resources["EmotionPulseAnimation"] as Microsoft.UI.Xaml.Media.Animation.Storyboard;
            var bounceAnimation = Resources["EmotionBounceAnimation"] as Microsoft.UI.Xaml.Media.Animation.Storyboard;

            pulseAnimation?.Stop();
            bounceAnimation?.Stop();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "停止情感动画失败");
        }
    }

    #endregion

    #endregion

    #endregion
}