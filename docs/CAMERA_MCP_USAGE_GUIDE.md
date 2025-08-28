# 相机 MCP 服务使用指南

## 概述

本项目现在支持真实的相机拍照功能，可以在不同平台上使用相应的相机服务：

- **Linux/树莓派**: 使用 `fswebcam` 命令行工具
- **Windows**: 使用 PowerShell 和 Windows Camera API
- **其他平台/测试**: 使用模拟服务

## 平台配置

### Linux/树莓派配置

1. **安装 fswebcam**:
```bash
sudo apt-get update
sudo apt-get install fswebcam
```

2. **检查摄像头设备**:
```bash
ls /dev/video*
# 通常是 /dev/video0
```

3. **测试拍照**:
```bash
fswebcam -r 1280x720 --no-banner test.jpg
```

### Windows 配置

1. **确保相机驱动正常**
2. **检查相机设备**:
```powershell
Get-PnpDevice -Class Camera | Where-Object {$_.Status -eq 'OK'}
```

## 代码集成示例

### 1. 服务注册 (Program.cs 或 Startup.cs)

```csharp
using Verdure.Assistant.Core.Services;

// 在服务配置中添加
services.AddCameraService(); // 自动检测平台
// 或者强制使用特定服务
// services.AddCameraService(CameraServiceType.Linux);

services.AddEnhancedMcpCameraDevice();
```

### 2. 在 MCP 服务器中使用

```csharp
public class McpServerService
{
    private readonly McpServer _mcpServer;
    private readonly ICameraService _cameraService;
    private readonly ILogger<McpServerService> _logger;

    public McpServerService(McpServer mcpServer, ICameraService cameraService, ILogger<McpServerService> logger)
    {
        _mcpServer = mcpServer;
        _cameraService = cameraService;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        // 创建增强相机设备
        var cameraDevice = new EnhancedMcpCameraDevice(_mcpServer, _cameraService, _logger);
        
        // 配置 AI 解释服务（可选）
        cameraDevice.SetExplainUrl("http://your-ai-service.com/vision/explain", "your-token");
        
        // 启动设备
        await cameraDevice.StartAsync();
        
        _logger.LogInformation("Enhanced camera device initialized");
    }
}
```

### 3. 直接使用相机服务

```csharp
public class PhotoService
{
    private readonly ICameraService _cameraService;
    private readonly ILogger<PhotoService> _logger;

    public PhotoService(ICameraService cameraService, ILogger<PhotoService> logger)
    {
        _cameraService = cameraService;
        _logger = logger;
    }

    public async Task<byte[]> TakePhotoAsync()
    {
        // 检查相机可用性
        if (!await _cameraService.IsAvailableAsync())
        {
            throw new InvalidOperationException("Camera is not available");
        }

        // 配置拍照参数
        var settings = new CameraSettings
        {
            Width = 1920,
            Height = 1080,
            JpegQuality = 90,
            AddTimestamp = true
        };

        // 拍照
        var imageBytes = await _cameraService.CapturePhotoAsync(settings);
        
        _logger.LogInformation("Photo captured: {Size} bytes", imageBytes.Length);
        return imageBytes;
    }

    public async Task<string> GetCameraInfoAsync()
    {
        var info = await _cameraService.GetCameraInfoAsync();
        var resolutions = await _cameraService.GetSupportedResolutionsAsync();
        
        return $"""
        Camera: {info.Name}
        Platform: {info.Platform}
        Device: {info.Device}
        Available: {info.IsAvailable}
        Supported Resolutions: {string.Join(", ", resolutions)}
        """;
    }
}
```

## MCP 工具使用

### 可用的 MCP 工具

1. **enhanced.camera.capture** - 拍摄照片
2. **enhanced.camera.take_photo** - 拍照并AI分析
3. **enhanced.camera.set_resolution** - 设置分辨率
4. **enhanced.camera.configure** - 配置相机参数
5. **enhanced.camera.info** - 获取相机信息

### 使用示例

```javascript
// 简单拍照
await mcpClient.callTool('enhanced.camera.capture', {});

// 拍照并分析
await mcpClient.callTool('enhanced.camera.take_photo', {
    question: '这张照片里有什么？'
});

// 设置高分辨率
await mcpClient.callTool('enhanced.camera.set_resolution', {
    resolution: '1920x1080'
});

// 配置相机参数
await mcpClient.callTool('enhanced.camera.configure', {
    skip_frames: 30,
    jpeg_quality: 95,
    add_timestamp: true
});

// 获取相机信息
await mcpClient.callTool('enhanced.camera.info', {});
```

## 故障排除

### Linux/树莓派

1. **权限问题**:
```bash
# 添加用户到 video 组
sudo usermod -a -G video $USER
# 重新登录后生效
```

2. **设备不存在**:
```bash
# 检查 USB 摄像头连接
lsusb | grep -i camera
# 加载摄像头模块
sudo modprobe uvcvideo
```

3. **fswebcam 未找到**:
```bash
which fswebcam
# 如果没有，重新安装
sudo apt-get install --reinstall fswebcam
```

### Windows

1. **相机被占用**:
```powershell
# 检查占用相机的进程
Get-Process | Where-Object {$_.ProcessName -like "*camera*"}
```

2. **权限问题**:
   - 检查应用是否有相机访问权限
   - Windows 设置 > 隐私 > 相机

### 通用问题

1. **服务未注册**:
```csharp
// 确保在 Program.cs 中注册了服务
services.AddCameraService();
```

2. **依赖注入问题**:
```csharp
// 确保正确注入 ICameraService
public YourService(ICameraService cameraService)
{
    _cameraService = cameraService;
}
```

## 扩展开发

### 添加新平台支持

1. 实现 `ICameraService` 接口
2. 在 `CameraServiceFactory` 中添加检测逻辑
3. 注册新的服务类型

### 自定义相机配置

```csharp
public class CustomCameraSettings : CameraSettings
{
    public string OutputFormat { get; set; } = "PNG";
    public bool EnableFlash { get; set; } = false;
    public int Brightness { get; set; } = 50;
}
```

### AI 分析服务集成

相机设备支持通过 HTTP 调用外部 AI 视觉分析服务。配置格式：

```csharp
cameraDevice.SetExplainUrl("https://api.openai.com/v1/vision", "your-api-key");
```

期望的 API 响应格式：
```json
{
    "analysis": "这张照片显示了一个现代化的办公环境..."
}
```
