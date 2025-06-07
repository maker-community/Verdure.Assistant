using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Services.MCP;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Models;

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
        DeviceId = "living_room_lamp";
        Name = "Lamp";
        Description = "智能台灯";
        Type = "light";
        
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
                return await Task.FromResult("台灯已关闭");
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
                return await Task.FromResult($"台灯亮度已设置为 {brightness}%");
            });
          // 设置颜色
        _mcpServer.AddTool(
            "self.lamp.set_color",
            "设置台灯颜色",
            new McpPropertyList
            {
                new("color", McpPropertyType.String, "颜色名称(red/green/blue/white/yellow)")
            },
            async (properties) =>
            {
                var color = properties["color"].GetValue<string>() ?? "white";
                SetState("color", color);
                _deviceLogger?.LogInformation("台灯颜色已设置为: {Color}", color);
                return await Task.FromResult($"台灯颜色已设置为 {color}");
            });
    }
}

/// <summary>
/// 扬声器MCP设备 - 对应xiaozhi-esp32的Speaker
/// </summary>
public class McpSpeakerDevice : McpIoTDevice
{
    private readonly ILogger<McpSpeakerDevice>? _deviceLogger;
      public McpSpeakerDevice(McpServer mcpServer, ILogger<McpSpeakerDevice>? logger = null) 
        : base(mcpServer, logger)
    {
        _deviceLogger = logger;
        DeviceId = "main_speaker";
        Name = "Speaker";
        Description = "智能扬声器";
        Type = "speaker";
        
        // 初始化设备状态
        SetState("volume", 50);
        SetState("muted", false);
        SetState("playing", false);
    }
    
    protected override void RegisterTools()
    {
        // 添加设备状态获取工具
        AddGetDeviceStatusTool();
          // 设置音量
        _mcpServer.AddTool(
            "self.audio_speaker.set_volume",
            "设置扬声器音量。如果当前音量未知，请先调用获取设备状态工具。",
            new McpPropertyList
            {
                new("volume", 0, 100, "音量值(0-100)")
            },
            async (properties) =>
            {
                var volume = properties["volume"].GetValue<int>();
                SetState("volume", volume);
                SetState("muted", false); // 设置音量时取消静音
                _deviceLogger?.LogInformation("扬声器音量已设置为: {Volume}", volume);
                return await Task.FromResult($"扬声器音量已设置为 {volume}%");
            });
          // 静音
        _mcpServer.AddTool(
            "self.speaker.mute",
            "将扬声器静音",
            new McpPropertyList(),
            async (properties) =>
            {
                SetState("muted", true);
                _deviceLogger?.LogInformation("扬声器已静音");
                return await Task.FromResult("扬声器已静音");
            });
        
        // 取消静音
        _mcpServer.AddTool(
            "self.speaker.unmute",
            "取消扬声器静音",
            new McpPropertyList(),
            async (properties) =>
            {
                SetState("muted", false);
                _deviceLogger?.LogInformation("扬声器已取消静音");
                return await Task.FromResult("扬声器已取消静音");
            });
    }
}

