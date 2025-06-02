# 示例代码

本目录包含绿荫助手（Verdure Assistant）项目的示例代码和参考实现。

## 📁 目录结构

```
samples/
├── py-xiaozhi/                # Python参考实现
├── BasicUsage/                # 基础使用示例
├── AdvancedFeatures/          # 高级功能示例
├── CustomAudioCodec/          # 自定义音频编解码器
├── CustomProtocol/            # 自定义通信协议
└── IntegrationExamples/       # 集成示例
```

## 🚀 快速开始示例

### 基础语音聊天
```csharp
// 基础使用示例
var config = new VerdureConfig
{
    ServerUrl = "wss://your-server.com/ws",
    EnableVoice = true
};

var voiceChat = new VoiceChatService(configService, logger);
await voiceChat.InitializeAsync(config);

// 开始语音对话
await voiceChat.StartVoiceChatAsync();
```

### 自定义音频处理
```csharp
// 自定义音频编解码器
public class CustomAudioCodec : IAudioCodec
{
    public byte[] Encode(byte[] pcmData, int sampleRate, int channels)
    {
        // 自定义编码实现
        return encodedData;
    }

    public byte[] Decode(byte[] encodedData, int sampleRate, int channels)
    {
        // 自定义解码实现
        return pcmData;
    }
}
```

## 📚 示例类别

### 基础示例 (BasicUsage/)
- **HelloWorld** - 最简单的语音聊天示例
- **ConsoleChat** - 控制台语音聊天应用
- **SimpleGUI** - 简单的图形界面示例

### 高级功能 (AdvancedFeatures/)
- **AutoDialogue** - 自动对话模式示例
- **StateManagement** - 设备状态管理
- **ErrorHandling** - 错误处理和恢复
- **ConfigurationManagement** - 动态配置管理

### 自定义实现 (CustomAudioCodec/, CustomProtocol/)
- **CustomCodec** - 自定义音频编解码器
- **CustomClient** - 自定义通信客户端
- **PluginSystem** - 插件系统示例

### 集成示例 (IntegrationExamples/)
- **AspNetCore** - ASP.NET Core集成
- **Blazor** - Blazor WebAssembly集成
- **WPF** - WPF应用集成
- **MAUI** - .NET MAUI集成

## 🐍 Python参考实现 (py-xiaozhi/)

这是原始的Python实现，作为C#版本的参考：

### 主要特性
- 完整的语音交互功能
- 图形用户界面
- 音乐播放功能
- 智能家居控制
- 多模态交互

### 运行Python版本
```bash
cd samples/py-xiaozhi
pip install -r requirements.txt
python main.py
```

### 架构对照

| Python组件 | C#对应组件 | 说明 |
|-----------|-----------|------|
| `application.py` | `VoiceChatService` | 主要应用逻辑 |
| `audio_processing/` | `IAudioRecorder`, `IAudioPlayer` | 音频处理 |
| `network/websocket_client.py` | `WebSocketClient` | WebSocket通信 |
| `protocols/` | `ProtocolMessage` | 通信协议 |
| `constants/` | `Constants/` | 常量定义 |

## 🔧 开发指南

### 创建新示例

1. **创建项目目录**
   ```bash
   mkdir samples/MyExample
   cd samples/MyExample
   ```

2. **创建项目文件**
   ```bash
   dotnet new console
   dotnet add reference ../../src/Verdure.Assistant.Core
   ```

3. **编写示例代码**
   ```csharp   using Verdure.Assistant.Core.Services;
   using Verdure.Assistant.Core.Models;
   
   // 你的示例代码
   ```

4. **添加README说明**
   ```markdown
   # My Example
   
   此示例演示了...
   
   ## 运行方式
   dotnet run
   ```

### 示例代码规范

- **简洁明了** - 代码应该易于理解
- **完整可运行** - 确保示例能够独立运行
- **注释充分** - 关键代码应有详细注释
- **错误处理** - 包含基本的错误处理
- **文档完整** - 每个示例都应有README

## 📖 学习路径

### 初学者
1. 从 `BasicUsage/HelloWorld` 开始
2. 学习 `BasicUsage/ConsoleChat`
3. 尝试 `BasicUsage/SimpleGUI`

### 进阶开发者
1. 研究 `AdvancedFeatures/AutoDialogue`
2. 学习 `AdvancedFeatures/StateManagement`
3. 实践 `CustomAudioCodec/` 示例

### 系统集成
1. 查看 `IntegrationExamples/AspNetCore`
2. 学习 `IntegrationExamples/Blazor`
3. 尝试其他平台集成

## 🤝 贡献示例

我们欢迎社区贡献新的示例代码：

1. **Fork 项目**
2. **创建示例** - 在 `samples/` 下创建新目录
3. **编写代码** - 遵循代码规范
4. **添加文档** - 包含README和注释
5. **提交PR** - 描述示例的用途和特点

### 示例提交清单

- [ ] 代码可以正常运行
- [ ] 包含详细的README
- [ ] 代码有适当的注释
- [ ] 遵循项目代码规范
- [ ] 包含错误处理
- [ ] 测试在多个环境下运行

## 📞 获取帮助

如果您在运行示例时遇到问题：

1. 检查示例的README文件
2. 查看项目主文档
3. 在GitHub上提交Issue
4. 参与Discussions讨论

我们很乐意为您提供帮助！
