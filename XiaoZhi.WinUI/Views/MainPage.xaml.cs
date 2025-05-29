using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System.Collections.ObjectModel;
using XiaoZhi.Core.Interfaces;
using XiaoZhi.Core.Models;
using XiaoZhi.Core.Services;
using XiaoZhi.WinUI.ViewModels;

namespace XiaoZhi.WinUI.Views
{
    /// <summary>
    /// 主页面 - 语音聊天界面
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private IVoiceChatService? _voiceChatService;
        private readonly ObservableCollection<ChatMessageItem> _messages = new();
        private bool _isConnected = false;
        private bool _isVoiceChatActive = false; public MainPage()
        {
            this.InitializeComponent();
            this.MessagesListView.ItemsSource = _messages;
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isConnected) return; try
            {
                ConnectButton.IsEnabled = false;
                ConnectionStatusText.Text = "连接中...";

                _voiceChatService = App.GetService<IVoiceChatService>();

                if (_voiceChatService == null)
                {
                    throw new InvalidOperationException("VoiceChatService not available from DI container");
                }

                // 注册事件处理器
                _voiceChatService.MessageReceived += OnMessageReceived;
                _voiceChatService.VoiceChatStateChanged += OnVoiceChatStateChanged;
                _voiceChatService.ErrorOccurred += OnErrorOccurred;

                // 创建配置
                var config = new XiaoZhiConfig
                {
                    ServerUrl = "ws://localhost:8080/ws",
                    UseWebSocket = true,
                    EnableVoice = EnableVoiceChatToggle.IsOn,
                    AudioSampleRate = 16000,
                    AudioChannels = 1,
                    AudioFormat = "opus"
                };

                await _voiceChatService.InitializeAsync(config);

                _isConnected = _voiceChatService.IsConnected;
                UpdateConnectionUI();

                if (_isConnected)
                {
                    ConnectionStatusText.Text = "已连接";
                    AddSystemMessage("已连接到服务器");
                }
                else
                {
                    ConnectionStatusText.Text = "连接失败";
                    AddSystemMessage("连接失败");
                }
            }
            catch (Exception ex)
            {
                ConnectionStatusText.Text = "连接失败";
                AddSystemMessage($"连接失败: {ex.Message}");
            }
            finally
            {
                ConnectButton.IsEnabled = !_isConnected;
            }
        }

        private async void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isConnected) return;

            try
            {
                if (_isVoiceChatActive)
                {
                    await _voiceChatService?.StopVoiceChatAsync()!;
                }

                _voiceChatService?.Dispose();
                _voiceChatService = null;

                _isConnected = false;
                _isVoiceChatActive = false;
                UpdateConnectionUI();

                ConnectionStatusText.Text = "已断开";
                AddSystemMessage("已断开连接");
            }
            catch (Exception ex)
            {
                AddSystemMessage($"断开连接失败: {ex.Message}");
            }
        }

        private async void VoiceChatButton_Click(object sender, RoutedEventArgs e)
        {
            if (_voiceChatService == null || !_isConnected) return;

            try
            {
                if (_isVoiceChatActive)
                {
                    await _voiceChatService.StopVoiceChatAsync();
                    AddSystemMessage("语音对话已停止");
                }
                else
                {
                    if (!EnableVoiceChatToggle.IsOn)
                    {
                        var dialog = new ContentDialog
                        {
                            Title = "启用语音对话",
                            Content = "是否要启用语音对话功能？这将允许应用访问您的麦克风。",
                            PrimaryButtonText = "启用",
                            CloseButtonText = "取消",
                            XamlRoot = this.XamlRoot
                        };

                        var result = await dialog.ShowAsync();
                        if (result != ContentDialogResult.Primary)
                            return;

                        EnableVoiceChatToggle.IsOn = true;
                    }

                    await _voiceChatService.StartVoiceChatAsync();
                    AddSystemMessage("语音对话已开始，请说话...");
                }
            }
            catch (Exception ex)
            {
                AddSystemMessage($"操作失败: {ex.Message}");
            }
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e)
        {
            await SendMessage();
        }

        private async void MessageTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter)
            {
                await SendMessage();
                e.Handled = true;
            }
        }

        private async Task SendMessage()
        {
            if (_voiceChatService == null || !_isConnected || string.IsNullOrWhiteSpace(MessageTextBox.Text))
                return;

            var messageText = MessageTextBox.Text.Trim();
            MessageTextBox.Text = string.Empty;

            try
            {
                await _voiceChatService.SendTextMessageAsync(messageText);

                // 添加用户消息到界面
                AddMessage("user", messageText);
            }
            catch (Exception ex)
            {
                AddSystemMessage($"发送消息失败: {ex.Message}");
            }
        }

        private void OnMessageReceived(object? sender, ChatMessage message)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                AddMessage(message.Role, message.Content);
            });
        }

        private void OnVoiceChatStateChanged(object? sender, bool isActive)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _isVoiceChatActive = isActive;
                UpdateVoiceChatUI();
            });
        }

        private void OnErrorOccurred(object? sender, string error)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                AddSystemMessage($"错误: {error}");
            });
        }
        private void UpdateConnectionUI()
        {
            ConnectButton.IsEnabled = !_isConnected;
            DisconnectButton.IsEnabled = _isConnected;
            EnableVoiceChatToggle.IsEnabled = _isConnected;
            VoiceChatButton.IsEnabled = _isConnected;
            MessageTextBox.IsEnabled = _isConnected;
            SendButton.IsEnabled = _isConnected;

            // 更新连接状态文本
            if (_isConnected)
            {
                ConnectionStatusText.Text = "已连接";
            }
            else
            {
                ConnectionStatusText.Text = "未连接";
                // 重置语音对话状态
                _isVoiceChatActive = false;
                UpdateVoiceChatUI();
            }
        }

        private void UpdateVoiceChatUI()
        {
            VoiceChatButton.Content = _isVoiceChatActive ? "停止语音对话" : "开始语音对话";
            if (_isVoiceChatActive)
            {
                VoiceChatButton.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.OrangeRed);
            }
            else
            {
                VoiceChatButton.ClearValue(Button.BackgroundProperty);
            }
        }

        private void AddMessage(string role, string content)
        {
            var item = new ChatMessageItem
            {
                Role = role,
                Content = content,
                Timestamp = DateTime.Now,
                IsUser = role == "user"
            };

            _messages.Add(item);

            // 滚动到底部
            if (_messages.Count > 0)
            {
                MessagesListView.ScrollIntoView(_messages.Last());
            }
        }
        private void AddSystemMessage(string message)
        {
            AddMessage("system", message);
        }

        private async void AutoDialogueToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (_voiceChatService == null || !_isConnected) return;

            try
            {
                var toggle = sender as ToggleSwitch;
                if (toggle != null)
                {
                    _voiceChatService.KeepListening = toggle.IsOn;

                    if (toggle.IsOn)
                    {
                        AddSystemMessage("自动对话模式已启用");
                        // 更新ToggleChatStateButton文本
                        if (ToggleChatStateButton != null)
                        {
                            ToggleChatStateButton.Content = "开始自动对话";
                            ToggleChatStateButton.IsEnabled = true;
                        }
                    }
                    else
                    {
                        AddSystemMessage("自动对话模式已禁用");
                        // 停止当前对话状态
                        if (_voiceChatService.IsVoiceChatActive)
                        {
                            await _voiceChatService.StopVoiceChatAsync();
                        }
                        // 更新ToggleChatStateButton文本
                        if (ToggleChatStateButton != null)
                        {
                            ToggleChatStateButton.Content = "切换对话状态";
                            ToggleChatStateButton.IsEnabled = false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AddSystemMessage($"切换自动对话模式失败: {ex.Message}");
            }
        }

        private async void ToggleChatStateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_voiceChatService == null || !_isConnected) return;

            try
            {
                await _voiceChatService.ToggleChatStateAsync();

                if (_voiceChatService.KeepListening)
                {
                    AddSystemMessage("对话状态已切换");
                }
            }
            catch (Exception ex)
            {
                AddSystemMessage($"切换对话状态失败: {ex.Message}");
            }
        }
    }    

    // 消息项模型，用于界面绑定
    public class ChatMessageItem
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public bool IsUser { get; set; }
        
        public string DisplayRole => Role switch
        {
            "user" => "您",
            "assistant" => "小智",
            "system" => "系统",
            _ => Role
        };
        
        public string TimeString => Timestamp.ToString("HH:mm:ss");
        
        public Microsoft.UI.Xaml.Media.Brush BackgroundBrush => IsUser
            ? new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LightBlue)
            : new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.LightGray);
            
        public HorizontalAlignment MessageAlignment => IsUser 
            ? HorizontalAlignment.Right 
            : HorizontalAlignment.Left;
    }
}
