using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;
using XiaoZhi.Core.Interfaces;
using XiaoZhi.Core.Models;
using XiaoZhi.Core.Services;

namespace XiaoZhi.WinUI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ILogger<MainViewModel> _logger;
    private IVoiceChatService? _voiceChatService;
    
    [ObservableProperty]
    private bool _isConnected;
    
    [ObservableProperty]
    private bool _isVoiceChatActive;
    
    [ObservableProperty]
    private bool _enableVoiceChat = true;
    
    [ObservableProperty]
    private string _currentMessage = string.Empty;
    
    [ObservableProperty]
    private string _connectionStatus = "未连接";
    
    [ObservableProperty]
    private string _serverUrl = "ws://localhost:8080/ws";
    
    [ObservableProperty]
    private bool _useWebSocket = true;

    public ObservableCollection<ChatMessage> Messages { get; } = new();

    public MainViewModel(ILogger<MainViewModel> logger)
    {
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        try
        {
            _voiceChatService = new VoiceChatService(_logger as ILogger<VoiceChatService>);
            
            // 注册事件处理器
            _voiceChatService.MessageReceived += OnMessageReceived;
            _voiceChatService.VoiceChatStateChanged += OnVoiceChatStateChanged;
            _voiceChatService.ErrorOccurred += OnErrorOccurred;

            // 加载默认配置
            var config = new XiaoZhiConfig
            {
                ServerUrl = ServerUrl,
                UseWebSocket = UseWebSocket,
                EnableVoice = EnableVoiceChat,
                AudioSampleRate = 16000,
                AudioChannels = 1,
                AudioFormat = "opus"
            };

            await _voiceChatService.InitializeAsync(config);
            
            IsConnected = _voiceChatService.IsConnected;
            ConnectionStatus = IsConnected ? "已连接" : "连接失败";
            
            _logger.LogInformation("MainViewModel 初始化完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "初始化失败");
            ConnectionStatus = $"初始化失败: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ToggleVoiceChatAsync()
    {
        if (_voiceChatService == null) return;

        try
        {
            if (IsVoiceChatActive)
            {
                await _voiceChatService.StopVoiceChatAsync();
            }
            else
            {
                if (!EnableVoiceChat)
                {
                    // 显示提示，用户可以选择是否启用语音对话
                    var shouldEnable = await ShowVoiceChatConfirmationAsync();
                    if (!shouldEnable) return;
                    
                    EnableVoiceChat = true;
                }
                
                await _voiceChatService.StartVoiceChatAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "切换语音对话状态失败");
            await ShowErrorAsync($"操作失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (_voiceChatService == null || string.IsNullOrWhiteSpace(CurrentMessage)) return;

        try
        {
            await _voiceChatService.SendTextMessageAsync(CurrentMessage);
            
            // 添加用户消息到列表
            var userMessage = new ChatMessage
            {
                Type = "text",
                Content = CurrentMessage,
                Role = "user",
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            
            Messages.Add(userMessage);
            CurrentMessage = string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "发送消息失败");
            await ShowErrorAsync($"发送消息失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (_voiceChatService != null) return;

        ConnectionStatus = "连接中...";
        await InitializeAsync();
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (_voiceChatService == null) return;

        try
        {
            if (IsVoiceChatActive)
            {
                await _voiceChatService.StopVoiceChatAsync();
            }
            
            _voiceChatService.Dispose();
            _voiceChatService = null;
            
            IsConnected = false;
            IsVoiceChatActive = false;
            ConnectionStatus = "已断开连接";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "断开连接失败");
            await ShowErrorAsync($"断开连接失败: {ex.Message}");
        }
    }

    private void OnMessageReceived(object? sender, ChatMessage message)
    {
        // 在 UI 线程中更新消息列表
        App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            Messages.Add(message);
        });
    }

    private void OnVoiceChatStateChanged(object? sender, bool isActive)
    {
        App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
        {
            IsVoiceChatActive = isActive;
        });
    }

    private void OnErrorOccurred(object? sender, string error)
    {
        App.MainWindow?.DispatcherQueue.TryEnqueue(async () =>
        {
            await ShowErrorAsync(error);
        });
    }

    private async Task<bool> ShowVoiceChatConfirmationAsync()
    {
        // TODO: 实现确认对话框
        // 这里简化为直接返回 true，实际项目中应该显示确认对话框
        return await Task.FromResult(true);
    }

    private async Task ShowErrorAsync(string message)
    {
        // TODO: 实现错误提示对话框
        // 这里简化为日志输出，实际项目中应该显示错误对话框
        _logger.LogError("错误: {Message}", message);
        await Task.CompletedTask;
    }
}
