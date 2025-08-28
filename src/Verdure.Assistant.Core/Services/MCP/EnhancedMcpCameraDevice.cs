using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Verdure.Assistant.Core.Services.MCP
{
    /// <summary>
    /// 增强的相机MCP设备 - 支持真实拍照和AI图像解释功能
    /// </summary>
    public class EnhancedMcpCameraDevice : McpIoTDevice
    {
        private readonly ILogger<EnhancedMcpCameraDevice>? _deviceLogger;
        private readonly HttpClient _httpClient;
        private readonly ICameraService _cameraService;
        private string _explainUrl = "http://api.xiaozhi.me/mcp/vision/explain";
        private string _explainToken = "test-token";
        private byte[]? _lastCapturedImage;
        private CameraSettings _cameraSettings;

        public EnhancedMcpCameraDevice(McpServer mcpServer, ICameraService cameraService, ILogger<EnhancedMcpCameraDevice>? logger = null)
            : base(mcpServer, logger)
        {
            _deviceLogger = logger;
            _httpClient = new HttpClient();
            _cameraService = cameraService ?? throw new ArgumentNullException(nameof(cameraService));
            DeviceId = "enhanced_camera";
            Name = "Enhanced Camera";
            Description = "增强相机设备 - 支持真实拍照和AI图像分析";
            Type = "camera";

            // 初始化相机设置
            _cameraSettings = new CameraSettings
            {
                Width = 1280,
                Height = 720,
                SkipFrames = 20,
                JpegQuality = 85
            };

            // 初始化设备状态
            InitializeDeviceState();
        }

        private async void InitializeDeviceState()
        {
            try
            {
                var isAvailable = await _cameraService.IsAvailableAsync();
                var cameraInfo = await _cameraService.GetCameraInfoAsync();
                
                SetState("available", isAvailable);
                SetState("last_capture_time", "");
                SetState("image_count", 0);
                SetState("resolution", $"{_cameraSettings.Width}x{_cameraSettings.Height}");
                SetState("format", "JPEG");
                SetState("platform", cameraInfo.Platform);
                SetState("camera_name", cameraInfo.Name);
                SetState("device", cameraInfo.Device);
            }
            catch (Exception ex)
            {
                _deviceLogger?.LogError(ex, "Failed to initialize camera device state");
                SetState("available", false);
                SetState("error", ex.Message);
            }
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
                "enhanced.camera.capture",
                "使用真实相机拍摄一张照片",
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

            // 拍照并解释工具
            _mcpServer.AddTool(
                "enhanced.camera.take_photo",
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
                "enhanced.camera.set_resolution",
                "设置相机分辨率",
                new McpPropertyList
                {
                    new("resolution", McpPropertyType.String, "分辨率格式如 1280x720")
                },
                async (properties) =>
                {
                    var resolution = properties["resolution"].GetValue<string>() ?? "1280x720";
                    
                    if (ParseResolution(resolution, out int width, out int height))
                    {
                        _cameraSettings.Width = width;
                        _cameraSettings.Height = height;
                        SetState("resolution", resolution);
                        _deviceLogger?.LogInformation("相机分辨率已设置为: {Resolution}", resolution);
                        return await Task.FromResult<McpReturnValue>($"相机分辨率已设置为: {resolution}");
                    }
                    else
                    {
                        return await Task.FromResult<McpReturnValue>($"无效的分辨率格式: {resolution}");
                    }
                });

            // 设置相机参数工具
            _mcpServer.AddTool(
                "enhanced.camera.configure",
                "配置相机参数",
                new McpPropertyList
                {
                    new("skip_frames", McpPropertyType.Integer, "跳过帧数，用于稳定图像"),
                    new("jpeg_quality", McpPropertyType.Integer, "JPEG质量 (1-100)"),
                    new("add_timestamp", McpPropertyType.Boolean, "是否添加时间戳")
                },
                async (properties) =>
                {
                    var config = new List<string>();

                    if (properties.TryGetProperty("skip_frames", out var skipFramesProp) && skipFramesProp != null)
                    {
                        var skipFrames = skipFramesProp.GetValue<int>();
                        _cameraSettings.SkipFrames = skipFrames;
                        config.Add($"跳过帧数: {skipFrames}");
                    }

                    if (properties.TryGetProperty("jpeg_quality", out var qualityProp) && qualityProp != null)
                    {
                        var quality = qualityProp.GetValue<int>();
                        if (quality >= 1 && quality <= 100)
                        {
                            _cameraSettings.JpegQuality = quality;
                            config.Add($"JPEG质量: {quality}");
                        }
                    }

                    if (properties.TryGetProperty("add_timestamp", out var timestampProp) && timestampProp != null)
                    {
                        var addTimestamp = timestampProp.GetValue<bool>();
                        _cameraSettings.AddTimestamp = addTimestamp;
                        config.Add($"时间戳: {(addTimestamp ? "开启" : "关闭")}");
                    }

                    var result = config.Count > 0 ? string.Join(", ", config) : "无配置更改";
                    _deviceLogger?.LogInformation("相机配置已更新: {Config}", result);
                    return await Task.FromResult<McpReturnValue>($"相机配置已更新: {result}");
                });

            // 获取相机信息工具
            _mcpServer.AddTool(
                "enhanced.camera.info",
                "获取相机详细信息",
                new McpPropertyList(),
                async (properties) =>
                {
                    try
                    {
                        var cameraInfo = await _cameraService.GetCameraInfoAsync();
                        var supportedResolutions = await _cameraService.GetSupportedResolutionsAsync();
                        
                        var info = $"""
                        相机信息:
                        - 名称: {cameraInfo.Name}
                        - 设备: {cameraInfo.Device}
                        - 平台: {cameraInfo.Platform}
                        - 状态: {(cameraInfo.IsAvailable ? "可用" : "不可用")}
                        - 支持格式: {string.Join(", ", cameraInfo.SupportedFormats)}
                        - 当前分辨率: {_cameraSettings.Width}x{_cameraSettings.Height}
                        - 支持分辨率: {string.Join(", ", supportedResolutions)}
                        - JPEG质量: {_cameraSettings.JpegQuality}
                        - 跳过帧数: {_cameraSettings.SkipFrames}
                        """;

                        return await Task.FromResult<McpReturnValue>(info);
                    }
                    catch (Exception ex)
                    {
                        _deviceLogger?.LogError(ex, "获取相机信息失败");
                        return await Task.FromResult<McpReturnValue>($"获取相机信息失败: {ex.Message}");
                    }
                });
        }

        /// <summary>
        /// 拍摄照片 - 使用实际的相机服务
        /// </summary>
        private async Task<bool> CapturePhotoAsync()
        {
            try
            {
                _deviceLogger?.LogInformation("Starting photo capture with settings: {Resolution}", 
                    $"{_cameraSettings.Width}x{_cameraSettings.Height}");

                // 检查相机是否可用
                var isAvailable = await _cameraService.IsAvailableAsync();
                if (!isAvailable)
                {
                    _deviceLogger?.LogWarning("Camera is not available");
                    SetState("available", false);
                    return false;
                }

                // 使用相机服务拍照
                _lastCapturedImage = await _cameraService.CapturePhotoAsync(_cameraSettings);

                if (_lastCapturedImage != null && _lastCapturedImage.Length > 0)
                {
                    _deviceLogger?.LogInformation("Photo captured successfully. Size: {Size} bytes", _lastCapturedImage.Length);
                    SetState("available", true);
                    return true;
                }
                else
                {
                    _deviceLogger?.LogWarning("Photo capture returned empty data");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _deviceLogger?.LogError(ex, "Failed to capture photo");
                SetState("error", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// 使用AI解释照片
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
                // 构建multipart/form-data请求
                var boundary = "----CAMERA_BOUNDARY_" + Guid.NewGuid().ToString("N");
                var content = new MultipartFormDataContent(boundary);

                // 添加问题字段
                content.Add(new StringContent(question), "question");

                // 添加图片文件
                var imageContent = new ByteArrayContent(_lastCapturedImage);
                imageContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
                content.Add(imageContent, "file", "camera.jpg");

                // 添加认证头
                if (!string.IsNullOrEmpty(_explainToken))
                {
                    _httpClient.DefaultRequestHeaders.Authorization = 
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _explainToken);
                }

                var response = await _httpClient.PostAsync(_explainUrl, content);

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
                var errorMsg = $"AI分析异常: {ex.Message}";
                _deviceLogger?.LogError(ex, "照片AI分析异常");
                return errorMsg;
            }
        }

        private bool ParseResolution(string resolution, out int width, out int height)
        {
            width = 0;
            height = 0;

            if (string.IsNullOrEmpty(resolution))
                return false;

            var parts = resolution.Split('x', 'X', '*');
            if (parts.Length != 2)
                return false;

            return int.TryParse(parts[0], out width) && int.TryParse(parts[1], out height);
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
}
