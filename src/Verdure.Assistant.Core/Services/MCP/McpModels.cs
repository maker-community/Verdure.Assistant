using System.Text.Json.Serialization;

namespace Verdure.Assistant.Core.Services.MCP;

/// <summary>
/// MCP属性类型 - 对应xiaozhi-esp32的PropertyType
/// </summary>
public enum McpPropertyType
{
    Boolean,
    Integer,
    String
}

/// <summary>
/// MCP工具返回值类型 - 对应xiaozhi-esp32的ReturnValue
/// </summary>
public class McpReturnValue
{
    public object? Value { get; set; }

    public static implicit operator McpReturnValue(bool value) => new() { Value = value };
    public static implicit operator McpReturnValue(int value) => new() { Value = value };
    public static implicit operator McpReturnValue(string value) => new() { Value = value };

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
/// MCP属性 - 对应xiaozhi-esp32的Property类
/// </summary>
public class McpProperty
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public McpPropertyType Type { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("required")]
    public bool Required { get; set; } = true;

    [JsonPropertyName("default")]
    public object? DefaultValue { get; set; }
    // 整数类型的范围限制
    [JsonPropertyName("minimum")]
    public int? MinValue { get; set; }

    [JsonPropertyName("maximum")]
    public int? MaxValue { get; set; }

    // Backward compatibility properties for McpIntegrationService
    public double? Minimum => MinValue;
    public double? Maximum => MaxValue;
    public IEnumerable<string>? EnumValues { get; set; }

    public object? Value { get; set; }

    public McpProperty() { }

    // Required property constructor
    public McpProperty(string name, McpPropertyType type, string description = "")
    {
        Name = name;
        Type = type;
        Description = description;
        Required = true;
    }

    // Optional property constructor with default value
    public McpProperty(string name, McpPropertyType type, object defaultValue, string description = "")
    {
        Name = name;
        Type = type;
        Description = description;
        Required = false;
        DefaultValue = defaultValue;
        Value = defaultValue;
    }

    // Integer property with range constructor
    public McpProperty(string name, int defaultValue, int minValue, int maxValue, string description = "")
    {
        Name = name;
        Type = McpPropertyType.Integer;
        Description = description;
        Required = false;
        DefaultValue = defaultValue;
        Value = defaultValue;
        MinValue = minValue;
        MaxValue = maxValue;

        if (defaultValue < minValue || defaultValue > maxValue)
        {
            throw new ArgumentException("Default value must be within the specified range");
        }
    }

    // Required integer property with range constructor
    public McpProperty(string name, int minValue, int maxValue, string description = "")
    {
        Name = name;
        Type = McpPropertyType.Integer;
        Description = description;
        Required = true;
        MinValue = minValue;
        MaxValue = maxValue;
    }

    public void SetValue(object? value)
    {
        // 范围检查（针对整数类型）
        if (Type == McpPropertyType.Integer && value is int intValue)
        {
            if (MinValue.HasValue && intValue < MinValue.Value)
            {
                throw new ArgumentException($"Value {intValue} is below minimum allowed: {MinValue.Value}");
            }
            if (MaxValue.HasValue && intValue > MaxValue.Value)
            {
                throw new ArgumentException($"Value {intValue} exceeds maximum allowed: {MaxValue.Value}");
            }
        }

        Value = value;
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
/// MCP属性列表 - 对应xiaozhi-esp32的PropertyList
/// </summary>
public class McpPropertyList : List<McpProperty>
{
    public McpPropertyList() { }

    public McpPropertyList(IEnumerable<McpProperty> properties) : base(properties) { }

    public McpProperty this[string name]
    {
        get
        {
            var property = this.FirstOrDefault(p => p.Name == name);
            if (property == null)
            {
                throw new KeyNotFoundException($"Property not found: {name}");
            }
            return property;
        }
    }

    public List<string> GetRequired()
    {
        return this.Where(p => p.Required && p.DefaultValue == null)
                  .Select(p => p.Name)
                  .ToList();
    }

    public bool TryGetProperty(string name, out McpProperty? property)
    {
        property = this.FirstOrDefault(p => p.Name == name);
        return property != null;
    }
}


/// <summary>
/// MCP工具 - 对应xiaozhi-esp32的McpTool
/// </summary>
public class SimpleMcpTool
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("inputSchema")]
    public object InputSchema { get; set; } = new();
}


