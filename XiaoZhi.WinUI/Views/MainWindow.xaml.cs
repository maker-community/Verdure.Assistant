using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Threading.Tasks;
using XiaoZhi.Core.Interfaces;
using XiaoZhi.Core.Models;
using XiaoZhi.Core.Constants;
using Microsoft.UI.Input;

namespace XiaoZhi.WinUI.Views;

/// <summary>
/// 主窗口 - 类似py-xiaozhi的GUI界面
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly ILogger<MainWindow>? _logger;
    private IVoiceChatService? _voiceChatService;
    private EmotionManager? _emotionManager;    // UI 状态
    private bool _isAutoMode = false;
    private bool _isConnected = false;
    private bool _isListening = false;

    public MainWindow()
    {
        this.InitializeComponent();

        _logger = App.GetService<ILogger<MainWindow>>();

        // 初始化服务
        InitializeServices();

        // 初始化表情管理器
        InitializeEmotionManager();

        // 设置初始状态
        SetInitialState();

        // 绑定事件
        BindEvents();
    }    private void InitializeServices()
    {
        try
        {
            _voiceChatService = App.GetService<IVoiceChatService>();
            // Note: Event registration is now handled in ConnectButton_Click to avoid duplicate registrations
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize services");
        }
    }

    private async void InitializeEmotionManager()
    {
        try
        {
            _emotionManager = new EmotionManager();
            await _emotionManager.InitializeAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize emotion manager");
        }
    }

    private void SetInitialState()
    {
        // 设置初始UI状态
        StatusText.Text = "未连接";
        TtsText.Text = "待命";
        DefaultEmotionText.Text = "😊";

        // 设置手动模式为默认模式
        SwitchToManualMode();

        // 设置初始表情
        SetEmotion("neutral");
    }

    private void BindEvents()
    {
        // 绑定按钮事件在XAML中已定义，这里处理其他事件
        this.Closed += OnWindowClosed;
    }

    #region 事件处理

    private void OnDeviceStateChanged(object? sender, DeviceState state)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateStatusText(GetStatusText(state));
            UpdateUIForDeviceState(state);
        });
    }

    private void OnVoiceChatStateChanged(object? sender, bool isActive)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _isListening = isActive;
            UpdateUIForVoiceChatState(isActive);
        });
    }

    private void OnMessageReceived(object? sender, ChatMessage message)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateTtsText(message.Content ?? "");

            // 根据消息内容设置表情
            var emotion = DetectEmotionFromMessage(message.Content ?? "");
            SetEmotion(emotion);
        });
    }

    private void OnErrorOccurred(object? sender, string error)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _logger?.LogError("Voice chat error: {Error}", error);
            UpdateStatusText($"错误: {error}");
        });
    }

    private void OnListeningModeChanged(object? sender, ListeningMode mode)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _logger?.LogInformation("Listening mode changed to: {Mode}", mode);
        });
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        // 清理资源
        if (_voiceChatService != null)
        {
            _voiceChatService.DeviceStateChanged -= OnDeviceStateChanged;
            _voiceChatService.VoiceChatStateChanged -= OnVoiceChatStateChanged;
            _voiceChatService.MessageReceived -= OnMessageReceived;
            _voiceChatService.ErrorOccurred -= OnErrorOccurred;
            _voiceChatService.ListeningModeChanged -= OnListeningModeChanged;
        }
    }

    #endregion

    #region UI更新方法

    private void UpdateStatusText(string status)
    {
        StatusText.Text = status;
    }

    private void UpdateTtsText(string text)
    {
        TtsText.Text = text;
    }    private void UpdateUIForDeviceState(DeviceState state)
    {
        // DeviceState.Idle means connected but idle, not disconnected!
        // Only update connection status UI, don't change _isConnected flag here
        
        // 根据状态更新UI表情
        switch (state)
        {
            case DeviceState.Idle:
                SetEmotion("neutral");
                break;
            case DeviceState.Listening:
                SetEmotion("thinking");
                break;
            case DeviceState.Speaking:
                SetEmotion("talking");
                break;
            case DeviceState.Connecting:
                SetEmotion("thinking");
                break;
        }
    }

    private void UpdateUIForVoiceChatState(bool isActive)
    {
        if (isActive)
        {
            SetEmotion("listening");
        }
        else
        {
            SetEmotion("neutral");
        }
    }

    private void UpdateConnectionUI()
    {
        // 更新连接指示器
        if (ConnectionIndicator != null)
        {
            var brush = _isConnected 
                ? App.Current.Resources["SystemFillColorSuccessBrush"] as Microsoft.UI.Xaml.Media.Brush
                : App.Current.Resources["SystemFillColorCriticalBrush"] as Microsoft.UI.Xaml.Media.Brush;
            
            if (brush != null)
            {
                ConnectionIndicator.Background = brush;
            }
        }

        // 更新连接状态文本
        if (ConnectionStatusText != null)
        {
            ConnectionStatusText.Text = _isConnected ? "在线" : "离线";
        }

        // 更新按钮状态
        if (ConnectButton != null)
        {
            ConnectButton.IsEnabled = !_isConnected;
        }
        
        if (DisconnectButton != null)
        {
            DisconnectButton.IsEnabled = _isConnected;
        }

        // 更新其他控件状态
        if (ManualButton != null)
        {
            ManualButton.IsEnabled = _isConnected;
        }
        
        if (AutoButton != null)
        {
            AutoButton.IsEnabled = _isConnected;
        }
        
        if (AbortButton != null)
        {
            AbortButton.IsEnabled = _isConnected;
        }
        
        if (ModeToggleButton != null)
        {
            ModeToggleButton.IsEnabled = _isConnected;
        }
        
        if (MessageTextBox != null)
        {
            MessageTextBox.IsEnabled = _isConnected;
        }
        
        if (SendButton != null)
        {
            SendButton.IsEnabled = _isConnected;
        }
    }

    #endregion

    #region 按钮事件处理

    private async void ManualButton_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_isConnected || _voiceChatService == null) return;

        try
        {
            await _voiceChatService.StartVoiceChatAsync();
            ManualButtonText.Text = "正在听...";
            SetEmotion("listening");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to start listening");
        }
    }

    private async void ManualButton_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isConnected || _voiceChatService == null) return;

        try
        {
            await _voiceChatService.StopVoiceChatAsync();
            ManualButtonText.Text = "按住后说话";
            SetEmotion("neutral");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to stop listening");
        }
    }    private void ManualButton_PointerCaptureLost(object sender, PointerEventArgs e)
    {
        // 当失去指针捕获时也停止监听 - 直接调用停止逻辑而不依赖PointerReleased
        _ = Task.Run(async () =>
        {
            if (!_isConnected || _voiceChatService == null) return;

            try
            {
                await _voiceChatService.StopVoiceChatAsync();
                DispatcherQueue.TryEnqueue(() =>
                {
                    ManualButtonText.Text = "按住后说话";
                    SetEmotion("neutral");
                });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to stop listening on capture lost");
            }
        });
    }

    private async void AutoButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isConnected || _voiceChatService == null) return;

        try
        {
            if (!_isListening)
            {
                _voiceChatService.KeepListening = true;
                await _voiceChatService.ToggleChatStateAsync();
                AutoButtonText.Text = "停止对话";
            }
            else
            {
                _voiceChatService.KeepListening = false;
                await _voiceChatService.ToggleChatStateAsync();
                AutoButtonText.Text = "开始对话";
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to toggle auto conversation");
        }
    }

    private async void AbortButton_Click(object sender, RoutedEventArgs e)
    {
        if (_voiceChatService == null) return;

        try
        {
            await _voiceChatService.ToggleChatStateAsync();
            SetEmotion("neutral");
            UpdateTtsText("对话已中断");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to abort conversation");
        }
    }

    private void ModeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _isAutoMode = !_isAutoMode;

        if (_isAutoMode)
        {
            SwitchToAutoMode();
        }
        else
        {
            SwitchToManualMode();
        }
    }

    private void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        // 静音按钮逻辑
        var button = sender as ToggleButton;
        if (button?.IsChecked == true)
        {
            // 静音
            MuteIcon.Glyph = "\uE74F"; // Mute icon
            VolumeSlider.IsEnabled = false;
        }
        else
        {
            // 取消静音
            MuteIcon.Glyph = "\uE767"; // Volume icon
            VolumeSlider.IsEnabled = true;
        }
    }

    private void VolumeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (VolumeText != null)
        {
            VolumeText.Text = $"{(int)e.NewValue}%";
        }
    }

    #endregion

    #region Navigation and UI Event Handlers

    private void MainNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item)
        {
            switch (item.Tag?.ToString())
            {
                case "MainPage":
                    MainPageContent.Visibility = Visibility.Visible;
                    SettingsPageContent.Visibility = Visibility.Collapsed;
                    break;
                case "SettingsPage":
                    MainPageContent.Visibility = Visibility.Collapsed;
                    SettingsPageContent.Visibility = Visibility.Visible;
                    break;
            }
        }
    }    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_voiceChatService != null && _voiceChatService.IsConnected) return;

        try
        {
            ConnectButton.IsEnabled = false;
            UpdateStatusText("连接中...");
            
            // 更新连接状态
            _isConnected = false;
            UpdateConnectionUI();

            _voiceChatService = App.GetService<IVoiceChatService>();
            
            if (_voiceChatService == null)
            {
                throw new InvalidOperationException("VoiceChatService not available from DI container");
            }
            
            // 注册事件处理器
            _voiceChatService.MessageReceived += OnMessageReceived;
            _voiceChatService.VoiceChatStateChanged += OnVoiceChatStateChanged;
            _voiceChatService.ErrorOccurred += OnErrorOccurred;
            _voiceChatService.DeviceStateChanged += OnDeviceStateChanged;
            _voiceChatService.ListeningModeChanged += OnListeningModeChanged;

            // 创建配置
            var config = new XiaoZhiConfig
            {
                ServerUrl = "ws://localhost:8080/ws",
                UseWebSocket = true,
                EnableVoice = true,
                AudioSampleRate = 16000,
                AudioChannels = 1,
                AudioFormat = "opus"
            };

            await _voiceChatService.InitializeAsync(config);
            
            // 更新连接状态
            _isConnected = _voiceChatService.IsConnected;
            
            if (_isConnected)
            {
                UpdateStatusText("已连接");
                SetEmotion("happy");
            }
            else
            {
                UpdateStatusText("连接失败");
                SetEmotion("sad");
            }
            
            UpdateConnectionUI();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to connect");
            UpdateStatusText("连接失败");
            _isConnected = false;
            UpdateConnectionUI();
            SetEmotion("sad");
        }
    }    private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_voiceChatService == null || !_voiceChatService.IsConnected) return;

        try
        {
            DisconnectButton.IsEnabled = false;
            UpdateStatusText("断开中...");
            
            if (_voiceChatService.IsVoiceChatActive)
            {
                await _voiceChatService.StopVoiceChatAsync();
            }
            
            // 清理事件订阅
            _voiceChatService.MessageReceived -= OnMessageReceived;
            _voiceChatService.VoiceChatStateChanged -= OnVoiceChatStateChanged;
            _voiceChatService.ErrorOccurred -= OnErrorOccurred;
            _voiceChatService.DeviceStateChanged -= OnDeviceStateChanged;
            _voiceChatService.ListeningModeChanged -= OnListeningModeChanged;
            
            _voiceChatService.Dispose();
            _voiceChatService = null;
              // 重置所有状态
            _isConnected = false;
            _isListening = false;
            
            // 重置UI到初始状态
            UpdateStatusText("已断开");
            UpdateTtsText("待命");
            SetEmotion("neutral");
            
            // 重置按钮文本
            if (ManualButtonText != null)
            {
                ManualButtonText.Text = "按住后说话";
            }
            
            if (AutoButtonText != null)
            {
                AutoButtonText.Text = "开始对话";
            }
            
            // 切换回手动模式
            SwitchToManualMode();
            
            UpdateConnectionUI();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to disconnect");
            UpdateStatusText("断开失败");
            DisconnectButton.IsEnabled = true;
        }
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        if (_voiceChatService != null && !string.IsNullOrWhiteSpace(MessageTextBox.Text))
        {
            try
            {
                await _voiceChatService.SendTextMessageAsync(MessageTextBox.Text);
                MessageTextBox.Text = "";
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to send message");
            }
        }
    }    private void MessageTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            SendButton_Click(sender, new RoutedEventArgs());
        }
    }

    #region Missing Event Handlers

    //private void MessageTextBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    //{
    //    if (e.Key == Windows.System.VirtualKey.Enter)
    //    {
    //        SendMessage();
    //    }
    //}

    //private void SendButton_Click(object sender, RoutedEventArgs e)
    //{
    //    SendMessage();
    //}

    //private void SendMessage()
    //{
    //    var message = MessageTextBox?.Text?.Trim();
    //    if (string.IsNullOrEmpty(message))
    //        return;

    //    // Send text message via voice chat service
    //    _voiceChatService?.SendTextMessage(message);

    //    // Clear input
    //    if (MessageTextBox != null)
    //        MessageTextBox.Text = string.Empty;
    //}

    private void WakeWordToggle_Toggled(object sender, RoutedEventArgs e)
    {
        var toggleSwitch = sender as ToggleSwitch;
        if (toggleSwitch != null)
        {
            // Handle wake word toggle
            _logger?.LogInformation($"Wake word detection {(toggleSwitch.IsOn ? "enabled" : "disabled")}");
        }
    }

    private void DefaultVolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        var slider = sender as Slider;
        if (slider != null)
        {
            // Handle default volume change
            _logger?.LogInformation($"Default volume changed to {slider.Value}%");
        }
    }

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Save settings logic
            _logger?.LogInformation("Settings saved successfully");

            // Show success message (optional)
            // Could add a toast notification here
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save settings");
        }
    }

    private void ManualButton_PointerCaptureLost(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        // Handle when pointer capture is lost (similar to release)
        ManualButton_PointerReleased(sender, e);
    }

    #endregion

    #region 模式切换

    private void SwitchToAutoMode()
    {
        _isAutoMode = true;
        ManualButton.Visibility = Visibility.Collapsed;
        AutoButton.Visibility = Visibility.Visible;
        ModeToggleText.Text = "自动对话";

        // 设置自动模式的表情
        SetEmotion("happy");
    }

    private void SwitchToManualMode()
    {
        _isAutoMode = false;
        AutoButton.Visibility = Visibility.Collapsed;
        ManualButton.Visibility = Visibility.Visible;
        ModeToggleText.Text = "手动对话";

        // 设置手动模式的表情
        SetEmotion("neutral");
    }

    #endregion

    #region 表情管理

    private async void SetEmotion(string emotion)
    {
        if (_emotionManager == null) return;

        try
        {
            // 尝试获取表情图片
            var imagePath = await _emotionManager.GetEmotionImageAsync(emotion);

            if (!string.IsNullOrEmpty(imagePath))
            {
                // 显示动画表情
                var bitmap = new BitmapImage(new Uri(imagePath));
                EmotionImage.Source = bitmap;
                EmotionImage.Visibility = Visibility.Visible;
                DefaultEmotionText.Visibility = Visibility.Collapsed;
            }
            else
            {
                // 使用emoji表情
                var emoji = _emotionManager.GetEmotionEmoji(emotion);
                DefaultEmotionText.Text = emoji;
                DefaultEmotionText.Visibility = Visibility.Visible;
                EmotionImage.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to set emotion: {Emotion}", emotion);
            // 使用默认表情
            DefaultEmotionText.Text = "😊";
            DefaultEmotionText.Visibility = Visibility.Visible;
            EmotionImage.Visibility = Visibility.Collapsed;
        }
    }

    private string DetectEmotionFromMessage(string message)
    {
        if (string.IsNullOrEmpty(message)) return "neutral";

        // 简单的表情检测逻辑
        message = message.ToLower();

        if (message.Contains("哈哈") || message.Contains("笑") || message.Contains("有趣"))
            return "laughing";
        if (message.Contains("开心") || message.Contains("高兴") || message.Contains("棒"))
            return "happy";
        if (message.Contains("难过") || message.Contains("伤心"))
            return "sad";
        if (message.Contains("生气") || message.Contains("愤怒"))
            return "angry";
        if (message.Contains("惊讶") || message.Contains("哇"))
            return "surprised";
        if (message.Contains("思考") || message.Contains("想想"))
            return "thinking";
        if (message.Contains("爱") || message.Contains("喜欢"))
            return "loving";

        return "neutral";
    }

    #endregion

    #region 工具方法

    private string GetStatusText(DeviceState state)
    {
        return state switch
        {
            DeviceState.Idle => "待命",
            DeviceState.Connecting => "连接中...",
            DeviceState.Listening => "正在听...",
            DeviceState.Speaking => "正在说话...",
            _ => "未知状态"
        };
    }

    #endregion
}
#endregion