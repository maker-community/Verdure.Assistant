using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Interfaces;

namespace Verdure.Assistant.Core.Services.Interrupt.Sources;

/// <summary>
/// 热键打断源 - 基于全局热键的打断
/// Hotkey interrupt source based on global hotkey detection
/// </summary>
public class HotkeyInterruptSource : InterruptSourceBase
{
    private readonly IVoiceChatService? _voiceChatService;
    private GlobalHotkeyService? _hotkeyService;
    private bool _hotkeyRegistered = false;

    public HotkeyInterruptSource(IVoiceChatService? voiceChatService = null, 
        ILogger<HotkeyInterruptSource>? logger = null)
        : base("Hotkey", InterruptTypes.Hotkey, logger)
    {
        _voiceChatService = voiceChatService;
    }

    /// <summary>
    /// 设置语音聊天服务
    /// </summary>
    public void SetVoiceChatService(IVoiceChatService voiceChatService)
    {
        if (_hotkeyService == null)
        {
            _hotkeyService = new GlobalHotkeyService(voiceChatService);
            _hotkeyService.HotkeyPressed += OnHotkeyPressed;
        }
    }

    protected override async Task OnStartAsync()
    {
        if (_hotkeyService != null && !_hotkeyRegistered)
        {
            _hotkeyRegistered = _hotkeyService.RegisterHotkey();
            if (_hotkeyRegistered)
            {
                _logger?.LogInformation("Global hotkey (F3) registered for interrupt");
            }
            else
            {
                _logger?.LogWarning("Failed to register global hotkey (F3)");
            }
        }
        await base.OnStartAsync();
    }

    protected override async Task OnStopAsync()
    {
        if (_hotkeyService != null && _hotkeyRegistered)
        {
            _hotkeyService.UnregisterHotkey();
            _hotkeyRegistered = false;
            _logger?.LogInformation("Global hotkey (F3) unregistered");
        }
        await base.OnStopAsync();
    }

    protected override async Task MonitoringLoopAsync()
    {
        _logger?.LogInformation("Hotkey interrupt monitoring started");

        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                // 热键检测在OnHotkeyPressed中处理，这里只需要保持监听循环
                await Task.Delay(1000, _cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in hotkey interrupt monitoring loop");
                await Task.Delay(1000, _cancellationTokenSource.Token);
            }
        }
    }

    private void OnHotkeyPressed(object? sender, bool pressed)
    {
        if (pressed && !_isPaused && IsEnabled)
        {
            TriggerInterrupt("F3 hotkey pressed", null, priority: 7);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_hotkeyService != null)
            {
                _hotkeyService.HotkeyPressed -= OnHotkeyPressed;
                if (_hotkeyRegistered)
                {
                    _hotkeyService.UnregisterHotkey();
                }
                _hotkeyService.Dispose();
            }
        }
        base.Dispose(disposing);
    }
}