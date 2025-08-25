# Verdure.Assistant.Api - 音乐播放API服务

## 概述

本项目将 Verdure.Assistant.Console 的音乐播放功能移植到了 Verdure.Assistant.Api 项目中，使用 **mpg123** 作为音频播放后端，提供了RESTful API接口来控制音乐播放。

## 主要特性

- ✅ **mpg123音频播放**: 使用mpg123命令行工具进行音频播放，提供更好的跨平台兼容性
- ✅ **音乐搜索和播放**: 集成酷狗音乐API，支持搜索和播放音乐
- ✅ **音乐缓存管理**: 自动缓存下载的音乐文件，提高播放速度
- ✅ **语音聊天功能**: 移植了完整的语音聊天服务
- ✅ **RESTful API**: 提供完整的HTTP API接口
- ✅ **详细日志**: 包含音乐缓存路径的详细日志输出

## 系统要求

### mpg123安装

**Windows:**
```bash
# 使用 Chocolatey
choco install mpg123

# 或者手动下载
# 1. 从 https://www.mpg123.de/download.shtml 下载
# 2. 解压到系统PATH目录
# 3. 确保命令行可以运行 mpg123 --version
```

**Linux:**
```bash
# Ubuntu/Debian
sudo apt-get install mpg123

# CentOS/RHEL
sudo yum install mpg123

# Fedora
sudo dnf install mpg123
```

**macOS:**
```bash
# 使用 Homebrew
brew install mpg123

# 使用 MacPorts
sudo port install mpg123
```

## 快速开始

### 1. 启动服务

```bash
cd src/Verdure.Assistant.Api
dotnet run
```

服务将在 `http://localhost:5000` (HTTP) 和 `https://localhost:5001` (HTTPS) 启动。

### 2. 查看API文档

访问 `https://localhost:5001/swagger` 查看自动生成的API文档。

### 3. 测试mpg123可用性

```bash
curl -X GET "https://localhost:5001/api/test/check-mpg123"
```

## API接口

### 音乐控制API

#### 搜索歌曲
```http
GET /api/music/search?songName=周杰伦
```

#### 搜索并播放
```http
POST /api/music/search-and-play
Content-Type: application/json

{
  "songName": "青花瓷"
}
```

#### 播放控制
```http
POST /api/music/toggle    # 切换播放/暂停
POST /api/music/pause     # 暂停
POST /api/music/resume    # 恢复
POST /api/music/stop      # 停止
```

#### 跳转和音量
```http
POST /api/music/seek
Content-Type: application/json

{
  "position": 60.0  # 跳转到60秒
}
```

```http
POST /api/music/volume
Content-Type: application/json

{
  "volume": 75.0  # 设置音量为75%
}
```

#### 获取状态
```http
GET /api/music/status     # 获取播放状态
GET /api/music/lyrics     # 获取当前歌词
```

#### 缓存管理
```http
POST /api/music/clear-cache  # 清理音乐缓存
```

### 语音聊天API

#### 服务控制
```http
POST /api/voicechat/initialize  # 初始化服务
POST /api/voicechat/start       # 开始语音对话
POST /api/voicechat/stop        # 停止语音对话
GET /api/voicechat/status       # 获取状态
```

#### 文本消息
```http
POST /api/voicechat/send-text
Content-Type: application/json

{
  "message": "你好"
}
```

### 测试API

#### 系统信息
```http
GET /api/test/system-info     # 获取系统信息和缓存状态
```

#### mpg123测试
```http
POST /api/test/mpg123         # 测试mpg123播放器
POST /api/test/music-search?songName=周杰伦  # 测试音乐搜索播放
```

## 音乐缓存

### 缓存路径

音乐文件将自动缓存到以下目录：

**Windows:** `C:\Users\{用户名}\AppData\Local\Temp\VerdureMusicCache\`

**Linux/macOS:** `/tmp/VerdureMusicCache/`

### 缓存文件命名

缓存文件按照以下格式命名：
- 格式: `{歌曲ID}.mp3`
- 示例: `1245657.mp3`

### 日志输出

系统会在控制台和日志中输出详细的缓存信息：

```
[音乐缓存] API音乐服务缓存目录: C:\Users\gil\AppData\Local\Temp\VerdureMusicCache
[音乐缓存] 搜索并播放歌曲: 青花瓷
[音乐缓存] 加载音频文件路径: C:\Users\gil\AppData\Local\Temp\VerdureMusicCache\1245657.mp3
[音乐缓存] 音频文件时长: 00:04:20
[音乐缓存] 使用mpg123播放: C:\Users\gil\AppData\Local\Temp\VerdureMusicCache\1245657.mp3
[音乐缓存] mpg123进程已启动，参数: --volume 16384 "C:\Users\gil\AppData\Local\Temp\VerdureMusicCache\1245657.mp3"
```

## 配置文件

### appsettings.json

```json
{
  "ServerUrl": "wss://api.tenclass.net/xiaozhi/v1/",
  "UseWebSocket": true,
  "EnableVoice": true,
  "MusicPlayer": {
    "Backend": "mpg123",
    "CacheDirectory": "",
    "Volume": 50
  }
}
```

### appsettings.Development.json

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Verdure.Assistant": "Debug"
    }
  },
  "MusicPlayer": {
    "Debug": true
  }
}
```

## 架构说明

### 核心组件

1. **Mpg123AudioPlayer**: 基于mpg123的音频播放器实现
2. **ApiMusicService**: API音乐服务，封装了KugouMusicService
3. **MusicController**: 音乐控制REST API
4. **VoiceChatController**: 语音聊天REST API
5. **TestController**: 测试和调试API

### 与Console项目的区别

| 功能 | Console项目 | Api项目 |
|------|------------|---------|
| 音频播放 | NLayer + PortAudioSharp2 | mpg123 |
| 用户界面 | 命令行交互 | RESTful API |
| 部署方式 | 桌面应用 | Web服务 |
| 跨平台性 | 需要PortAudio依赖 | 仅需要mpg123 |

### mpg123优势

1. **跨平台**: 在Windows、Linux、macOS上都有良好支持
2. **轻量级**: 无需复杂的音频库依赖
3. **稳定性**: 成熟的命令行工具，稳定可靠
4. **易部署**: 大多数系统都可以简单安装

## 故障排除

### mpg123不可用

如果遇到 "mpg123 不可用" 错误：

1. 确认mpg123已正确安装
2. 检查mpg123是否在系统PATH中
3. 测试命令: `mpg123 --version`
4. 查看详细错误信息: `GET /api/test/check-mpg123`

### 音乐播放失败

1. 检查网络连接
2. 查看音乐缓存目录是否可写
3. 检查mpg123进程是否正常启动
4. 查看详细日志输出

### 缓存问题

1. 查看缓存目录: `GET /api/test/system-info`
2. 清理缓存: `POST /api/music/clear-cache`
3. 检查磁盘空间

## 开发说明

### 添加新功能

1. 在相应的Controller中添加新的API端点
2. 在Service层实现业务逻辑
3. 更新API文档

### 调试技巧

1. 使用`/api/test`端点进行功能测试
2. 查看控制台输出的详细缓存日志
3. 使用`/api/test/system-info`查看系统状态

## 许可证

本项目遵循与Verdure.Assistant主项目相同的许可证。
