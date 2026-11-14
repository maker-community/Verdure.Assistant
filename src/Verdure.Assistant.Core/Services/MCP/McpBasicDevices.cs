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
    private readonly IImageCaptureService? _capture;
    private readonly IVisionService? _vision;
    private string _explainUrl = "http://api.xiaozhi.me/mcp/vision/explain";
    private string _explainToken = "test-token";
    private byte[]? _lastCapturedImage;
    private string? _lastCapturedMime;

    public McpCameraDevice(McpServer mcpServer, ILogger<McpCameraDevice>? logger = null,
        IImageCaptureService? capture = null, IVisionService? vision = null)
        : base(mcpServer, logger)
    {
        _deviceLogger = logger;
        _capture = capture;
        _vision = vision;
        _httpClient = new HttpClient();
        DeviceId = "main_camera";
        Name = "Camera";
        Description = "摄像头与屏幕 - 支持拍照、截屏和AI图像理解";
        Type = "camera";

        // 初始化设备状态
        SetState("available", _capture?.IsSupported(ImageCaptureSource.Camera) == true);
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
            "用摄像头拍摄一张照片（只拍照，不分析；要看照片内容请用 self.camera.take_photo）",
            new McpPropertyList(),
            async (properties) =>
            {
                var (image, error) = await TryCaptureAsync(ImageCaptureSource.Camera);
                if (image == null)
                {
                    return error ?? "拍照失败";
                }

                var timestamp = RecordCapture();
                return $"照片拍摄成功，时间: {timestamp}，尺寸 {image.Width}x{image.Height}";
            });

        // 拍照并识别 - 对应ESP32的take_photo工具
        _mcpServer.AddTool(
            "self.camera.take_photo",
            "用摄像头拍一张照片并用AI识别回答问题。当用户想让你\"看看\"眼前、面前、摄像头里的东西（例如：这是什么、桌上有什么、我穿了什么）时调用。",
            new McpPropertyList
            {
                new("question", McpPropertyType.String, "关于照片的问题或分析要求，例如\"这是什么东西\"")
            },
            async (properties) =>
            {
                var question = properties["question"].GetValue<string>() ?? "请描述这张照片的内容";
                return await CaptureAndExplainAsync(ImageCaptureSource.Camera, question, "照片");
            });

        // 截屏并识别
        _mcpServer.AddTool(
            "self.screen.take_screenshot",
            "截取电脑主屏幕并用AI识别回答问题。当用户想让你\"看看屏幕\"、读屏幕上的文字、解释当前界面或图表时调用。",
            new McpPropertyList
            {
                new("question", McpPropertyType.String, "关于屏幕内容的问题，例如\"屏幕上写了什么\"")
            },
            async (properties) =>
            {
                var question = properties["question"].GetValue<string>() ?? "请描述屏幕上的内容";
                return await CaptureAndExplainAsync(ImageCaptureSource.Screen, question, "屏幕截图");
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
    }

    /// <summary>
    /// 取一张图（摄像头或屏幕），保存到 <see cref="_lastCapturedImage"/>。失败时返回面向用户的错误文字。
    /// </summary>
    private async Task<(CapturedImage? Image, string? Error)> TryCaptureAsync(ImageCaptureSource source)
    {
        if (_capture == null || !_capture.IsSupported(source))
        {
            return (null, source == ImageCaptureSource.Camera
                ? "当前平台没有可用的摄像头，无法拍照"
                : "当前平台不支持截取屏幕");
        }

        try
        {
            var image = await _capture.CaptureAsync(source).ConfigureAwait(false);
            _lastCapturedImage = image.Data;
            _lastCapturedMime = image.MimeType;
            _deviceLogger?.LogInformation("{Source} 取图成功：{Width}x{Height}，{Kb}KB", source, image.Width, image.Height, image.Data.Length / 1024);
            return (image, null);
        }
        catch (ImageCaptureException ex)
        {
            _deviceLogger?.LogWarning(ex, "{Source} 取图失败", source);
            return (null, ex.Message);
        }
        catch (Exception ex)
        {
            _deviceLogger?.LogError(ex, "{Source} 取图异常", source);
            return (null, $"取图失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 使用AI解释照片 - 对应ESP32的Explain方法
    /// </summary>
    private string RecordCapture()
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        SetState("last_capture_time", timestamp);
        SetState("image_count", GetState<int>("image_count") + 1);
        return timestamp;
    }

    private async Task<string> CaptureAndExplainAsync(ImageCaptureSource source, string question, string label)
    {
        try
        {
            var (image, error) = await TryCaptureAsync(source);
            if (image == null)
            {
                return error ?? $"{label}失败";
            }

            RecordCapture();
            var explanation = await ExplainPhotoAsync(question);
            _deviceLogger?.LogInformation("{Label}分析完成，问题: {Question}", label, question);
            return $"{label}分析结果：\n{explanation}";
        }
        catch (Exception ex)
        {
            _deviceLogger?.LogError(ex, "{Label}并分析时异常", label);
            return $"操作异常: {ex.Message}";
        }
    }

    private async Task<string> ExplainPhotoAsync(string question)
    {
        if (_lastCapturedImage == null || _lastCapturedImage.Length == 0)
        {
            return "没有可分析的照片，请先拍照";
        }

        // 优先使用 DeepSeek 图像理解；没有配置 Key 时回退到小智服务器的识图接口
        if (_vision?.IsAvailable == true)
        {
            try
            {
                return await _vision.ExplainAsync(_lastCapturedImage, _lastCapturedMime, question).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _deviceLogger?.LogWarning(ex, "DeepSeek 图像理解失败");
                return $"图像识别失败：{DescribeVisionError(ex)}";
            }
        }

        return await ExplainWithXiaoZhiAsync(question).ConfigureAwait(false);
    }

    private static string DescribeVisionError(Exception ex) => ex switch
    {
        Verdure.Assistant.Core.Services.DeepSeek.DeepSeekApiException { IsAuthenticationError: true } => "API Key 无效",
        Verdure.Assistant.Core.Services.DeepSeek.DeepSeekApiException { IsInsufficientBalance: true } => "账户余额不足",
        Verdure.Assistant.Core.Services.DeepSeek.DeepSeekApiException { IsRateLimited: true } => "请求太频繁，请稍后再试",
        TimeoutException => "请求超时",
        HttpRequestException => "网络连接失败",
        _ => ex.Message
    };

    private async Task<string> ExplainWithXiaoZhiAsync(string question)
    {
        if (string.IsNullOrEmpty(_explainUrl))
        {
            return "图像识别未配置：请在 DeepSeek 页面设置 API Key，或配置小智识图服务";
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
            imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(_lastCapturedMime ?? "image/jpeg");
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
