using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace InterruptArchitectureTest.Sources;

/// <summary>
/// 热键打断源 - 使用轮询方式检测按键状态 (基于 ElectronBot 的实现方式)
/// </summary>
public class HotkeyInterruptSource : Core.InterruptSourceBase
{
    private readonly Dictionary<string, KeyInfo> _monitoredKeys = new();
    private readonly Dictionary<string, bool> _keyStates = new();
    private readonly Dictionary<string, DateTime> _lastTriggerTimes = new();
    private readonly TimeSpan _debounceTime = TimeSpan.FromMilliseconds(300);
    private readonly int _pollingInterval = 50; // 50ms 轮询间隔

    // Windows API
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    public HotkeyInterruptSource(ILogger<HotkeyInterruptSource>? logger = null)
        : base("HotkeyDetector", Core.InterruptTypes.Hotkey, logger)
    {
        // 初始化默认监控的按键
        AddMonitoredKey("F3", 0x72);  // F3 键
        AddMonitoredKey("E", 'E');    // E 键 (参考 ElectronBot 实现)
        AddMonitoredKey("ESC", 0x1B); // ESC 键
    }

    public void AddMonitoredKey(string name, int virtualKeyCode)
    {
        _monitoredKeys[name] = new KeyInfo(name, virtualKeyCode);
        _keyStates[name] = false;
        _lastTriggerTimes[name] = DateTime.MinValue;
        _logger?.LogInformation("Added monitored key: {Name} (VK: 0x{Key:X2})", name, virtualKeyCode);
    }

    public void RemoveMonitoredKey(string name)
    {
        _monitoredKeys.Remove(name);
        _keyStates.Remove(name);
        _lastTriggerTimes.Remove(name);
        _logger?.LogInformation("Removed monitored key: {Name}", name);
    }

    protected override Task OnStartAsync()
    {
        _logger?.LogInformation("Started hotkey monitoring using polling method");
        _logger?.LogInformation("Monitoring keys: {Keys}", string.Join(", ", _monitoredKeys.Keys));
        return Task.CompletedTask;
    }

    protected override Task OnStopAsync()
    {
        _logger?.LogInformation("Stopped hotkey monitoring");
        return Task.CompletedTask;
    }

    protected override async Task MonitoringLoopAsync()
    {
        _logger?.LogInformation("Hotkey polling loop started (interval: {Interval}ms)", _pollingInterval);

        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                if (!_isPaused && IsEnabled)
                {
                    CheckAllKeys();
                }

                await Task.Delay(_pollingInterval, _cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in hotkey polling loop");
                await Task.Delay(1000, _cancellationTokenSource.Token);
            }
        }

        _logger?.LogDebug("Hotkey polling loop stopped");
    }

    private void CheckAllKeys()
    {
        var now = DateTime.UtcNow;

        foreach (var kvp in _monitoredKeys)
        {
            var keyName = kvp.Key;
            var keyInfo = kvp.Value;
            var currentState = IsKeyPressed(keyInfo.VirtualKeyCode);
            var previousState = _keyStates[keyName];

            // 检测按键按下事件 (从未按下到按下的状态变化)
            if (currentState && !previousState)
            {
                // 检查防抖
                if (now - _lastTriggerTimes[keyName] >= _debounceTime)
                {
                    _lastTriggerTimes[keyName] = now;
                    OnKeyPressed(keyName, keyInfo);
                }
                else
                {
                    _logger?.LogDebug("Key press ignored due to debounce: {Key}", keyName);
                }
            }

            _keyStates[keyName] = currentState;
        }
    }

    private bool IsKeyPressed(int virtualKeyCode)
    {
        try
        {
            // GetAsyncKeyState 返回按键状态，最高位表示按键是否被按下
            return (GetAsyncKeyState(virtualKeyCode) & 0x8000) != 0;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error checking key state for VK: 0x{Key:X2}", virtualKeyCode);
            return false;
        }
    }

    private void OnKeyPressed(string keyName, KeyInfo keyInfo)
    {
        _logger?.LogInformation("Key pressed: {KeyName} (VK: 0x{VK:X2})", keyName, keyInfo.VirtualKeyCode);
        
        TriggerInterrupt(
            $"Key '{keyName}' pressed", 
            new { 
                KeyName = keyName, 
                VirtualKeyCode = keyInfo.VirtualKeyCode,
                Timestamp = DateTime.UtcNow
            }, 
            priority: 8
        );
    }

    /// <summary>
    /// 获取按钮状态 - 兼容 ElectronBot 的接口
    /// </summary>
    /// <returns>E 键是否被按下</returns>
    public bool GetButtonState()
    {
        return IsKeyPressed('E');
    }

    /// <summary>
    /// 检查特定按键是否被按下
    /// </summary>
    /// <param name="keyName">按键名称</param>
    /// <returns>是否被按下</returns>
    public bool IsKeyCurrentlyPressed(string keyName)
    {
        if (_monitoredKeys.TryGetValue(keyName, out var keyInfo))
        {
            return IsKeyPressed(keyInfo.VirtualKeyCode);
        }
        return false;
    }
}

/// <summary>
/// 按键信息
/// </summary>
public class KeyInfo
{
    public string Name { get; }
    public int VirtualKeyCode { get; }

    public KeyInfo(string name, int virtualKeyCode)
    {
        Name = name;
        VirtualKeyCode = virtualKeyCode;
    }
}
