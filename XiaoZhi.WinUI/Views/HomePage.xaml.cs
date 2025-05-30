using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.Resources;
using XiaoZhi.Core.Constants;
using XiaoZhi.Core.Interfaces;
using XiaoZhi.Core.Models;
using XiaoZhi.Core.Services;

namespace XiaoZhi.WinUI.Views;

/// <summary>
/// 首页 - 语音对话界面
/// </summary>
public sealed partial class HomePage : Page
{
    private readonly ILogger<HomePage>? _logger;
    private readonly IVoiceChatService? _voiceChatService;
    private readonly EmotionManager? _emotionManager;
    private InterruptManager? _interruptManager;
    private readonly ResourceLoader _resourceLoader;
    private bool _isConnected = false;
    private bool _isListening = false;
    private bool _isAutoMode = false;    
    public HomePage()
    {
        this.InitializeComponent();
        _resourceLoader = new();
        
        try
        {
            _logger = App.GetService<ILogger<HomePage>>();
            _voiceChatService = App.GetService<IVoiceChatService>();
            _emotionManager = App.GetService<EmotionManager>();
            _interruptManager = App.GetService<InterruptManager>();
        }
        catch (Exception ex)
        {
            // 如果服务获取失败，继续初始化但记录错误
            System.Diagnostics.Debug.WriteLine($"Failed to get services: {ex.Message}");
        }

        InitializeUI();
        BindEvents();
    }    private void InitializeUI()
    {
        // 初始化状态文本
        StatusText.Text = _resourceLoader.GetString("Status_Disconnected");
        ConnectionStatusText.Text = _resourceLoader.GetString("ConnectionStatus_Offline");
        TtsText.Text = _resourceLoader.GetString("TtsText_Standby");
        DefaultEmotionText.Text = "😊";

        // 设置初始音量
        VolumeSlider.Value = 80;
        UpdateVolumeText(80);

        // 设置手动模式为默认模式
        SwitchToManualMode();

        // 设置初始表情
        SetEmotion("neutral");
    }
    private async void BindEvents()
    {
        // 页面事件
        this.Unloaded += HomePage_Unloaded;

        // 绑定语音服务事件
        if (_voiceChatService != null)
        {
            _voiceChatService.DeviceStateChanged += OnDeviceStateChanged;
            _voiceChatService.VoiceChatStateChanged += OnVoiceChatStateChanged;
            _voiceChatService.MessageReceived += OnMessageReceived;
            _voiceChatService.ErrorOccurred += OnErrorOccurred;
        }

        // 初始化和绑定InterruptManager事件
        if (_interruptManager != null)
        {
            try
            {
                await _interruptManager.InitializeAsync();
                _interruptManager.InterruptTriggered += OnInterruptTriggered;
                _logger?.LogInformation("InterruptManager initialized successfully");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to initialize InterruptManager");
            }
        }
    }
    private void OnInterruptTriggered(object? sender, InterruptEventArgs e)
    {
        try
        {
            _logger?.LogInformation("Interrupt triggered: {Reason} - {Description}", e.Reason, e.Description);

            this.DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    await HandleInterrupt(e.Reason, e.Description);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to handle interrupt in UI thread");
                    AddMessage($"处理打断时出错: {ex.Message}", true);
                }
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to process interrupt event");
        }
    }

    private async Task HandleInterrupt(AbortReason reason, string description)
    {
        if (_voiceChatService == null)
        {
            _logger?.LogWarning("VoiceChatService is null, cannot handle interrupt");
            return;
        }

        var currentState = _voiceChatService.CurrentState;
        _logger?.LogInformation("Handling interrupt {Reason} in state {State}", reason, currentState);

        // Add user feedback message
        AddMessage($"[打断] {description}", false);

        switch (reason)
        {
            case AbortReason.VoiceInterruption:
                // VAD detected user speech during AI response - abort speaking
                if (currentState == DeviceState.Speaking)
                {
                    await _voiceChatService.StopVoiceChatAsync();
                    _logger?.LogInformation("Voice chat stopped due to voice interruption");

                    // Auto-restart listening if in auto mode (like py-xiaozhi)
                    if (_voiceChatService.KeepListening)
                    {
                        // Brief delay then restart listening (matches py-xiaozhi pattern)
                        await Task.Delay(200);
                        if (_voiceChatService.CurrentState == DeviceState.Idle)
                        {
                            await _voiceChatService.StartVoiceChatAsync();
                            _logger?.LogInformation("Auto-restarted listening after voice interrupt");
                        }
                    }
                }
                break;

            case AbortReason.KeyboardInterruption:
            case AbortReason.UserInterruption:
                // F3 key or manual interrupt - handle based on current state
                switch (currentState)
                {
                    case DeviceState.Speaking:
                        // Abort current speaking
                        await _voiceChatService.StopVoiceChatAsync();
                        _logger?.LogInformation("Voice chat stopped due to user interrupt");
                        break;

                    case DeviceState.Listening:
                        // Stop listening
                        await _voiceChatService.StopVoiceChatAsync();
                        _logger?.LogInformation("Listening stopped due to user interrupt");
                        break;

                    case DeviceState.Idle:
                        // If in auto mode, this might toggle chat state (like py-xiaozhi F3 behavior)
                        if (_isAutoMode)
                        {
                            await _voiceChatService.ToggleChatStateAsync();
                            _logger?.LogInformation("Toggled chat state due to user interrupt in idle");
                        }
                        break;
                }
                break;

            case AbortReason.WakeWordDetected:
                // Wake word detected - handle like py-xiaozhi wake word behavior
                switch (currentState)
                {
                    case DeviceState.Speaking:
                        // Abort speaking and start listening
                        await _voiceChatService.StopVoiceChatAsync();
                        await Task.Delay(100); // Brief pause
                        await _voiceChatService.StartVoiceChatAsync();
                        _logger?.LogInformation("Switched from speaking to listening due to wake word");
                        break;

                    case DeviceState.Idle:
                        // Start listening
                        await _voiceChatService.StartVoiceChatAsync();
                        _logger?.LogInformation("Started listening due to wake word");
                        break;
                }
                break;

            default:
                _logger?.LogWarning("Unhandled interrupt reason: {Reason}", reason);
                break;
        }
    }

    #region 事件处理

    private void OnDeviceStateChanged(object? sender, DeviceState state)
    {
        this.DispatcherQueue.TryEnqueue(() =>
        {
            // Don't update connection state based on device state!
            // DeviceState.Idle means "connected but idle", not "disconnected"
            // Connection state should only be managed by actual connection/disconnection events

            switch (state)
            {
                case DeviceState.Listening:
                    StatusText.Text = _resourceLoader.GetString("Status_Listening");
                    // Update emotion/visual indicators but don't touch connection state
                    SetEmotion("listening");
                    break;
                case DeviceState.Speaking:
                    StatusText.Text = _resourceLoader.GetString("Status_Playing");
                    // Update emotion/visual indicators but don't touch connection state
                    SetEmotion("speaking");
                    break;
                case DeviceState.Connecting:
                    StatusText.Text = _resourceLoader.GetString("Status_Connecting");
                    // Update emotion/visual indicators but don't touch connection state
                    SetEmotion("thinking");
                    break;
                case DeviceState.Idle:
                default:
                    StatusText.Text = _resourceLoader.GetString("Status_Standby");
                    // Update emotion/visual indicators but don't touch connection state
                    SetEmotion("neutral");
                    break;
            }
        });
    }
    private void OnVoiceChatStateChanged(object? sender, bool isActive)
    {
        this.DispatcherQueue.TryEnqueue(() =>
        {
            _isListening = isActive;
            UpdateUIForVoiceChatState(isActive);

            // Update auto button text when in auto mode
            if (_isAutoMode && AutoButtonText != null)
            {
                if (_voiceChatService?.KeepListening == true && _isListening)
                {
                    AutoButtonText.Text = _resourceLoader.GetString("AutoButtonText_Stop");
                }
                else if (_voiceChatService?.KeepListening == false || !_isListening)
                {
                    AutoButtonText.Text = _resourceLoader.GetString("AutoButtonText_Start");
                }
            }
        });
    }

    private void OnMessageReceived(object? sender, ChatMessage message)
    {
        this.DispatcherQueue.TryEnqueue(() =>
        {
            var displayText = message.Role switch
            {
                "user" => $"用户: {message.Content}",
                "assistant" => $"小智: {message.Content}",
                _ => message.Content
            };

            AddMessage(displayText, false);

            // 如果是助手消息，更新TTS文本
            if (message.Role == "assistant")
            {
                TtsText.Text = message.Content;
            }
        });
    }

    private void OnErrorOccurred(object? sender, string error)
    {
        this.DispatcherQueue.TryEnqueue(() =>
        {
            AddMessage($"错误: {error}", true);
            _logger?.LogError("Voice chat error: {Error}", error);
        });
    }

    #endregion

    #region UI更新方法

    private void UpdateConnectionState(bool connected)
    {
        _isConnected = connected;

        // 更新连接状态指示器
        if (ConnectionIndicator != null)
        {
            var brush = connected ?
                Application.Current.Resources["SystemFillColorSuccessBrush"] as Microsoft.UI.Xaml.Media.Brush :
                Application.Current.Resources["SystemFillColorCriticalBrush"] as Microsoft.UI.Xaml.Media.Brush;
            if (brush != null)
            {
                ConnectionIndicator.Background = brush;
            }
        }

        // 更新连接状态文本
        if (ConnectionStatusText != null)
        {
            ConnectionStatusText.Text = connected ? "在线" : "离线";
        }

        // 更新按钮状态
        if (ConnectButton != null)
        {
            ConnectButton.IsEnabled = !connected;
        }

        if (DisconnectButton != null)
        {
            DisconnectButton.IsEnabled = connected;
        }

        // 更新其他控件状态
        if (ManualButton != null)
        {
            ManualButton.IsEnabled = connected;
        }

        if (AutoButton != null)
        {
            AutoButton.IsEnabled = connected;
        }

        if (AbortButton != null)
        {
            AbortButton.IsEnabled = connected;
        }

        if (ModeToggleButton != null)
        {
            ModeToggleButton.IsEnabled = connected;
        }

        if (MessageTextBox != null)
        {
            MessageTextBox.IsEnabled = connected;
        }

        if (SendButton != null)
        {
            SendButton.IsEnabled = connected;
        }
    }

    private void UpdateUIForVoiceChatState(bool isActive)
    {
        if (isActive)
        {
            ShowMicrophoneVisualizer(true);
            SetEmotion("listening");
        }
        else
        {
            ShowMicrophoneVisualizer(false);
            SetEmotion("neutral");
        }
    }

    private void SwitchToManualMode()
    {
        _isAutoMode = false;
        if (ManualButton != null) ManualButton.Visibility = Visibility.Visible;
        if (AutoButton != null) AutoButton.Visibility = Visibility.Collapsed;
        if (ModeToggleText != null) ModeToggleText.Text = _resourceLoader.GetString("ModeToggleText_Manual");
    }
    private void SwitchToAutoMode()
    {
        _isAutoMode = true;
        if (ManualButton != null) ManualButton.Visibility = Visibility.Collapsed;
        if (AutoButton != null) AutoButton.Visibility = Visibility.Visible;
        if (ModeToggleText != null) ModeToggleText.Text = _resourceLoader.GetString("ModeToggleText_Auto");

        // Update button text based on current listening state and auto mode
        if (AutoButtonText != null)
        {
            if (_voiceChatService?.KeepListening == true && _isListening)            {
                AutoButtonText.Text = _resourceLoader.GetString("AutoButtonText_Stop");
            }
            else
            {
                AutoButtonText.Text = _resourceLoader.GetString("AutoButtonText_Start");
            }
        }
    }

    private void UpdateModeUI(bool isAutoMode)
    {
        _isAutoMode = isAutoMode;

        if (isAutoMode)
        {
            SwitchToAutoMode();
        }
        else
        {
            SwitchToManualMode();
        }
    }

    private void ShowMicrophoneVisualizer(bool show)
    {
        if (show)
        {
            if (VolumeControlPanel != null) VolumeControlPanel.Visibility = Visibility.Collapsed;
            if (MicVisualizerPanel != null) MicVisualizerPanel.Visibility = Visibility.Visible;
            // TODO: Start microphone level animation
        }
        else
        {
            if (VolumeControlPanel != null) VolumeControlPanel.Visibility = Visibility.Visible;
            if (MicVisualizerPanel != null) MicVisualizerPanel.Visibility = Visibility.Collapsed;
            // TODO: Stop microphone level animation
        }
    }

    private void AddMessage(string message, bool isError = false)
    {
        var textBlock = new TextBlock
        {
            Text = message,
            Margin = new Thickness(0, 4, 0, 4),
            TextWrapping = TextWrapping.Wrap,
            Foreground = isError ?
                Application.Current.Resources["SystemFillColorCriticalBrush"] as Microsoft.UI.Xaml.Media.Brush :
                Application.Current.Resources["TextFillColorPrimaryBrush"] as Microsoft.UI.Xaml.Media.Brush
        };

        // 如果已经有默认消息，清除它
        if (MessagesPanel.Children.Count > 0 &&
            MessagesPanel.Children[0] is TextBlock defaultMsg &&
            defaultMsg.Text.Contains("等待对话开始"))
        {
            MessagesPanel.Children.Clear();
        }

        MessagesPanel.Children.Add(textBlock);

        // 滚动到底部
        MessagesScrollViewer.ChangeView(null, MessagesScrollViewer.ScrollableHeight, null);
    }

    private void UpdateVolumeText(double value)
    {
        if (VolumeText != null)
        {
            VolumeText.Text = $"{(int)value}%";
        }
    }
    private void SetEmotion(string emotionName)
    {
        try
        {
            if (_emotionManager != null && DefaultEmotionText != null)
            {
                var emoji = _emotionManager.GetEmotionEmoji(emotionName);
                DefaultEmotionText.Text = emoji;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to set emotion: {EmotionName}", emotionName);
        }
    }

    #endregion

    #region 按钮事件处理    
    private async void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isConnected || _voiceChatService == null) return;

        try
        {
            ConnectButton.IsEnabled = false;
            StatusText.Text = _resourceLoader.GetString("Status_Connecting");
            ConnectionStatusText.Text = _resourceLoader.GetString("ConnectionStatus_Connecting");
            ConnectionIndicator.Background = Application.Current.Resources["SystemFillColorCautionBrush"] as Microsoft.UI.Xaml.Media.Brush;

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

            // Set up wake word detector coordination (matches py-xiaozhi behavior)
            if (_interruptManager != null)
            {
                _voiceChatService.SetInterruptManager(_interruptManager);
                _logger?.LogInformation("Wake word detector coordination enabled");
            }

            // Use the service's IsConnected property to determine actual connection state
            bool isConnected = _voiceChatService.IsConnected;
            UpdateConnectionState(isConnected);

            if (isConnected)
            {
                AddMessage("连接成功");
                StatusText.Text = _resourceLoader.GetString("Status_Connected");
            }
            else
            {
                AddMessage("连接失败: 服务未连接", true);
                ConnectButton.IsEnabled = true;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to connect to voice chat service");
            AddMessage($"连接失败: {ex.Message}", true);
            UpdateConnectionState(false);
            ConnectButton.IsEnabled = true;
        }
    }

    private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isConnected || _voiceChatService == null) return;

        try
        {
            DisconnectButton.IsEnabled = false;

            // 停止当前语音对话
            if (_isListening)
            {
                await _voiceChatService.StopVoiceChatAsync();
            }

            // 清理事件订阅
            _voiceChatService.MessageReceived -= OnMessageReceived;
            _voiceChatService.VoiceChatStateChanged -= OnVoiceChatStateChanged;
            _voiceChatService.ErrorOccurred -= OnErrorOccurred;
            _voiceChatService.DeviceStateChanged -= OnDeviceStateChanged;

            _voiceChatService.Dispose();
            // 重置所有状态
            _isConnected = false;
            _isListening = false;

            AddMessage("已断开连接");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to disconnect from voice chat service");
            AddMessage($"断开连接失败: {ex.Message}", true);
        }
    }

    private async void ManualButton_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_voiceChatService == null || !_isConnected) return;

        try
        {
            if (!_isListening)
            {
                await _voiceChatService.StartVoiceChatAsync();
                ManualButtonText.Text = _resourceLoader.GetString("ManualButtonText_Release");
                AddMessage("开始录音，松开结束");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to start manual voice chat");
            AddMessage($"开始录音失败: {ex.Message}", true);
        }
    }

    private async void ManualButton_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_voiceChatService == null || !_isConnected) return;

        try
        {
            if (_isListening)
            {
                await _voiceChatService.StopVoiceChatAsync();
                ManualButtonText.Text = _resourceLoader.GetString("ManualButtonText_Hold");
                AddMessage("录音结束，正在处理...");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to stop manual voice chat");
            AddMessage($"停止录音失败: {ex.Message}", true);
        }
    }
    private async void AutoButton_Click(object sender, RoutedEventArgs e)
    {
        if (_voiceChatService == null || !_isConnected) return;

        try
        {
            if (!_isListening)
            {
                // Enable auto mode and start the conversation
                _voiceChatService.KeepListening = true;
                await _voiceChatService.ToggleChatStateAsync();
                AutoButtonText.Text = "停止对话";
                AddMessage("自动对话已开始");
            }
            else
            {
                // Disable auto mode and stop the conversation
                _voiceChatService.KeepListening = false;
                await _voiceChatService.ToggleChatStateAsync();
                AutoButtonText.Text = "开始对话";
                AddMessage("自动对话已停止");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to toggle auto chat mode");
            AddMessage($"切换自动对话失败: {ex.Message}", true);
        }
    }

    private async void AbortButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_voiceChatService != null && _isListening)
            {
                await _voiceChatService.StopVoiceChatAsync();
                AddMessage("已中断当前操作");
                TtsText.Text = "待命";
                SetEmotion("neutral");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to abort current operation");
            AddMessage($"中断操作失败: {ex.Message}", true);
        }
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        var message = MessageTextBox.Text.Trim();
        if (string.IsNullOrEmpty(message) || _voiceChatService == null || !_isConnected)
            return;

        try
        {
            AddMessage($"我: {message}", false);
            MessageTextBox.Text = "";
            await _voiceChatService.SendTextMessageAsync(message);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to send text message");
            AddMessage($"发送失败: {ex.Message}", true);
        }
    }

    private void MessageTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            SendButton_Click(sender, new RoutedEventArgs());
        }
    }

    private void ModeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _isAutoMode = !_isAutoMode;
        UpdateModeUI(_isAutoMode);
        AddMessage($"已切换到{(_isAutoMode ? "自动" : "手动")}对话模式");
    }

    private void VolumeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        var value = e.NewValue;
        if (value < 0) return;

        UpdateVolumeText(value);
        // Note: IVoiceChatService doesn't have SetVolume method
        // if (_voiceChatService != null)
        // {
        //     _voiceChatService.SetVolume((float)(value / 100.0));
        // }
    }

    private void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        var isMuted = VolumeSlider.Value == 0;

        if (isMuted)
        {
            VolumeSlider.Value = 80;
            MuteIcon.Glyph = "\uE767"; // Volume icon
        }
        else
        {
            VolumeSlider.Value = 0;
            MuteIcon.Glyph = "\uE74F"; // Mute icon
        }
    }

    #endregion

    #region 页面生命周期

    private void HomePage_Unloaded(object sender, RoutedEventArgs e)
    {
        // 清理事件订阅
        if (_voiceChatService != null)
        {
            _voiceChatService.DeviceStateChanged -= OnDeviceStateChanged;
            _voiceChatService.VoiceChatStateChanged -= OnVoiceChatStateChanged;
            _voiceChatService.MessageReceived -= OnMessageReceived;
            _voiceChatService.ErrorOccurred -= OnErrorOccurred;
        }
    }

    #endregion

    private void ManualButton_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        ManualButton_PointerReleased(sender, null!);
    }
}