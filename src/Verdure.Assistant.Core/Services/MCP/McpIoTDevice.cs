using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Services.MCP;

namespace Verdure.Assistant.Core.Services.MCP;

/// <summary>
/// MCP IoT设备基类 - 对应xiaozhi-esp32的Thing基类，采用MCP工具模式
/// </summary>
public abstract class McpIoTDevice : IDisposable
{
    protected readonly ILogger? _logger;
    protected readonly McpServer _mcpServer;
    public string Name { get; protected set; } = string.Empty;
    public string Description { get; protected set; } = string.Empty;
    public string Type { get; protected set; } = string.Empty;
    public string DeviceId { get; protected set; } = string.Empty;
    
    // 设备状态属性
    protected Dictionary<string, object?> _deviceState = new();
    
    // 设备状态变化事件
    public event EventHandler<McpDeviceStateChangedEventArgs>? StateChanged;

    protected McpIoTDevice(McpServer mcpServer, ILogger? logger = null)
    {
        _mcpServer = mcpServer;
        _logger = logger;
        
        // 子类初始化后注册工具
        RegisterTools();
    }
    
    /// <summary>
    /// 注册MCP工具 - 子类重写此方法
    /// </summary>
    protected abstract void RegisterTools();
    
    /// <summary>
    /// 获取设备状态
    /// </summary>
    public virtual string GetDeviceStatusJson()
    {
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            name = Name,
            type = Type,
            description = Description,
            state = _deviceState
        });
    }
    
    /// <summary>
    /// 设置设备状态属性
    /// </summary>
    protected void SetState(string key, object? value)
    {
        var oldValue = _deviceState.TryGetValue(key, out var existing) ? existing : null;
        _deviceState[key] = value;
        
        // 触发状态变化事件
        if (!Equals(oldValue, value))
        {
            OnStateChanged(key, value);
        }
    }
    
    /// <summary>
    /// 获取设备状态属性
    /// </summary>
    protected T? GetState<T>(string key)
    {
        if (_deviceState.TryGetValue(key, out var value) && value != null)
        {
            try
            {
                if (value is T directValue)
                    return directValue;
                    
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch
            {
                return default(T);
            }
        }
        return default(T);
    }
    
    /// <summary>
    /// 触发状态变化事件
    /// </summary>
    protected virtual void OnStateChanged(string propertyName, object? newValue)
    {
        StateChanged?.Invoke(this, new McpDeviceStateChangedEventArgs
        {
            DeviceName = Name,
            PropertyName = propertyName,
            NewValue = newValue,
            StateJson = GetDeviceStatusJson()
        });
    }
    
    /// <summary>
    /// 添加设备状态获取工具
    /// </summary>
    protected void AddGetDeviceStatusTool()
    {        _mcpServer.AddTool(
            $"self.{Name.ToLower()}.get_device_status",
            $"获取{Description}的实时状态信息",
            new McpPropertyList(),
            async (properties) =>
            {
                return await Task.FromResult(GetDeviceStatusJson());
            });
    }
    
    /// <summary>
    /// 获取设备所有工具
    /// </summary>
    public List<McpTool> GetTools()
    {
        // Return tools that were registered for this device from the MCP server
        return _mcpServer.GetTools()
            .Where(t => t.Name.StartsWith($"self.{Name.ToLower()}."))
            .ToList();
    }
    
    /// <summary>
    /// 初始化设备
    /// </summary>
    public virtual async Task InitializeAsync()
    {
        _logger?.LogInformation($"Initializing MCP device: {Name}");
        await Task.CompletedTask;
    }
    
    /// <summary>
    /// 获取设备属性值字典
    /// </summary>
    public Dictionary<string, object?> GetPropertyValues()
    {
        return new Dictionary<string, object?>(_deviceState);
    }

    #region IDisposable Support
    
    private bool _disposed = false;
    
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // 清理托管资源
                _deviceState.Clear();
            }
            
            _disposed = true;
        }
    }
    
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    #endregion
}

/// <summary>
/// MCP设备状态变化事件参数
/// </summary>
public class McpDeviceStateChangedEventArgs : EventArgs
{
    public string DeviceName { get; set; } = string.Empty;
    public string PropertyName { get; set; } = string.Empty;
    public object? NewValue { get; set; }
    public string StateJson { get; set; } = string.Empty;
}
