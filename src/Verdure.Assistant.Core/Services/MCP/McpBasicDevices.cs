using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Services.MCP;

namespace Verdure.Assistant.Core.Services.MCP;

/// <summary>
/// 智能灯MCP设备 - 对应xiaozhi-esp32的Lamp
/// </summary>
public class McpLampDevice : McpIoTDevice
{
    private readonly ILogger<McpLampDevice>? _deviceLogger;
    
    public McpLampDevice(McpServer mcpServer, ILogger<McpLampDevice>? logger = null) 
        : base(mcpServer, logger)
    {
        _deviceLogger = logger;
        Name = "Lamp";
        Description = "智能台灯";
        Type = "light";
        DeviceId = "smart_lamp_001";
        
        // 初始化设备状态
        SetState("power", false);
        SetState("brightness", 50);
        SetState("color", "white");
    }
    
    protected override void RegisterTools()
    {
        // 添加设备状态获取工具
        AddGetDeviceStatusTool();
        
        // 打开灯
        _mcpServer.AddTool(
            "self.lamp.turn_on",
            "打开台灯",
            new McpPropertyList(),
            async (properties) =>
            {
                SetState("power", true);
                _deviceLogger?.LogInformation("智能灯已打开");
                return await Task.FromResult<McpReturnValue>("台灯已打开");
            });
        
        // 关闭灯
        _mcpServer.AddTool(
            "self.lamp.turn_off",
            "关闭台灯",
            new McpPropertyList(),
            async (properties) =>
            {
                SetState("power", false);
                _deviceLogger?.LogInformation("智能灯已关闭");
                return await Task.FromResult<McpReturnValue>("台灯已关闭");
            });
        
        // 设置亮度
        _mcpServer.AddTool(
            "self.lamp.set_brightness",
            "设置台灯亮度",
            new McpPropertyList
            {
                new("brightness", 0, 100, "亮度值(0-100)")
            },
            async (properties) =>
            {
                var brightness = properties["brightness"].GetValue<int>();
                SetState("brightness", brightness);
                _deviceLogger?.LogInformation("台灯亮度已设置为: {Brightness}", brightness);
                return await Task.FromResult<McpReturnValue>($"台灯亮度已设置为 {brightness}%");
            });
        
        // 设置颜色
        _mcpServer.AddTool(
            "self.lamp.set_color",
            "设置台灯颜色",
            new McpPropertyList
            {
                new McpProperty("color", McpPropertyType.String, "颜色名称")
                {
                    EnumValues = new[] { "white", "red", "green", "blue", "yellow", "purple" }
                }
            },
            async (properties) =>
            {
                var color = properties["color"].GetValue<string>() ?? "white";
                SetState("color", color);
                _deviceLogger?.LogInformation("台灯颜色已设置为: {Color}", color);
                return await Task.FromResult<McpReturnValue>($"台灯颜色已设置为 {color}");
            });
    }
}

/// <summary>
/// 智能音箱MCP设备 - 对应xiaozhi-esp32的Speaker
/// </summary>
public class McpSpeakerDevice : McpIoTDevice
{
    private readonly ILogger<McpSpeakerDevice>? _deviceLogger;
    
    public McpSpeakerDevice(McpServer mcpServer, ILogger<McpSpeakerDevice>? logger = null) 
        : base(mcpServer, logger)
    {
        _deviceLogger = logger;
        Name = "Speaker";
        Description = "智能音箱";
        Type = "speaker";
        DeviceId = "smart_speaker_001";
        
        // 初始化设备状态
        SetState("power", false);
        SetState("volume", 50);
        SetState("muted", false);
    }
    
    protected override void RegisterTools()
    {
        // 添加设备状态获取工具
        AddGetDeviceStatusTool();
        
        // 打开音箱
        _mcpServer.AddTool(
            "self.speaker.turn_on",
            "打开音箱",
            new McpPropertyList(),
            async (properties) =>
            {
                SetState("power", true);
                _deviceLogger?.LogInformation("音箱已打开");
                return await Task.FromResult<McpReturnValue>("音箱已打开");
            });
        
        // 关闭音箱
        _mcpServer.AddTool(
            "self.speaker.turn_off",
            "关闭音箱",
            new McpPropertyList(),
            async (properties) =>
            {
                SetState("power", false);
                _deviceLogger?.LogInformation("音箱已关闭");
                return await Task.FromResult<McpReturnValue>("音箱已关闭");
            });
        
        // 设置音量
        _mcpServer.AddTool(
            "self.speaker.set_volume",
            "设置音箱音量",
            new McpPropertyList
            {
                new("volume", 0, 100, "音量值(0-100)")
            },
            async (properties) =>
            {
                var volume = properties["volume"].GetValue<int>();
                SetState("volume", volume);
                _deviceLogger?.LogInformation("音箱音量已设置为: {Volume}", volume);
                return await Task.FromResult<McpReturnValue>($"音箱音量已设置为 {volume}%");
            });
        
        // 静音/取消静音
        _mcpServer.AddTool(
            "self.speaker.toggle_mute",
            "切换音箱静音状态",
            new McpPropertyList(),
            async (properties) =>
            {
                var currentMuted = GetState<bool>("muted");
                var newMuted = !currentMuted;
                SetState("muted", newMuted);
                _deviceLogger?.LogInformation("音箱静音状态: {Muted}", newMuted ? "静音" : "取消静音");
                return await Task.FromResult<McpReturnValue>(newMuted ? "音箱已静音" : "音箱已取消静音");
            });
    }
}

