using Microsoft.Extensions.Logging;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Verdure.Assistant.Core.Services.MCP;

/// <summary>
/// MCP服务器 - 对应xiaozhi-esp32的McpServer
/// </summary>
public class McpServer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private readonly ILogger<McpServer>? _logger;
    private readonly List<McpTool> _tools = new();

    public McpServer(ILogger<McpServer>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 添加工具
    /// </summary>
    public void AddTool(McpTool tool)
    {
        // 防止添加重复工具
        if (_tools.Any(t => t.Name == tool.Name))
        {
            _logger?.LogWarning("Tool {ToolName} already exists, replacing", tool.Name);
            RemoveTool(tool.Name);
        }

        _tools.Add(tool);
        _logger?.LogInformation("Added MCP tool: {ToolName}", tool.Name);
    }

    /// <summary>
    /// 添加工具（简化版本）
    /// </summary>
    public void AddTool(string name, string description, McpPropertyList properties, Func<McpPropertyList, Task<McpReturnValue>> handler)
    {
        var tool = new McpTool(name, description, properties, handler);
        AddTool(tool);
    }

    /// <summary>
    /// 移除工具
    /// </summary>
    public bool RemoveTool(string toolName)
    {
        var tool = _tools.FirstOrDefault(t => t.Name == toolName);
        if (tool != null)
        {
            _tools.Remove(tool);
            _logger?.LogInformation("Removed MCP tool: {ToolName}", toolName);
            return true;
        }
        return false;
    }

    /// <summary>
    /// 获取所有工具
    /// </summary>
    public IReadOnlyList<McpTool> GetTools()
    {
        return _tools.AsReadOnly();
    }

    /// <summary>
    /// 获取工具列表的JSON格式 - tools/list 方法
    /// </summary>
    public string GetToolsListJson(string cursor = "")
    {
        var tools = _tools.Select(tool => new
        {
            name = tool.Name,
            description = tool.Description,
            inputSchema = tool.InputSchema
        }).ToList();

        var result = new
        {
            tools = tools,
            nextCursor = "" // 暂不支持分页
        };

        return JsonSerializer.Serialize(result);
    }

    /// <summary>
    /// 调用工具 - tools/call 方法
    /// </summary>
    public async Task<string> CallToolAsync(string toolName, Dictionary<string, object> arguments)
    {
        var tool = _tools.FirstOrDefault(t => t.Name == toolName);
        if (tool == null)
        {
            var errorResult = McpToolCallResult.CreateError($"Tool not found: {toolName}");
            return JsonSerializer.Serialize(errorResult, JsonOptions);
        }

        _logger?.LogDebug("Calling MCP tool: {ToolName} with arguments: {Arguments}",
            toolName, JsonSerializer.Serialize(arguments, JsonOptions));

        var result = await tool.CallAsync(arguments);

        if (result.IsError)
        {
            _logger?.LogWarning("MCP tool call failed: {ToolName} - {Error}",
                toolName, result.Content.FirstOrDefault()?.Text);
        }
        else
        {
            _logger?.LogInformation("MCP tool call succeeded: {ToolName}", toolName);
        }

        return JsonSerializer.Serialize(result, JsonOptions);
    }

    /// <summary>
    /// 处理JSON-RPC消息
    /// </summary>
    public async Task<string> HandleJsonRpcAsync(string jsonMessage)
    {
        try
        {
            var request = JsonSerializer.Deserialize<JsonRpcRequest>(jsonMessage);
            if (request == null)
            {
                return CreateErrorResponse(0, -32700, "Parse error");
            }

            return request.Method switch
            {
                "initialize" => HandleInitialize(request),
                "tools/list" => HandleToolsList(request),
                "tools/call" => await HandleToolsCall(request),
                _ => CreateErrorResponse(request.Id, -32601, $"Method not found: {request.Method}")
            };
        }
        catch (JsonException)
        {
            return CreateErrorResponse(0, -32700, "Parse error");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error handling JSON-RPC message");
            return CreateErrorResponse(0, -32603, "Internal error");
        }
    }

    private string HandleInitialize(JsonRpcRequest request)
    {
        var result = new
        {
            protocolVersion = "2024-11-05",
            capabilities = new
            {
                tools = new { }
            },
            serverInfo = new
            {
                name = "Verdure Assistant MCP Server",
                version = "1.0.0"
            }
        };

        return CreateSuccessResponse(request.Id, result);
    }

    private string HandleToolsList(JsonRpcRequest request)
    {
        var cursor = "";
        if (request.Params is JsonElement paramsElement &&
            paramsElement.TryGetProperty("cursor", out var cursorElement))
        {
            cursor = cursorElement.GetString() ?? "";
        }

        var resultJson = GetToolsListJson(cursor);
        var resultObj = JsonSerializer.Deserialize<object>(resultJson);

        return CreateSuccessResponse(request.Id, resultObj);
    }

    private async Task<string> HandleToolsCall(JsonRpcRequest request)
    {
        try
        {
            if (request.Params is not JsonElement paramsElement)
            {
                return CreateErrorResponse(request.Id, -32602, "Invalid params");
            }

            if (!paramsElement.TryGetProperty("name", out var nameElement))
            {
                return CreateErrorResponse(request.Id, -32602, "Missing tool name");
            }

            var toolName = nameElement.GetString();
            if (string.IsNullOrEmpty(toolName))
            {
                return CreateErrorResponse(request.Id, -32602, "Invalid tool name");
            }

            var arguments = new Dictionary<string, object>();
            if (paramsElement.TryGetProperty("arguments", out var argsElement))
            {
                var argsDict = JsonSerializer.Deserialize<Dictionary<string, object>>(argsElement.GetRawText());
                if (argsDict != null)
                {
                    arguments = argsDict;
                }
            }

            var resultJson = await CallToolAsync(toolName, arguments);
            var resultObj = JsonSerializer.Deserialize<object>(resultJson);

            return CreateSuccessResponse(request.Id, resultObj);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in tools/call handler");
            return CreateErrorResponse(request.Id, -32603, "Internal error");
        }
    }

    private static string CreateSuccessResponse(int id, object? result)
    {
        var response = new JsonRpcResponse
        {
            Id = id,
            Result = result
        };

        return JsonSerializer.Serialize(response, JsonOptions);
    }

    private static string CreateErrorResponse(int id, int code, string message)
    {
        var response = new JsonRpcResponse
        {
            Id = id,
            Error = new JsonRpcError
            {
                Code = code,
                Message = message
            }
        };
        return JsonSerializer.Serialize(response);
    }

    /// <summary>
    /// 初始化MCP服务器
    /// </summary>
    public async Task InitializeAsync()
    {
        _logger?.LogInformation("Initializing MCP Server");
        await Task.CompletedTask; // Placeholder for future initialization logic
    }

    /// <summary>
    /// 处理JSON-RPC 2.0请求
    /// </summary>
    public async Task<string> HandleRequestAsync(string jsonRequest)
    {
        try
        {
            var request = JsonSerializer.Deserialize<JsonRpcRequest>(jsonRequest);
            if (request == null)
            {
                return CreateErrorResponse(0, -32700, "Parse error");
            }

            _logger?.LogDebug("Handling MCP request: {Method}", request.Method);

            switch (request.Method)
            {
                case "tools/list":
                    var toolsResult = GetToolsListJson();
                    return CreateSuccessResponse(request.Id, JsonSerializer.Deserialize<object>(toolsResult));

                case "tools/call":
                    if (request.Params is JsonElement paramsElement)
                    {
                        var toolName = paramsElement.GetProperty("name").GetString() ?? "";
                        var arguments = new Dictionary<string, object>();

                        if (paramsElement.TryGetProperty("arguments", out var argsElement))
                        {
                            foreach (var prop in argsElement.EnumerateObject())
                            {
                                arguments[prop.Name] = prop.Value.GetRawText();
                            }
                        }

                        var callResult = await CallToolAsync(toolName, arguments);
                        return CreateSuccessResponse(request.Id, JsonSerializer.Deserialize<object>(callResult, JsonOptions));
                    }
                    return CreateErrorResponse(request.Id, -32602, "Invalid params");

                default:
                    return CreateErrorResponse(request.Id, -32601, $"Method not found: {request.Method}");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error handling MCP request");
            return CreateErrorResponse(0, -32603, "Internal error");
        }
    }

    /// <summary>
    /// 注册工具到MCP服务器 - 用于兼容性
    /// </summary>
    public async Task RegisterToolsAsync()
    {
        _logger?.LogInformation("Registering {Count} MCP tools", _tools.Count);
        await Task.CompletedTask; // Placeholder for future registration logic
    }
}
