using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Interfaces;

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

/// <summary>
/// 相机MCP设备 - 对应xiaozhi-esp32的Camera
/// 支持拍照和AI图像解释功能
/// </summary>
public class McpCameraDevice : McpIoTDevice
{
    private readonly ILogger<McpCameraDevice>? _deviceLogger;
    private readonly HttpClient _httpClient;
    private string _explainUrl = "http://api.xiaozhi.me/mcp/vision/explain";
    private string _explainToken = "test-token";
    private byte[]? _lastCapturedImage;

    public McpCameraDevice(McpServer mcpServer, ILogger<McpCameraDevice>? logger = null)
        : base(mcpServer, logger)
    {
        _deviceLogger = logger;
        _httpClient = new HttpClient();
        DeviceId = "main_camera";
        Name = "Camera";
        Description = "智能相机设备 - 支持拍照和AI图像分析";
        Type = "camera";

        // 初始化设备状态
        SetState("available", true);
        SetState("last_capture_time", "");
        SetState("image_count", 0);
        SetState("resolution", "640x480");
        SetState("format", "JPEG");
    }

    /// <summary>
    /// 设置AI解释服务的URL和认证令牌
    /// </summary>
    public void SetExplainUrl(string url, string token = "")
    {
        _explainUrl = url;
        _explainToken = token;
        _deviceLogger?.LogInformation("Camera explain URL set to: {Url}", url);
    }

    protected override void RegisterTools()
    {
        // 添加设备状态获取工具
        AddGetDeviceStatusTool();

        // 拍照工具
        _mcpServer.AddTool(
            "self.camera.capture",
            "拍摄一张照片",
            new McpPropertyList(),
            async (properties) =>
            {
                try
                {
                    var success = await CapturePhotoAsync();
                    if (success)
                    {
                        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        SetState("last_capture_time", timestamp);
                        var imageCount = GetState<int>("image_count") + 1;
                        SetState("image_count", imageCount);

                        _deviceLogger?.LogInformation("照片拍摄成功，时间: {Timestamp}", timestamp);
                        return await Task.FromResult<McpReturnValue>($"照片拍摄成功，时间: {timestamp}");
                    }
                    else
                    {
                        _deviceLogger?.LogWarning("照片拍摄失败");
                        return await Task.FromResult<McpReturnValue>("照片拍摄失败，请检查相机是否可用");
                    }
                }
                catch (Exception ex)
                {
                    _deviceLogger?.LogError(ex, "拍照操作异常");
                    return await Task.FromResult<McpReturnValue>($"拍照异常: {ex.Message}");
                }
            });

        // 拍照并解释工具 - 对应ESP32的take_photo工具
        _mcpServer.AddTool(
            "self.camera.take_photo",
            "拍摄照片并使用AI进行分析解释",
            new McpPropertyList
            {
                new("question", McpPropertyType.String, "关于照片的问题或分析要求")
            },
            async (properties) =>
            {
                try
                {
                    var question = properties["question"].GetValue<string>() ?? "请描述这张照片的内容";

                    // 先拍照
                    var captureSuccess = await CapturePhotoAsync();
                    if (!captureSuccess)
                    {
                        return await Task.FromResult<McpReturnValue>("拍照失败，无法进行分析");
                    }

                    // 更新状态
                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    SetState("last_capture_time", timestamp);
                    var imageCount = GetState<int>("image_count") + 1;
                    SetState("image_count", imageCount);

                    // 调用AI解释
                    var explanation = await ExplainPhotoAsync(question);

                    _deviceLogger?.LogInformation("照片拍摄并分析完成，问题: {Question}", question);
                    return await Task.FromResult<McpReturnValue>($"照片分析结果：\n{explanation}");
                }
                catch (Exception ex)
                {
                    _deviceLogger?.LogError(ex, "拍照并分析操作异常");
                    return await Task.FromResult<McpReturnValue>($"操作异常: {ex.Message}");
                }
            });

        // 设置分辨率工具
        _mcpServer.AddTool(
            "self.camera.set_resolution",
            "设置相机分辨率",
            new McpPropertyList
            {
                new("resolution", McpPropertyType.String, "分辨率设置 (640x480, 800x600, 1024x768)")
            },
            async (properties) =>
            {
                var resolution = properties["resolution"].GetValue<string>() ?? "640x480";
                SetState("resolution", resolution);
                _deviceLogger?.LogInformation("相机分辨率已设置为: {Resolution}", resolution);
                return await Task.FromResult<McpReturnValue>($"相机分辨率已设置为: {resolution}");
            });
    }    /// <summary>
         /// 拍摄照片 - 对应ESP32的Capture方法
         /// 在实际应用中可能需要连接真实的摄像头API
         /// </summary>
    private async Task<bool> CapturePhotoAsync()
    {
        try
        {
            // 模拟拍照过程
            // 在实际实现中，这里应该调用系统相机API或USB摄像头API
            await Task.Delay(500); // 模拟拍照延迟

            // 创建一个模拟的JPEG头部和简单图片数据
            // 这是一个最小的有效JPEG文件字节数组（1x1像素的图片）
            _lastCapturedImage = new byte[]
            {
                0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01,
                0x01, 0x01, 0x00, 0x48, 0x00, 0x48, 0x00, 0x00, 0xFF, 0xDB, 0x00, 0x43,
                0x00, 0x08, 0x06, 0x06, 0x07, 0x06, 0x05, 0x08, 0x07, 0x07, 0x07, 0x09,
                0x09, 0x08, 0x0A, 0x0C, 0x14, 0x0D, 0x0C, 0x0B, 0x0B, 0x0C, 0x19, 0x12,
                0x13, 0x0F, 0x14, 0x1D, 0x1A, 0x1F, 0x1E, 0x1D, 0x1A, 0x1C, 0x1C, 0x20,
                0x24, 0x2E, 0x27, 0x20, 0x22, 0x2C, 0x23, 0x1C, 0x1C, 0x28, 0x37, 0x29,
                0x2C, 0x30, 0x31, 0x34, 0x34, 0x34, 0x1F, 0x27, 0x39, 0x3D, 0x38, 0x32,
                0x3C, 0x2E, 0x33, 0x34, 0x32, 0xFF, 0xC0, 0x00, 0x11, 0x08, 0x00, 0x01,
                0x00, 0x01, 0x01, 0x01, 0x11, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01,
                0xFF, 0xC4, 0x00, 0x1F, 0x00, 0x00, 0x01, 0x05, 0x01, 0x01, 0x01, 0x01,
                0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x02,
                0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0xFF, 0xC4, 0x00,
                0xB5, 0x10, 0x00, 0x02, 0x01, 0x03, 0x03, 0x02, 0x04, 0x03, 0x05, 0x05,
                0x04, 0x04, 0x00, 0x00, 0x01, 0x7D, 0x01, 0x02, 0x03, 0x00, 0x04, 0x11,
                0x05, 0x12, 0x21, 0x31, 0x41, 0x06, 0x13, 0x51, 0x61, 0x07, 0x22, 0x71,
                0x14, 0x32, 0x81, 0x91, 0xA1, 0x08, 0x23, 0x42, 0xB1, 0xC1, 0x15, 0x52,
                0xD1, 0xF0, 0x24, 0x33, 0x62, 0x72, 0x82, 0x09, 0x0A, 0x16, 0x17, 0x18,
                0x19, 0x1A, 0x25, 0x26, 0x27, 0x28, 0x29, 0x2A, 0x34, 0x35, 0x36, 0x37,
                0x38, 0x39, 0x3A, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49, 0x4A, 0x53,
                0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5A, 0x63, 0x64, 0x65, 0x66, 0x67,
                0x68, 0x69, 0x6A, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7A, 0x83,
                0x84, 0x85, 0x86, 0x87, 0x88, 0x89, 0x8A, 0x92, 0x93, 0x94, 0x95, 0x96,
                0x97, 0x98, 0x99, 0x9A, 0xA2, 0xA3, 0xA4, 0xA5, 0xA6, 0xA7, 0xA8, 0xA9,
                0xAA, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6, 0xB7, 0xB8, 0xB9, 0xBA, 0xC2, 0xC3,
                0xC4, 0xC5, 0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xD2, 0xD3, 0xD4, 0xD5, 0xD6,
                0xD7, 0xD8, 0xD9, 0xDA, 0xE1, 0xE2, 0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8,
                0xE9, 0xEA, 0xF1, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8, 0xF9, 0xFA,
                0xFF, 0xDA, 0x00, 0x0C, 0x03, 0x01, 0x00, 0x02, 0x11, 0x03, 0x11, 0x00,
                0x3F, 0x00, 0xF2, 0x8A, 0x28, 0xAF, 0xC0, 0x0F, 0xFF, 0xD9
            };

            return true;
        }
        catch (Exception ex)
        {
            _deviceLogger?.LogError(ex, "拍照过程中发生异常");
            return false;
        }
    }

