# 部署指南

Verdure Assistant 项目的生产环境部署指南。

## 部署方式

### 单文件发布

```bash
# Windows 发布
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true

# Linux 发布
dotnet publish -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true

# macOS 发布
dotnet publish -c Release -r osx-x64 --self-contained true -p:PublishSingleFile=true
```

### Docker 部署

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY publish/ .
ENTRYPOINT ["dotnet", "Verdure.Assistant.Api.dll"]
```

### 服务部署

#### Windows 服务

```bash
# 创建 Windows 服务
sc create VerdureAssistant binPath="C:\App\Verdure.Assistant.Console.exe"
```

#### Linux 服务 (systemd)

创建 `/etc/systemd/system/verdure-assistant.service`：

```ini
[Unit]
Description=Verdure Assistant
After=network.target

[Service]
Type=notify
ExecStart=/opt/verdure-assistant/Verdure.Assistant.Console
Restart=always
User=verdure

[Install]
WantedBy=multi-user.target
```

## 配置管理

### 环境变量

```bash
export VoiceService__ServerUrl="wss://api.tenclass.net/xiaozhi/v1/"
export VoiceService__ApiKey="your-api-key"
```

### 配置文件

生产环境配置文件 `appsettings.Production.json`：

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  "VoiceService": {
    "ServerUrl": "wss://api.tenclass.net/xiaozhi/v1/",
    "EnableVoice": true
  }
}
```

## 监控和维护

### 健康检查

配置应用健康检查端点：

```csharp
builder.Services.AddHealthChecks()
    .AddCheck<VoiceServiceHealthCheck>("voice-service");
```

### 日志管理

使用结构化日志和日志聚合工具：

```csharp
builder.Services.AddSerilog((services, lc) => lc
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/app-.txt", rollingInterval: RollingInterval.Day));
```

## 故障排除

### 常见部署问题

1. **端口占用** - 检查端口是否被其他服务占用
2. **权限问题** - 确保应用有必要的系统权限
3. **依赖缺失** - 检查运行时依赖是否安装

### 性能优化

- 启用 ReadyToRun 编译
- 配置垃圾回收策略
- 优化连接池设置