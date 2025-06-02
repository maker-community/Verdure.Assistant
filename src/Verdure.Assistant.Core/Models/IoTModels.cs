using System.Text.Json.Serialization;

namespace Verdure.Assistant.Core.Models;

/// <summary>
/// IoT设备参数类型
/// </summary>
public enum IoTValueType
{
    String,
    Number,
    Boolean,
    Object
}

/// <summary>
/// IoT设备方法参数
/// </summary>
public class IoTParameter
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public IoTValueType Type { get; set; }

    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("value")]
    public object? Value { get; set; }

    public IoTParameter() { }

    public IoTParameter(string name, string description, IoTValueType type, bool required = false)
    {
        Name = name;
        Description = description;
        Type = type;
        Required = required;
    }

    public T? GetValue<T>()
    {
        if (Value == null) return default(T);
        
        try
        {
            if (Value is T directValue)
                return directValue;
                
            return (T)Convert.ChangeType(Value, typeof(T));
        }
        catch
        {
            return default(T);
        }
    }
}

/// <summary>
/// IoT设备方法描述
/// </summary>
public class IoTMethod
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("parameters")]
    public List<IoTParameter> Parameters { get; set; } = new();

    public Func<Dictionary<string, IoTParameter>, Task<object?>>? Handler { get; set; }

    public IoTMethod() { }

    public IoTMethod(string name, string description, List<IoTParameter>? parameters = null, Func<Dictionary<string, IoTParameter>, Task<object?>>? handler = null)
    {
        Name = name;
        Description = description;
        Parameters = parameters ?? new List<IoTParameter>();
        Handler = handler;
    }
}

/// <summary>
/// IoT设备属性描述
/// </summary>
public class IoTProperty
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public IoTValueType Type { get; set; }

    [JsonPropertyName("readable")]
    public bool Readable { get; set; } = true;

    [JsonPropertyName("writable")]
    public bool Writable { get; set; } = false;

    [JsonPropertyName("value")]
    public object? Value { get; set; }

    public IoTProperty() { }

    public IoTProperty(string name, string description, IoTValueType type, bool readable = true, bool writable = false)
    {
        Name = name;
        Description = description;
        Type = type;
        Readable = readable;
        Writable = writable;
    }
}

/// <summary>
/// IoT设备描述符
/// </summary>
public class IoTDeviceDescriptor
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("properties")]
    public List<IoTProperty> Properties { get; set; } = new();

    [JsonPropertyName("methods")]
    public List<IoTMethod> Methods { get; set; } = new();
}

/// <summary>
/// IoT命令
/// </summary>
public class IoTCommand
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("parameters")]
    public Dictionary<string, object> Parameters { get; set; } = new();
}

/// <summary>
/// IoT命令执行结果
/// </summary>
public class IoTCommandResult
{
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public object? Data { get; set; }

    [JsonPropertyName("device")]
    public string Device { get; set; } = string.Empty;

    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    public static IoTCommandResult CreateSuccess(string message, object? data = null, string device = "", string method = "")
    {
        return new IoTCommandResult
        {
            Success = true,
            Message = message,
            Data = data,
            Device = device,
            Method = method
        };
    }

    public static IoTCommandResult CreateError(string message, string device = "", string method = "")
    {
        return new IoTCommandResult
        {
            Success = false,
            Message = message,
            Device = device,
            Method = method
        };
    }
}
