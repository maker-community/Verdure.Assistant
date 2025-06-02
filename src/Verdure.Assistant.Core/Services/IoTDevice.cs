using Microsoft.Extensions.Logging;
using System.Text.Json;
using Verdure.Assistant.Core.Models;

namespace Verdure.Assistant.Core.Services;

/// <summary>
/// IoT设备基类 - 对应py-xiaozhi的Thing基类
/// </summary>
public abstract class IoTDevice : IDisposable
{
    protected readonly ILogger? _logger;
    
    public string Name { get; protected set; } = string.Empty;
    public string Description { get; protected set; } = string.Empty;
    public string Type { get; protected set; } = string.Empty;
    
    protected Dictionary<string, IoTProperty> Properties { get; set; } = new();
    protected Dictionary<string, IoTMethod> Methods { get; set; } = new();
    
    protected IoTDevice(ILogger? logger = null)
    {
        _logger = logger;
        RegisterProperties();
        RegisterMethods();
    }
    
    /// <summary>
    /// 注册设备属性 - 子类重写此方法
    /// </summary>
    protected abstract void RegisterProperties();
    
    /// <summary>
    /// 注册设备方法 - 子类重写此方法
    /// </summary>
    protected abstract void RegisterMethods();
    
    /// <summary>
    /// 添加属性
    /// </summary>
    protected void AddProperty(string name, string description, IoTValueType type, bool readable = true, bool writable = false, object? defaultValue = null)
    {
        var property = new IoTProperty(name, description, type, readable, writable)
        {
            Value = defaultValue
        };
        Properties[name] = property;
    }
    
    /// <summary>
    /// 添加方法
    /// </summary>
    protected void AddMethod(string name, string description, List<IoTParameter>? parameters = null, Func<Dictionary<string, IoTParameter>, Task<object?>>? handler = null)
    {
        var method = new IoTMethod(name, description, parameters, handler);
        Methods[name] = method;
    }
    
    /// <summary>
    /// 获取设备描述符
    /// </summary>
    public IoTDeviceDescriptor GetDescriptor()
    {
        return new IoTDeviceDescriptor
        {
            Name = Name,
            Description = Description,
            Type = Type,
            Properties = Properties.Values.ToList(),
            Methods = Methods.Values.Select(m => new IoTMethod
            {
                Name = m.Name,
                Description = m.Description,
                Parameters = m.Parameters
            }).ToList()
        };
    }
    
    /// <summary>
    /// 获取设备状态JSON
    /// </summary>
    public string GetStateJson()
    {
        var state = new Dictionary<string, object>
        {
            ["name"] = Name,
            ["type"] = Type,
            ["properties"] = Properties.Where(p => p.Value.Readable)
                .ToDictionary(p => p.Key, p => p.Value.Value)
        };
        
        return JsonSerializer.Serialize(state);
    }
    
    /// <summary>
    /// 调用设备方法
    /// </summary>
    public async Task<IoTCommandResult> InvokeAsync(IoTCommand command)
    {
        try
        {
            if (!Methods.TryGetValue(command.Method, out var method))
            {
                return IoTCommandResult.CreateError($"方法 {command.Method} 不存在", Name, command.Method);
            }
            
            if (method.Handler == null)
            {
                return IoTCommandResult.CreateError($"方法 {command.Method} 没有处理器", Name, command.Method);
            }
            
            // 准备参数
            var parameters = new Dictionary<string, IoTParameter>();
            foreach (var param in method.Parameters)
            {
                var parameter = new IoTParameter(param.Name, param.Description, param.Type, param.Required);
                
                if (command.Parameters.TryGetValue(param.Name, out var value))
                {
                    parameter.Value = value;
                }
                else if (param.Required)
                {
                    return IoTCommandResult.CreateError($"缺少必需参数: {param.Name}", Name, command.Method);
                }
                
                parameters[param.Name] = parameter;
            }
            
            // 执行方法
            var result = await method.Handler(parameters);
            
            return IoTCommandResult.CreateSuccess($"方法 {command.Method} 执行成功", result, Name, command.Method);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "执行IoT命令失败: {Device}.{Method}", Name, command.Method);
            return IoTCommandResult.CreateError($"执行失败: {ex.Message}", Name, command.Method);
        }
    }
    
    /// <summary>
    /// 设置属性值
    /// </summary>
    protected void SetPropertyValue(string name, object? value)
    {
        if (Properties.TryGetValue(name, out var property))
        {
            property.Value = value;
        }
    }
    
    /// <summary>
    /// 获取属性值
    /// </summary>
    protected T? GetPropertyValue<T>(string name)
    {
        if (Properties.TryGetValue(name, out var property) && property.Value != null)
        {
            try
            {
                if (property.Value is T directValue)
                    return directValue;
                    
                return (T)Convert.ChangeType(property.Value, typeof(T));
            }
            catch
            {
                return default(T);
            }        }
        return default(T);
    }
    
    #region IDisposable Support
    
    private bool _disposed = false;
    
    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // 子类可以重写此方法来释放托管资源
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
