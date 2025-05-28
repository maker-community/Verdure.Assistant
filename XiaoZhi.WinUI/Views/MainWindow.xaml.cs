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

namespace XiaoZhi.WinUI.Views
{
    /// <summary>
    /// 主窗口 - 类似py-xiaozhi的GUI界面
    /// </summary>
    public sealed partial class MainWindow : Window
    {
        private readonly ILogger<MainWindow>? _logger;
        private IVoiceChatService? _voiceChatService;
        private EmotionManager? _emotionManager;
        
        // UI 状态
        private bool _isAutoMode = false;
        private bool _isConnected = false;
        private bool _isListening = false;
        private bool _isSpeaking = false;

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
        }

        private void InitializeServices()
        {
            try
            {
                _voiceChatService = App.GetService<IVoiceChatService>();
                if (_voiceChatService != null)
                {
                    // 订阅事件
                    _voiceChatService.DeviceStateChanged += OnDeviceStateChanged;
                    _voiceChatService.VoiceChatStateChanged += OnVoiceChatStateChanged;
                    _voiceChatService.MessageReceived += OnMessageReceived;
                    _voiceChatService.ErrorOccurred += OnErrorOccurred;
                    _voiceChatService.ListeningModeChanged += OnListeningModeChanged;
                }
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
        }

        private void UpdateUIForDeviceState(DeviceState state)
        {
            _isConnected = state != DeviceState.Disconnected;
            
            // 根据状态更新UI
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
        }

        private async void ManualButton_PointerCaptureLost(object sender, PointerEventArgs e)
        {
            // 当失去指针捕获时也停止监听
            await ManualButton_PointerReleased(sender, null);
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
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (_voiceChatService != null && !_voiceChatService.IsConnected)
            {
                try
                {
                    // Initialize and connect - this should be handled in your configuration
                    UpdateStatusText("连接中...");
                    ConnectButton.IsEnabled = false;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to connect");
                    UpdateStatusText("连接失败");
                    ConnectButton.IsEnabled = true;
                }
            }
        }

        private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (_voiceChatService != null && _voiceChatService.IsConnected)
            {
                try
                {
                    await _voiceChatService.StopVoiceChatAsync();
                    // Disconnect logic would go here
                    UpdateStatusText("已断开");
                    ConnectButton.IsEnabled = true;
                    DisconnectButton.IsEnabled = false;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to disconnect");
                }
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
        }

        private async void MessageTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                await SendButton_Click(sender, null);
            }
        }

        // Settings page event handlers
        private void WakeWordToggle_Toggled(object sender, RoutedEventArgs e)
        {
            // Wake word toggle logic
        }

        private void DefaultVolumeSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            // Default volume change logic
        }

        private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            // Save settings logic
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
                DeviceState.Disconnected => "未连接",
                _ => "未知状态"
            };
        }

        #endregion
    }
}