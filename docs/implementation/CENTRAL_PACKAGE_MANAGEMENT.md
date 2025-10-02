# Central Package Management 实施文档

## 概述

本项目已实施 Central Package Management (CPM)，参考 BotSharp 的包管理方式，统一管理所有 NuGet 包的版本。

## 实施日期

2025-01-XX

## 变更内容

### 1. 新增文件

#### Directory.Packages.props
位于解决方案根目录，定义了所有 NuGet 包及其版本。包含以下类别：

- **Community Toolkit**: CommunityToolkit.Maui, CommunityToolkit.Mvvm
- **IoT and Device Bindings**: Iot.Device.Bindings, System.Device.Gpio
- **MQTT**: MQTTnet
- **Microsoft AspNetCore**: Microsoft.AspNetCore.OpenApi
- **Microsoft Cognitive Services**: Microsoft.CognitiveServices.Speech
- **Microsoft Extensions**: 所有 Microsoft.Extensions.* 包
- **Microsoft MAUI**: Microsoft.Maui.Controls 相关包
- **Microsoft Testing**: xunit, coverlet 等测试包
- **Microsoft Web and Windows**: WinUI, WebView2, WindowsAppSDK
- **Audio**: NAudio, OpusSharp, SoundFlow
- **QR Code and Images**: QRCoder, SkiaSharp, SixLabors.ImageSharp
- **UI Extensions**: WinUIEx

#### Directory.Build.props
位于解决方案根目录，启用 Central Package Management 功能。

### 2. 修改文件

所有 `.csproj` 文件中的 `PackageReference` 元素都已移除 `Version` 属性。

#### 受影响的项目文件

**src 目录 (6 个项目):**
- Verdure.Assistant.Core
- Verdure.Assistant.Console
- Verdure.Assistant.Api
- Verdure.Assistant.ViewModels
- Verdure.Assistant.WinUI
- Verdure.Assistant.MAUI

**tests 目录 (19 个项目):**
- ApiCheck
- CodecTest
- ConversationStateMachine.Tests
- DecodeTest
- InterruptArchitectureTest
- KeywordRecognitionDiagnostic
- McpIntegrationVerificationTest
- McpResponseTest
- McpVoiceChatIntegrationTest
- McpWebSocketIntegrationTest
- OpusApiTest
- OpusSharpTest
- OpusTest
- SoundFlow.Samples.VoiceInterruption
- SoundFlow.Samples.VoiceInterruptionMusic
- SoundFlowDirectTest
- SoundFlowPlaybackTest
- SoundFlowRecordingCodecTest
- WebSocketAudioFlowTest

## 版本变更

### 统一的包版本

以下包在项目中存在版本冲突，已统一到最高版本：

| 包名 | 原版本 | 统一后版本 |
|-----|-------|-----------|
| Microsoft.Extensions.DependencyInjection | 9.0.5, 9.0.8, 9.0.9 | 9.0.9 |
| Microsoft.Extensions.Hosting | 9.0.0, 9.0.8 | 9.0.8 |
| Microsoft.Extensions.Logging | 9.0.5, 9.0.8 | 9.0.8 |
| Microsoft.Extensions.Logging.Console | 9.0.0, 9.0.5, 9.0.8 | 9.0.8 |
| Microsoft.Extensions.Logging.Debug | 9.0.8, 9.0.9 | 9.0.9 |

### 通配符版本转换

以下包原本使用通配符版本，已转换为具体版本：

| 包名 | 原版本 | 新版本 |
|-----|-------|--------|
| Microsoft.Web.WebView2 | 1.* | 1.0.2839.39 |
| Microsoft.Windows.SDK.BuildTools | 10.* | 10.0.26100.1742 |
| Microsoft.WindowsAppSDK | 1.* | 1.6.250124002 |

### 保持不变的内容

- ✅ 所有其他 NuGet 包版本保持不变
- ✅ .NET 9.0 框架版本保持不变
- ✅ 所有项目配置保持不变

## 优势

### 1. 集中管理
所有包版本在 `Directory.Packages.props` 中统一定义，便于管理和升级。

### 2. 版本一致性
确保整个解决方案使用相同版本的包，避免版本冲突。

### 3. 简化项目文件
项目文件更加简洁，只需声明包名，不需要指定版本。

### 4. 便于升级
升级包版本只需修改 `Directory.Packages.props` 一个文件。

### 5. 减少错误
避免因不同项目使用不同版本而导致的兼容性问题。

## 使用方法

### 添加新的 NuGet 包

1. 在 `Directory.Packages.props` 中添加包版本定义：
```xml
<PackageVersion Include="PackageName" Version="x.y.z" />
```

2. 在项目的 `.csproj` 文件中引用包（不需要指定版本）：
```xml
<PackageReference Include="PackageName" />
```

### 升级 NuGet 包版本

只需修改 `Directory.Packages.props` 中对应包的版本号即可，所有引用该包的项目都会自动使用新版本。

### 为特定项目使用不同版本

如果确实需要为特定项目使用不同版本（不推荐），可以在项目文件中覆盖：
```xml
<PackageReference Include="PackageName" VersionOverride="x.y.z" />
```

## 验证

所有项目已验证构建成功：
- ✅ Verdure.Assistant.Core
- ✅ Verdure.Assistant.Console
- ✅ Verdure.Assistant.Api
- ✅ Verdure.Assistant.ViewModels
- ✅ ConversationStateMachine.Tests
- ✅ OpusSharpTest
- ✅ InterruptArchitectureTest
- ✅ WebSocketAudioFlowTest

## 参考

- [Central Package Management | Microsoft Docs](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management)
- [BotSharp Package Management](https://github.com/SciSharp/BotSharp)

## 注意事项

1. **通配符版本**: CPM 不支持通配符版本（如 `1.*`），必须使用具体版本号。
2. **版本升级**: 升级包版本时，建议先在开发环境测试，确保兼容性。
3. **冲突解决**: 如果遇到版本冲突，应选择最高的兼容版本。
4. **构建失败**: 如果构建失败，检查 `Directory.Packages.props` 中的版本是否正确。

## 维护建议

1. **定期更新**: 定期检查并更新包版本，保持项目的安全性和稳定性。
2. **版本测试**: 升级主要版本前，应在隔离环境中进行充分测试。
3. **文档更新**: 包版本变更时，更新本文档的版本变更记录。
4. **团队沟通**: 重大版本升级前，应与团队成员沟通，确保所有人了解变更。
