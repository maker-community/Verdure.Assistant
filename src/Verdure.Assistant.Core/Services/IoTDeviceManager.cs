using Microsoft.Extensions.Logging;
using System.Text.Json;
using Verdure.Assistant.Core.Models;

namespace Verdure.Assistant.Core.Services;

/// <summary>
/// IoT设备管理器接口
/// </summary>
public interface IIoTDeviceManager
{
    /// <summary>
    /// 添加IoT设备
    /// </summary>
    void AddDevice(IoTDevice device);
    
    /// <summary>
    /// 移除IoT设备
    /// </summary>
    bool RemoveDevice(string deviceName);
    
    /// <summary>
    /// 获取所有设备
    /// </summary>
    IReadOnlyList<IoTDevice> GetDevices();
    
    /// <summary>
    /// 根据名称获取设备
    /// </summary>
    IoTDevice? GetDevice(string deviceName);
    
    /// <summary>
    /// 获取所有设备描述符JSON
    /// </summary>
    string GetDescriptorsJson();
    
    /// <summary>
    /// 获取所有设备状态JSON
    /// </summary>
    string GetStatesJson();
    
    /// <summary>
    /// 执行IoT命令
    /// </summary>
    Task<IoTCommandResult> ExecuteCommandAsync(IoTCommand command);
    
    /// <summary>
    /// 执行多个IoT命令
    /// </summary>
    Task<List<IoTCommandResult>> ExecuteCommandsAsync(List<IoTCommand> commands);
    
    /// <summary>
    /// 设备状态变化事件
    /// </summary>
    event EventHandler<IoTDeviceStateChangedEventArgs>? DeviceStateChanged;
}

/// <summary>
/// IoT设备状态变化事件参数
/// </summary>
public class IoTDeviceStateChangedEventArgs : EventArgs
{
    public string DeviceName { get; set; } = string.Empty;
    public string StateJson { get; set; } = string.Empty;
}

/// <summary>
/// IoT设备管理器实现 - 对应py-xiaozhi的ThingManager
/// </summary>
public class IoTDeviceManager : IIoTDeviceManager
{
    private readonly ILogger<IoTDeviceManager> _logger;
    private readonly List<IoTDevice> _devices = new();
    private readonly Dictionary<string, string> _lastStates = new();
    
    public event EventHandler<IoTDeviceStateChangedEventArgs>? DeviceStateChanged;
    
    public IoTDeviceManager(ILogger<IoTDeviceManager> logger)
    {
        _logger = logger;
    }
    
    public void AddDevice(IoTDevice device)
    {
        if (_devices.Any(d => d.Name == device.Name))
        {
            _logger.LogWarning("设备 {DeviceName} 已存在，将替换现有设备", device.Name);
            RemoveDevice(device.Name);
        }
        
        _devices.Add(device);
        _logger.LogInformation("已添加IoT设备: {DeviceName} ({DeviceType})", device.Name, device.Type);
        
        // 触发状态变化事件
        NotifyStateChanged(device.Name, device.GetStateJson());
    }
    
    public bool RemoveDevice(string deviceName)
    {
        var device = _devices.FirstOrDefault(d => d.Name == deviceName);
        if (device != null)
        {
            _devices.Remove(device);
            _lastStates.Remove(deviceName);
            _logger.LogInformation("已移除IoT设备: {DeviceName}", deviceName);
            return true;
        }
        return false;
    }
    
    public IReadOnlyList<IoTDevice> GetDevices()
    {
        return _devices.AsReadOnly();
    }
    
    public IoTDevice? GetDevice(string deviceName)
    {
        return _devices.FirstOrDefault(d => d.Name == deviceName);
    }
    
    public string GetDescriptorsJson()
    {
        var descriptors = _devices.Select(d => d.GetDescriptor()).ToList();
        return JsonSerializer.Serialize(descriptors, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
    }
    
    public string GetStatesJson()
    {
        var states = _devices.Select(d => JsonSerializer.Deserialize<object>(d.GetStateJson())).ToList();
        return JsonSerializer.Serialize(states, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
    }
    
    public async Task<IoTCommandResult> ExecuteCommandAsync(IoTCommand command)
    {
        try
        {
            var device = GetDevice(command.Name);
            if (device == null)
            {
                var errorMsg = $"设备 {command.Name} 不存在";
                _logger.LogWarning(errorMsg);
                return IoTCommandResult.CreateError(errorMsg, command.Name, command.Method);
            }
            
            _logger.LogDebug("执行IoT命令: {Device}.{Method}", command.Name, command.Method);
            var result = await device.InvokeAsync(command);
            
            if (result.Success)
            {
                _logger.LogInformation("IoT命令执行成功: {Device}.{Method} - {Message}", 
                    command.Name, command.Method, result.Message);
                
                // 命令执行成功后，检查设备状态是否变化
                CheckAndNotifyStateChange(device);
            }
            else
            {
                _logger.LogWarning("IoT命令执行失败: {Device}.{Method} - {Message}", 
                    command.Name, command.Method, result.Message);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            var errorMsg = $"执行IoT命令时发生异常: {ex.Message}";
            _logger.LogError(ex, errorMsg);
            return IoTCommandResult.CreateError(errorMsg, command.Name, command.Method);
        }
    }
    
    public async Task<List<IoTCommandResult>> ExecuteCommandsAsync(List<IoTCommand> commands)
    {
        var results = new List<IoTCommandResult>();
        
        foreach (var command in commands)
        {
            var result = await ExecuteCommandAsync(command);
            results.Add(result);
        }
        
        return results;
    }
    
    /// <summary>
    /// 检查并通知设备状态变化
    /// </summary>
    private void CheckAndNotifyStateChange(IoTDevice device)
    {
        var currentState = device.GetStateJson();
        var deviceName = device.Name;
        
        if (!_lastStates.TryGetValue(deviceName, out var lastState) || lastState != currentState)
        {
            _lastStates[deviceName] = currentState;
            NotifyStateChanged(deviceName, currentState);
        }
    }
    
    /// <summary>
    /// 通知设备状态变化
    /// </summary>
    private void NotifyStateChanged(string deviceName, string stateJson)
    {
        try
        {
            DeviceStateChanged?.Invoke(this, new IoTDeviceStateChangedEventArgs
            {
                DeviceName = deviceName,
                StateJson = stateJson
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "通知设备状态变化时发生异常: {DeviceName}", deviceName);
        }
    }
    
    /// <summary>
    /// 获取状态变化的设备JSON（增量更新）
    /// </summary>
    public string GetChangedStatesJson()
    {
        var changedStates = new List<object>();
        
        foreach (var device in _devices)
        {
            var currentState = device.GetStateJson();
            var deviceName = device.Name;
            
            if (!_lastStates.TryGetValue(deviceName, out var lastState) || lastState != currentState)
            {
                _lastStates[deviceName] = currentState;
                changedStates.Add(JsonSerializer.Deserialize<object>(currentState));
            }
        }
        
        return JsonSerializer.Serialize(changedStates, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });
    }
}
