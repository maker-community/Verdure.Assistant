# 绿荫助手（Verdure Assistant）更新日志

本项目的所有重要更改都会记录在此文件中。

格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.0.0/)，
版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## [未发布]

### 新增
- 🎯 **项目重新定位** - 从单一"小智"服务扩展为多服务智能助手平台
- 🏗️ **项目重构** - 采用标准.NET开源项目目录结构
- 🔄 **自动化CI/CD** - GitHub Actions工作流
- 📝 **完整文档体系** - README、CONTRIBUTING、CHANGELOG
- 🛠️ **开发脚本** - PowerShell构建、测试和开发设置脚本
- 📋 **GitHub模板** - Issue和PR模板
- 🔒 **代码质量** - 代码分析和安全扫描
- 📦 **自动发布** - 基于标签的自动发布流程

### 变更
- 📁 **目录结构** - 重新组织项目文件结构
  - 将主要项目移动到 `src/` 目录
  - 将测试项目移动到 `tests/` 目录  
  - 将示例代码移动到 `samples/` 目录
  - 将文档移动到 `docs/` 目录
  - 将构建脚本移动到 `scripts/` 目录
- 🔧 **解决方案文件** - 更新项目引用路径
- 📖 **README更新** - 添加项目结构说明和开发指南

### 修复
- 🐛 **路径问题** - 修复项目间引用路径
- 🔗 **依赖关系** - 优化NuGet包引用

## [1.0.0] - 2025-05-30

### 新增
- 🎤 **语音交互** - 实时语音识别和合成
- 🌐 **通信协议** - WebSocket和MQTT双协议支持
- 🖥️ **用户界面** - WinUI 3桌面应用和控制台应用
- 🤖 **智能功能** - 自动对话模式和状态管理
- 🔊 **音频处理** - 基于Opus的高质量音频编解码
- ⚙️ **配置管理** - 灵活的配置系统
- 🔐 **安全通信** - WSS加密连接和验证机制
- 📱 **跨平台** - 支持Windows、Linux、macOS

### 技术架构
- 🏛️ **分层架构** - 清晰的服务层、通信层、核心层分离
- 🔌 **依赖注入** - 基于Microsoft.Extensions.DependencyInjection
- 📊 **日志系统** - 基于Microsoft.Extensions.Logging
- 🧪 **测试覆盖** - 完整的单元测试和集成测试

### 技术特性
- .NET 9.0支持
- 跨平台兼容性
- 音频编解码（Opus）
- 依赖注入架构
- 事件驱动设计
- 完整的日志记录

### 安全性
- WSS加密传输
- 安全的音频数据传输

[未发布]: https://github.com/maker-community/Verdure.Assistant/compare/v1.0.0...HEAD
[1.0.0]: https://github.com/maker-community/Verdure.Assistant/releases/tag/v1.0.0