/// <summary>
/// 音乐播放器MCP设备 - 对应xiaozhi-esp32的音乐播放功能
/// </summary>
public class McpMusicPlayerDevice : McpIoTDevice
{
    private readonly ILogger<McpMusicPlayerDevice>? _deviceLogger;
    
    public McpMusicPlayerDevice(McpServer mcpServer, ILogger<McpMusicPlayerDevice>? logger = null) 
        : base(mcpServer, logger)
    {
        _deviceLogger = logger;
        Name = "MusicPlayer";
        Description = "音乐播放器";
        Type = "media_player";
        DeviceId = "music_player_001";
        
        // 初始化设备状态
        SetState("playing", false);
        SetState("current_song", "");
        SetState("volume", 70);
        SetState("repeat", false);
        SetState("shuffle", false);
    }
    
    protected override void RegisterTools()
    {
        // 添加设备状态获取工具
        AddGetDeviceStatusTool();
        
        // 播放音乐
        _mcpServer.AddTool(
            "self.music_player.play",
            "播放音乐",
            new McpPropertyList
            {
                new McpProperty("song", McpPropertyType.String, "")
                {
                    Required = false,
                    Description = "歌曲名称（可选）"
                }
            },
            async (properties) =>
            {
                var song = properties.FirstOrDefault(p => p.Name == "song")?.GetValue<string>();
                
                if (!string.IsNullOrEmpty(song))
                {
                    SetState("current_song", song);
                    _deviceLogger?.LogInformation("开始播放: {Song}", song);
                }
                
                SetState("playing", true);
                var message = !string.IsNullOrEmpty(song) ? $"开始播放: {song}" : "音乐播放已开始";
                return await Task.FromResult<McpReturnValue>(message);
            });
        
        // 暂停音乐
        _mcpServer.AddTool(
            "self.music_player.pause",
            "暂停音乐",
            new McpPropertyList(),
            async (properties) =>
            {
                SetState("playing", false);
                _deviceLogger?.LogInformation("音乐已暂停");
                return await Task.FromResult<McpReturnValue>("音乐已暂停");
            });
        
        // 停止音乐
        _mcpServer.AddTool(
            "self.music_player.stop",
            "停止音乐",
            new McpPropertyList(),
            async (properties) =>
            {
                SetState("playing", false);
                SetState("current_song", "");
                _deviceLogger?.LogInformation("音乐已停止");
                return await Task.FromResult<McpReturnValue>("音乐已停止");
            });
        
        // 下一首
        _mcpServer.AddTool(
            "self.music_player.next",
            "播放下一首",
            new McpPropertyList(),
            async (properties) =>
            {
                _deviceLogger?.LogInformation("切换到下一首");
                return await Task.FromResult<McpReturnValue>("已切换到下一首");
            });
        
        // 上一首
        _mcpServer.AddTool(
            "self.music_player.previous",
            "播放上一首",
            new McpPropertyList(),
            async (properties) =>
            {
                _deviceLogger?.LogInformation("切换到上一首");
                return await Task.FromResult<McpReturnValue>("已切换到上一首");
            });
        
        // 设置音量
        _mcpServer.AddTool(
            "self.music_player.set_volume",
            "设置播放器音量",
            new McpPropertyList
            {
                new("volume", 0, 100, "音量值(0-100)")
            },
            async (properties) =>
            {
                var volume = properties["volume"].GetValue<int>();
                SetState("volume", volume);
                _deviceLogger?.LogInformation("播放器音量已设置为: {Volume}", volume);
                return await Task.FromResult<McpReturnValue>($"播放器音量已设置为 {volume}%");
            });
        
        // 切换重复模式
        _mcpServer.AddTool(
            "self.music_player.toggle_repeat",
            "切换重复播放",
            new McpPropertyList(),
            async (properties) =>
            {
                var currentRepeat = GetState<bool>("repeat");
                var newRepeat = !currentRepeat;
                SetState("repeat", newRepeat);
                _deviceLogger?.LogInformation("重复播放: {Repeat}", newRepeat ? "开启" : "关闭");
                return await Task.FromResult<McpReturnValue>(newRepeat ? "重复播放已开启" : "重复播放已关闭");
            });
        
        // 切换随机播放
        _mcpServer.AddTool(
            "self.music_player.toggle_shuffle",
            "切换随机播放",
            new McpPropertyList(),
            async (properties) =>
            {
                var currentShuffle = GetState<bool>("shuffle");
                var newShuffle = !currentShuffle;
                SetState("shuffle", newShuffle);
                _deviceLogger?.LogInformation("随机播放: {Shuffle}", newShuffle ? "开启" : "关闭");
                return await Task.FromResult<McpReturnValue>(newShuffle ? "随机播放已开启" : "随机播放已关闭");
            });
    }
}
