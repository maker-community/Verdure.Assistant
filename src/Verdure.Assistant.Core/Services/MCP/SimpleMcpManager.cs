using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Verdure.Assistant.Core.Models;
using System.Text.Json;

namespace Verdure.Assistant.Core.Services.MCP;

/// <summary>
/// 简化的MCP管理器 - 参考xiaozhi-esp32的简单设计模式
/// 统一管理所有MCP功能，在构造函数中立即初始化工具
/// </summary>
public class SimpleMcpManager : IMcpIntegration
{
    private readonly ILogger<SimpleMcpManager> _logger;
    private readonly McpServer _mcpServer;
    private readonly Dictionary<string, McpTool> _tools = new();
    private readonly Dictionary<string, object> _deviceStates = new();

    /// <summary>
    /// 参考ESP32的设计，在构造函数中立即初始化所有工具
    /// </summary>
    public SimpleMcpManager(
        ILogger<SimpleMcpManager>? logger = null,
        McpServer? mcpServer = null)
    {
        _logger = logger ?? NullLogger<SimpleMcpManager>.Instance;
        _mcpServer = mcpServer ?? new McpServer(NullLogger<McpServer>.Instance);

        // 立即初始化所有工具（像ESP32那样）
        InitializeTools();
        
        _logger.LogInformation("SimpleMcpManager initialized with {ToolCount} tools", _tools.Count);
    }

    /// <summary>
    /// 在构造函数中立即初始化所有工具，避免复杂的异步初始化
    /// </summary>
    private void InitializeTools()
    {
        try
        {
            // 音乐播放器工具
            RegisterMusicPlayerTools();
            
            // 摄像头控制工具
            RegisterCameraTools();
            
            // 设备状态工具
            RegisterDeviceStatusTools();
            
            _logger.LogInformation("All MCP tools registered successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize some MCP tools");
        }
    }

    /// <summary>
    /// 注册音乐播放器相关工具
    /// </summary>
    private void RegisterMusicPlayerTools()
    {
        // 播放音乐工具
        var playMusicProperties = new McpPropertyList
        {
            new McpProperty("song", McpPropertyType.String, "要播放的歌曲名称"),
            new McpProperty("artist", McpPropertyType.String, "", "歌手名称") { Required = false },
            new McpProperty("album", McpPropertyType.String, "", "专辑名称") { Required = false }
        };

        var playMusicTool = new McpTool("play_music", "播放指定的音乐", playMusicProperties, 
            async (props) => await ExecutePlayMusicInternalAsync(props));
        _tools[playMusicTool.Name] = playMusicTool;

        // 暂停音乐工具
        var pauseMusicTool = new McpTool("pause_music", "暂停当前播放的音乐", new McpPropertyList(),
            async (_) => await ExecutePauseMusicInternalAsync());
        _tools[pauseMusicTool.Name] = pauseMusicTool;

        // 停止音乐工具
        var stopMusicTool = new McpTool("stop_music", "停止当前播放的音乐", new McpPropertyList(),
            async (_) => await ExecuteStopMusicInternalAsync());
        _tools[stopMusicTool.Name] = stopMusicTool;

        _logger.LogDebug("Music player tools registered");
    }

    /// <summary>
    /// 注册摄像头控制工具
    /// </summary>
    private void RegisterCameraTools()
    {
        var cameraProperties = new McpPropertyList
        {
            new McpProperty("action", McpPropertyType.String, "摄像头操作: on, off, photo, video_start, video_stop"),
            new McpProperty("duration", McpPropertyType.Integer, 10, "录制时长（秒）") { Required = false }
        };

        var cameraControlTool = new McpTool("control_camera", "控制摄像头操作", cameraProperties,
            async (props) => await ExecuteCameraControlInternalAsync(props));
        _tools[cameraControlTool.Name] = cameraControlTool;

        _logger.LogDebug("Camera control tools registered");
    }

