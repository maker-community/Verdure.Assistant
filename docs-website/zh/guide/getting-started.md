# 快速开始

欢迎来到绿荫助手（Verdure Assistant）的世界！本指南将帮助您快速了解和运行项目。

## 🎯 项目简介

绿荫助手是一个基于 .NET 9 的智能语音助手项目，提供了完整的语音交互解决方案。无论您是初学者还是有经验的开发者，都能从这个项目中学到现代 .NET 开发的精髓。

### 核心特性

- **🎤 语音交互**：支持"你好小电"和"你好小娜"唤醒词
- **🌐 跨平台**：支持 Windows、Linux、macOS
- **📱 多终端**：API、MAUI、WinUI、Console 四种部署方式
- **🏗️ 现代架构**：依赖注入、异步编程、模块化设计

## 🚀 5分钟快速体验

### 前提条件

确保您的开发环境满足以下要求：

- ✅ [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) 或更高版本
- ✅ [Git](https://git-scm.com/) 版本控制工具
- ✅ 代码编辑器（推荐 [Visual Studio 2022](https://visualstudio.microsoft.com/) 或 [VS Code](https://code.visualstudio.com/)）

### 步骤一：克隆项目

```bash
git clone https://github.com/maker-community/Verdure.Assistant.git
cd Verdure.Assistant
```

### 步骤二：检查环境

验证 .NET 环境是否正确安装：

```bash
dotnet --version
# 应该显示 9.0.x 或更高版本
```

### 步骤三：构建项目

```bash
# 还原 NuGet 包
dotnet restore

# 构建所有项目
dotnet build --configuration Release
```

### 步骤四：运行控制台版本（推荐首次体验）

控制台版本是最简单的入门方式：

```bash
cd src/Verdure.Assistant.Console
dotnet run
```

您将看到类似以下的交互菜单：

```
===============================
    绿荫助手 (Verdure Assistant)
===============================
请选择操作:
1. 开始语音对话
2. 停止语音对话  
3. 切换对话状态 (自动模式)
4. 切换自动对话模式
5. 发送文本消息
6. 查看连接状态
7. 退出
===============================
```

## 🎮 试用不同版本

### WinUI 桌面应用（Windows 专用）

如果您使用 Windows 10/11，可以体验图形界面版本：

```bash
cd src/Verdure.Assistant.WinUI
dotnet run
```

特点：
- 现代化的 Windows 应用界面
- 可视化语音状态显示
- 直观的控制按钮

### API 服务版本（适合服务器部署）

适合部署到服务器或树莓派：

```bash
cd src/Verdure.Assistant.Api
dotnet run
```

特点：
- RESTful API 接口
- 适合嵌入式设备
- 支持远程调用

## 📖 下一步学习

恭喜！您已经成功运行了绿荫助手。现在可以深入学习：

### 🏗️ 理解架构
- **[项目架构](/zh/guide/architecture)** - 了解系统设计思想
- **[技术栈介绍](/zh/guide/tech-stack)** - 掌握使用的技术和框架

### 🔧 开发环境
- **[环境搭建](/zh/guide/environment-setup)** - 配置完整的开发环境
- **[Visual Studio 开发](/zh/development/visual-studio)** - 使用 VS 进行开发
- **[VS Code 开发](/zh/development/vs-code)** - 使用轻量级编辑器开发

### 📱 深入项目
选择您感兴趣的项目类型深入学习：

- **[API 服务](/zh/projects/api)** - 学习后端服务开发
- **[MAUI 应用](/zh/projects/maui)** - 掌握跨平台移动开发
- **[WinUI 应用](/zh/projects/winui)** - 了解 Windows 桌面应用开发
- **[Console 应用](/zh/projects/console)** - 学习控制台应用开发

## ❓ 常见问题

### Q: 运行时提示缺少依赖？

**A:** 确保已安装 .NET 9 SDK，并运行 `dotnet restore` 恢复包：

```bash
dotnet restore
```

### Q: Windows 上无法运行 WinUI 版本？

**A:** WinUI 需要 Windows 10 1809 或更高版本，确保系统满足要求：

```bash
# 检查 Windows 版本
winver
```

### Q: 语音功能无法使用？

**A:** 语音功能需要：
1. 麦克风权限
2. 网络连接（连接到语音服务）
3. 音频设备正常工作

### Q: 编译出错？

**A:** 常见解决方案：
1. 清理并重新构建：`dotnet clean && dotnet build`
2. 检查 .NET 版本：`dotnet --version`
3. 更新 Visual Studio 到最新版本

## 🔍 故障排除

如果遇到问题：

1. **查看日志**：控制台输出通常包含详细的错误信息
2. **检查网络**：确保能访问外部服务
3. **验证权限**：确保应用有必要的系统权限
4. **查看文档**：参考 [故障排除指南](/zh/development/debugging)

## 💬 获取帮助

- **GitHub Issues**：[提交问题](https://github.com/maker-community/Verdure.Assistant/issues)
- **讨论区**：[技术讨论](https://github.com/maker-community/Verdure.Assistant/discussions)
- **贡献指南**：[参与开发](https://github.com/maker-community/Verdure.Assistant/blob/main/CONTRIBUTING.md)

---

现在您已经成功入门了绿荫助手！接下来选择您感兴趣的方向继续深入学习吧。🎉