    /// <summary>
    /// 使用AI解释照片 - 对应ESP32的Explain方法
    /// </summary>
    private async Task<string> ExplainPhotoAsync(string question)
    {
        if (string.IsNullOrEmpty(_explainUrl))
        {
            return "AI解释服务未配置，请先设置解释服务URL";
        }

        if (_lastCapturedImage == null || _lastCapturedImage.Length == 0)
        {
            return "没有可分析的照片，请先拍照";
        }

        try
        {
            // 构建multipart/form-data请求 - 对应ESP32的实现
            var boundary = "----CAMERA_BOUNDARY_" + Guid.NewGuid().ToString("N");
            var content = new MultipartFormDataContent(boundary);

            // 添加问题字段
            content.Add(new StringContent(question), "question");

            // 添加图片文件
            var imageContent = new ByteArrayContent(_lastCapturedImage);
            imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            content.Add(imageContent, "file", "camera.jpg");

            // 设置请求头 - 对应ESP32的设备ID和客户端ID
            var request = new HttpRequestMessage(HttpMethod.Post, _explainUrl)
            {
                Content = content
            };

            // 添加认证头
            if (!string.IsNullOrEmpty(_explainToken))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _explainToken);
            }

            // 发送请求
            var response = await _httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadAsStringAsync();
                _deviceLogger?.LogInformation("照片AI分析成功，图片大小: {Size} bytes", _lastCapturedImage.Length);
                return result;
            }
            else
            {
                var errorMsg = $"AI分析请求失败，状态码: {response.StatusCode}";
                _deviceLogger?.LogWarning(errorMsg);
                return errorMsg;
            }
        }
        catch (Exception ex)
        {
            var errorMsg = $"AI分析过程中发生异常: {ex.Message}";
            _deviceLogger?.LogError(ex, "AI分析异常");
            return errorMsg;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _httpClient?.Dispose();
        }
        base.Dispose(disposing);
    }
}