    /// <summary>
    /// 注册设备状态工具
    /// </summary>
    private void RegisterDeviceStatusTools()
    {
        var deviceStatusProperties = new McpPropertyList
        {
            new McpProperty("device", McpPropertyType.String, "all", "设备类型: all, music_player, camera, system") { Required = false }
        };

        var deviceStatusTool = new McpTool("get_device_status", "获取设备状态信息", deviceStatusProperties,
            async (props) => await Task.FromResult(ExecuteGetDeviceStatusInternalAsync(props)));
        _tools[deviceStatusTool.Name] = deviceStatusTool;

        _logger.LogDebug("Device status tools registered");
    }

    /// <summary>
    /// 获取所有可用的语音聊天函数
    /// </summary>
    public List<VoiceChatFunction> GetVoiceChatFunctions()
    {
        var functions = new List<VoiceChatFunction>();

        foreach (var tool in _tools.Values)
        {
            try
            {
                var function = new VoiceChatFunction
                {
                    Name = tool.Name,
                    Description = tool.Description,
                    Parameters = ConvertInputSchemaToFunctionParameters(tool.InputSchema)
                };
                functions.Add(function);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to convert MCP tool {ToolName} to voice chat function", tool.Name);
            }
        }

        return functions;
    }

    /// <summary>
    /// 执行指定的工具
    /// </summary>
    public async Task<McpToolCallResult> ExecuteToolAsync(string toolName, Dictionary<string, object>? parameters = null)
    {
        try
        {
            if (!_tools.ContainsKey(toolName))
            {
                return McpToolCallResult.CreateError($"Tool '{toolName}' not found");
            }

            var tool = _tools[toolName];
            var props = ConvertParametersToPropertyList(tool.Properties, parameters ?? new Dictionary<string, object>());
            
            if (tool.Handler != null)
            {
                var result = await tool.Handler(props);
                return McpToolCallResult.CreateSuccess(result.Value?.ToString() ?? "Success", result.Value);
            }

            return McpToolCallResult.CreateError("Tool handler not found");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute tool {ToolName}", toolName);
            return McpToolCallResult.CreateError($"Tool execution failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 内部播放音乐逻辑
    /// </summary>
    private async Task<McpReturnValue> ExecutePlayMusicInternalAsync(McpPropertyList props)
    {
        var song = props.FirstOrDefault(p => p.Name == "song")?.Value?.ToString();
        if (string.IsNullOrEmpty(song))
        {
            throw new ArgumentException("Song name is required");
        }

        _deviceStates["music_player_status"] = "playing";
        _deviceStates["current_song"] = song;

        await Task.Delay(100); // 模拟异步操作
        return new McpReturnValue { Value = $"正在播放: {song}" };
    }

    /// <summary>
    /// 内部暂停音乐逻辑
    /// </summary>
    private async Task<McpReturnValue> ExecutePauseMusicInternalAsync()
    {
        _deviceStates["music_player_status"] = "paused";
        await Task.Delay(50);
        return new McpReturnValue { Value = "音乐已暂停" };
    }

    /// <summary>
    /// 内部停止音乐逻辑
    /// </summary>
    private async Task<McpReturnValue> ExecuteStopMusicInternalAsync()
    {
        _deviceStates["music_player_status"] = "stopped";
        _deviceStates.Remove("current_song");
        await Task.Delay(50);
        return new McpReturnValue { Value = "音乐已停止" };
    }

    /// <summary>
    /// 内部摄像头控制逻辑
    /// </summary>
    private async Task<McpReturnValue> ExecuteCameraControlInternalAsync(McpPropertyList props)
    {
        var action = props.FirstOrDefault(p => p.Name == "action")?.Value?.ToString();
        if (string.IsNullOrEmpty(action))
        {
            throw new ArgumentException("Action is required");
        }

        string result = action.ToLower() switch
        {
            "on" => "摄像头已开启",
            "off" => "摄像头已关闭",
            "photo" => "已拍照",
            "video_start" => "开始录制视频",
            "video_stop" => "停止录制视频",
            _ => throw new ArgumentException($"Unknown camera action: {action}")
        };

        _deviceStates["camera_status"] = action.ToLower() == "video_start" ? "recording" : 
                                        action.ToLower() == "off" ? "off" : "on";

        if (action.ToLower() == "photo" || action.ToLower() == "video_stop")
        {
            _deviceStates["camera_last_action"] = action.ToLower();
        }

        await Task.Delay(100);
        return new McpReturnValue { Value = result };
    }

    /// <summary>
    /// 内部获取设备状态逻辑
    /// </summary>
    private McpReturnValue ExecuteGetDeviceStatusInternalAsync(McpPropertyList props)
    {
        var device = props.FirstOrDefault(p => p.Name == "device")?.Value?.ToString() ?? "all";
        
        var status = new Dictionary<string, object>();
        
        switch (device.ToLower())
        {
            case "music_player":
                status["music_player_status"] = _deviceStates.TryGetValue("music_player_status", out var mpStatus) ? mpStatus : "stopped";
                if (_deviceStates.TryGetValue("current_song", out var currentSong))
                {
                    status["current_song"] = currentSong;
                }
                break;
            case "camera":
                status["camera_status"] = _deviceStates.TryGetValue("camera_status", out var camStatus) ? camStatus : "off";
                if (_deviceStates.TryGetValue("camera_last_action", out var lastAction))
                {
                    status["camera_last_action"] = lastAction;
                }
                break;
            case "system":
                status["system_status"] = "running";
                status["tools_count"] = _tools.Count;
                break;
            case "all":
            default:
                status = new Dictionary<string, object>(_deviceStates)
                {
                    ["system_status"] = "running",
                    ["tools_count"] = _tools.Count
                };
                break;
        }

        var statusJson = JsonSerializer.Serialize(status, new JsonSerializerOptions { WriteIndented = true });
        return new McpReturnValue { Value = $"设备状态:\n{statusJson}" };
    }

    /// <summary>
    /// 获取设备状态
    /// </summary>
    public Dictionary<string, object> GetDeviceStates()
    {
        return new Dictionary<string, object>(_deviceStates)
        {
            ["system_status"] = "running",
            ["tools_count"] = _tools.Count
        };
    }

    /// <summary>
    /// 获取所有已注册的工具（用于向后兼容）
    /// </summary>
    public Dictionary<string, McpTool> GetAllTools()
    {
        return new Dictionary<string, McpTool>(_tools);
    }

    /// <summary>
    /// 处理MCP请求
    /// </summary>
    public async Task<string> HandleRequestAsync(string jsonRequest)
    {
        try
        {
            return await _mcpServer.HandleRequestAsync(jsonRequest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle MCP request");
            return $"{{\"jsonrpc\":\"2.0\",\"error\":{{\"code\":-32603,\"message\":\"Internal error: {ex.Message}\"}}}}";
        }
    }

    /// <summary>
    /// 将输入架构转换为函数参数
    /// </summary>
    private static Dictionary<string, object> ConvertInputSchemaToFunctionParameters(object inputSchema)
    {
        if (inputSchema is Dictionary<string, object> schemaDict)
        {
            return schemaDict;
        }

        // 如果输入架构不是字典，返回默认格式
        return new Dictionary<string, object>
        {
            ["type"] = "object",
            ["properties"] = new Dictionary<string, object>()
        };
    }

    /// <summary>
    /// 将参数字典转换为McpPropertyList
    /// </summary>
    private static McpPropertyList ConvertParametersToPropertyList(McpPropertyList templateProperties, Dictionary<string, object> parameters)
    {
        var props = new McpPropertyList();
        
        foreach (var templateProp in templateProperties)
        {
            var prop = new McpProperty(templateProp.Name, templateProp.Type, templateProp.Description)
            {
                Required = templateProp.Required,
                DefaultValue = templateProp.DefaultValue
            };

            if (parameters.TryGetValue(templateProp.Name, out var value))
            {
                prop.Value = value;
            }
            else if (templateProp.DefaultValue != null)
            {
                prop.Value = templateProp.DefaultValue;
            }

            props.Add(prop);
        }

        return props;
    }
}
