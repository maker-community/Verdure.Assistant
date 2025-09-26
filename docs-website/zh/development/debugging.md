# 调试技巧

Verdure Assistant 项目的调试和故障排除指南。

## 常见问题

### 连接问题

**问题**：无法连接到服务器

**解决方案**：
1. 检查网络连接
2. 验证服务器地址配置
3. 确认防火墙设置

### 音频问题

**问题**：语音功能无法工作

**解决方案**：
1. 检查麦克风权限
2. 验证音频设备配置
3. 测试音频驱动程序

## 日志分析

### 启用详细日志

在 `appsettings.json` 中配置：

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft": "Warning"
    }
  }
}
```

### 日志文件位置

- **Windows**: `%LOCALAPPDATA%/Verdure.Assistant/logs/`
- **Linux**: `~/.local/share/Verdure.Assistant/logs/`
- **macOS**: `~/Library/Application Support/Verdure.Assistant/logs/`

## 性能分析

### 内存使用监控

使用 .NET 诊断工具监控内存使用情况：

```bash
dotnet-trace collect --providers Microsoft-Extensions-Logging --process-id <PID>
```

### 性能计数器

监控关键性能指标：
- CPU 使用率
- 内存消耗
- 网络延迟
- 音频处理时间

## 调试工具

### Visual Studio 调试器

- 断点调试
- 监视窗口
- 调用堆栈分析

### 命令行工具

- `dotnet-dump` - 内存转储分析
- `dotnet-trace` - 性能跟踪
- `dotnet-counters` - 实时性能计数器