/// <summary>
/// 音乐播放器MCP设备 - 对应xiaozhi-esp32的MusicPlayer模式
/// 集成KugouMusicService实现真实的音乐播放功能
/// </summary>
public class McpMusicPlayerDevice : McpIoTDevice
{
    private readonly ILogger<McpMusicPlayerDevice>? _deviceLogger;
    private readonly IMusicPlayerService? _musicService;
      public McpMusicPlayerDevice(McpServer mcpServer, IMusicPlayerService? musicService = null, ILogger<McpMusicPlayerDevice>? logger = null) 
        : base(mcpServer, logger)
    {
        _deviceLogger = logger;
        _musicService = musicService;
        DeviceId = "music_player";
        Name = "music_player";
        Description = "你是默认的酷我智能音乐播放器，所有的酷我音乐播放请求都由你接管 - 支持酷我音乐搜索和播放";
        Type = "media_player";
        
        // 初始化设备状态
        SetState("playing", false);
        SetState("current_song", "");
        SetState("artist", "");
        SetState("progress", 0);
        SetState("duration", 0);
        SetState("volume", 50);
        
        // 如果有音乐服务，订阅事件更新状态
        if (_musicService != null)
        {
            _musicService.PlaybackStateChanged += OnMusicServiceStateChanged;
            _musicService.ProgressUpdated += OnMusicServiceProgressUpdated;
        }
    }
      protected override void RegisterTools()
    {
        // 添加设备状态获取工具
        AddGetDeviceStatusTool();
        
        // 搜索并播放音乐
        _mcpServer.AddTool(
            "self.music_player.search_and_play",
            "使用酷我音乐搜索并播放音乐",
            new McpPropertyList
            {
                new("query", McpPropertyType.String, "搜索关键词（歌曲名、歌手名等）")
            },
            async (properties) =>
            {
                var query = properties["query"].GetValue<string>() ?? "";
                
                if (_musicService != null)
                {
                    try
                    {
                        _deviceLogger?.LogInformation("通过酷我音乐搜索并播放: {Query}", query);
                        var result = await _musicService.SearchAndPlayAsync(query);
                        
                        if (result.Success)
                        {
                            // 更新设备状态
                            SetState("current_song", _musicService.CurrentTrack?.Name ?? query);
                            SetState("artist", _musicService.CurrentTrack?.Artist ?? "");
                            SetState("playing", _musicService.IsPlaying);
                            
                            return await Task.FromResult<McpReturnValue>($"正在播放: {_musicService.CurrentTrack?.Name ?? query}");
                        }
                        else
                        {
                            _deviceLogger?.LogWarning("音乐播放失败: {Message}", result.Message);
                            return await Task.FromResult<McpReturnValue>($"播放失败: {result.Message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _deviceLogger?.LogError(ex, "音乐播放异常");
                        return await Task.FromResult<McpReturnValue>($"播放异常: {ex.Message}");
                    }
                }
                else
                {
                    // 模拟模式
                    SetState("current_song", $"搜索歌曲: {query}");
                    SetState("playing", true);
                    _deviceLogger?.LogInformation("模拟播放音乐: {Query}", query);
                    return await Task.FromResult<McpReturnValue>($"正在播放搜索结果: {query}");
                }
            });
        
        // 播放/暂停
        _mcpServer.AddTool(
            "self.music_player.play_pause",
            "使用酷我音乐播放或暂停音乐",
            new McpPropertyList(),
            async (properties) =>
            {
                if (_musicService != null)
                {
                    try
                    {
                        var result = await _musicService.TogglePlayPauseAsync();
                        if (result.Success)
                        {
                            SetState("playing", _musicService.IsPlaying);
                            var action = _musicService.IsPlaying ? "播放" : "暂停";
                            _deviceLogger?.LogInformation("音乐播放器状态: {Action}", action);
                            return await Task.FromResult<McpReturnValue>($"音乐已{action}");
                        }
                        else
                        {
                            return await Task.FromResult<McpReturnValue>($"操作失败: {result.Message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _deviceLogger?.LogError(ex, "播放/暂停操作异常");
                        return await Task.FromResult<McpReturnValue>($"操作异常: {ex.Message}");
                    }
                }
                else
                {
                    // 模拟模式
                    var isPlaying = GetState<bool>("playing");
                    SetState("playing", !isPlaying);
                    var action = !isPlaying ? "播放" : "暂停";
                    _deviceLogger?.LogInformation("音乐播放器状态: {Action}", action);
                    return await Task.FromResult<McpReturnValue>($"音乐已{action}");
                }
            });
          // 下一首
        _mcpServer.AddTool(
            "self.music_player.next",
            "切换到下一首歌曲",
            new McpPropertyList(),
            async (properties) =>
            {
                // TODO: 实现下一首功能（需要KugouMusicService支持播放列表）
                SetState("current_song", "下一首歌曲");
                SetState("progress", 0);
                _deviceLogger?.LogInformation("切换到下一首歌曲");
                return await Task.FromResult<McpReturnValue>("已切换到下一首歌曲");
            });
          // 上一首
        _mcpServer.AddTool(
            "self.music_player.previous",
            "切换到上一首歌曲",
            new McpPropertyList(),
            async (properties) =>
            {
                // TODO: 实现上一首功能（需要KugouMusicService支持播放列表）
                SetState("current_song", "上一首歌曲");
                SetState("progress", 0);
                _deviceLogger?.LogInformation("切换到上一首歌曲");
                return await Task.FromResult<McpReturnValue>("已切换到上一首歌曲");
            });
        
        // 设置音量
        _mcpServer.AddTool(
            "self.music_player.set_volume",
            "设置音乐播放器音量",
            new McpPropertyList
            {
                new("volume", 0, 100, "音量值(0-100)")
            },
            async (properties) =>
            {
                var volume = properties["volume"].GetValue<int>();
                
                if (_musicService != null)
                {
                    try
                    {
                        var result = await _musicService.SetVolumeAsync(volume);
                        if (result.Success)
                        {
                            SetState("volume", volume);
                            _deviceLogger?.LogInformation("音乐播放器音量已设置为: {Volume}", volume);
                            return await Task.FromResult<McpReturnValue>($"音乐播放器音量已设置为 {volume}%");
                        }
                        else
                        {
                            return await Task.FromResult<McpReturnValue>($"设置音量失败: {result.Message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _deviceLogger?.LogError(ex, "设置音量异常");
                        return await Task.FromResult<McpReturnValue>($"设置音量异常: {ex.Message}");
                    }
                }
                else
                {
                    // 模拟模式
                    SetState("volume", volume);
                    _deviceLogger?.LogInformation("音乐播放器音量已设置为: {Volume}", volume);
                    return await Task.FromResult<McpReturnValue>($"音乐播放器音量已设置为 {volume}%");
                }
            });
    }
    
    // 事件处理方法
    private void OnMusicServiceStateChanged(object? sender, MusicPlaybackEventArgs e)
    {
        try
        {
            SetState("playing", e.Status == "Playing");
            SetState("current_song", e.Track?.Name ?? "");
            SetState("artist", e.Track?.Artist ?? "");
            _deviceLogger?.LogDebug("音乐播放状态更新: {Status}", e.Status);
        }
        catch (Exception ex)
        {
            _deviceLogger?.LogError(ex, "处理音乐状态变化事件失败");
        }
    }
    
    private void OnMusicServiceProgressUpdated(object? sender, ProgressUpdateEventArgs e)
    {
        try
        {
            SetState("progress", (int)e.Position);
            SetState("duration", (int)e.Duration);
            _deviceLogger?.LogDebug("音乐播放进度更新: {Position}/{Duration}", e.Position, e.Duration);
        }
        catch (Exception ex)
        {
            _deviceLogger?.LogError(ex, "处理音乐进度更新事件失败");
        }
    }
}