/// <summary>
/// MCP工具 - 对应xiaozhi-esp32的McpTool
/// </summary>
public class McpTool
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("inputSchema")]
    public object InputSchema { get; set; } = new();

    public McpPropertyList Properties { get; set; } = new();

    public Func<McpPropertyList, Task<McpReturnValue>>? Handler { get; set; }

    public McpTool() { }

    public McpTool(string name, string description, McpPropertyList properties, Func<McpPropertyList, Task<McpReturnValue>> handler)
    {
        Name = name;
        Description = description;
        Properties = properties;
        Handler = handler;

        // 构建输入架构
        BuildInputSchema();
    }

    private void BuildInputSchema()
    {
        var required = Properties.GetRequired();
        var propertiesObj = new Dictionary<string, object>();

        foreach (var prop in Properties)
        {
            var propSchema = new Dictionary<string, object>
            {
                ["type"] = prop.Type switch
                {
                    McpPropertyType.Boolean => "boolean",
                    McpPropertyType.Integer => "integer",
                    McpPropertyType.String => "string",
                    _ => "string"
                }
            };

            if (!string.IsNullOrEmpty(prop.Description))
            {
                propSchema["description"] = prop.Description;
            }

            if (prop.DefaultValue != null)
            {
                propSchema["default"] = prop.DefaultValue;
            }

            if (prop.Type == McpPropertyType.Integer)
            {
                if (prop.MinValue.HasValue)
                    propSchema["minimum"] = prop.MinValue.Value;
                if (prop.MaxValue.HasValue)
                    propSchema["maximum"] = prop.MaxValue.Value;
            }

            propertiesObj[prop.Name] = propSchema;
        }

        var schema = new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = propertiesObj
        };

        if (required.Count > 0)
        {
            schema["required"] = required;
        }

        InputSchema = schema;
    }

    public async Task<McpToolCallResult> CallAsync(Dictionary<string, object> arguments)
    {
        if (Handler == null)
        {
            return McpToolCallResult.CreateError($"Tool {Name} has no handler");
        }

        try
        {
            // 准备属性值
            var propertyList = new McpPropertyList();

            foreach (var prop in Properties)
            {
                var newProp = new McpProperty(prop.Name, prop.Type, prop.Description)
                {
                    Required = prop.Required,
                    DefaultValue = prop.DefaultValue,
                    MinValue = prop.MinValue,
                    MaxValue = prop.MaxValue
                };

                if (arguments.TryGetValue(prop.Name, out var value))
                {
                    newProp.SetValue(value);
                }
                else if (prop.DefaultValue != null)
                {
                    newProp.SetValue(prop.DefaultValue);
                }
                else if (prop.Required)
                {
                    return McpToolCallResult.CreateError($"Missing required parameter: {prop.Name}");
                }

                propertyList.Add(newProp);
            }

            // 调用处理器
            var result = await Handler(propertyList);

            return McpToolCallResult.CreateSuccess(result.Value?.ToString() ?? "Success", result.Value);
        }
        catch (Exception ex)
        {
            return McpToolCallResult.CreateError($"Tool execution failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 执行工具 - ExecuteAsync alias for CallAsync
    /// </summary>
    public async Task<McpToolCallResult> ExecuteAsync(Dictionary<string, object> arguments)
    {
        return await CallAsync(arguments);
    }
}

/// <summary>
/// MCP工具调用结果
/// </summary>
public class McpToolCallResult
{
    [JsonPropertyName("content")]
    public List<McpContent> Content { get; set; } = new();

    [JsonPropertyName("isError")]
    public bool IsError { get; set; }

    public static McpToolCallResult CreateSuccess(string message, object? data = null)
    {
        return new McpToolCallResult
        {
            Content = new List<McpContent>
            {
                new()
                {
                    Type = "text",
                    Text = message
                }
            },
            IsError = false
        };
    }

    public static McpToolCallResult CreateError(string message)
    {
        return new McpToolCallResult
        {
            Content = new List<McpContent>
            {
                new()
                {
                    Type = "text",
                    Text = message
                }
            },
            IsError = true
        };
    }
}

/// <summary>
/// MCP内容
/// </summary>
public class McpContent
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// JSON-RPC 2.0 请求
/// </summary>
public class JsonRpcRequest
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("params")]
    public object? Params { get; set; }

    [JsonPropertyName("id")]
    public int Id { get; set; }
}

/// <summary>
/// JSON-RPC 2.0 响应
/// </summary>
public class JsonRpcResponse
{
    [JsonPropertyName("jsonrpc")]
    public string JsonRpc { get; set; } = "2.0";

    [JsonPropertyName("result")]
    public object? Result { get; set; }

    [JsonPropertyName("error")]
    public JsonRpcError? Error { get; set; }

    [JsonPropertyName("id")]
    public int Id { get; set; }
}

/// <summary>
/// JSON-RPC 2.0 错误
/// </summary>
public class JsonRpcError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public object? Data { get; set; }
}
