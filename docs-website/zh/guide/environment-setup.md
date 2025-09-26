# 开发环境搭建

本章将详细指导您搭建完整的 Verdure Assistant 开发环境，包括必要的工具安装、配置和验证。

## 🎯 环境要求概览

| 组件 | 最低版本 | 推荐版本 | 必需性 |
|------|---------|----------|-------|
| .NET SDK | 9.0.0 | 最新版本 | ✅ 必需 |
| Visual Studio | 2022 17.8+ | 2022 最新版 | 🔶 可选 |
| VS Code | 最新版 | 最新版 | 🔶 可选 |
| Git | 2.30+ | 最新版 | ✅ 必需 |
| Windows SDK | 10.0.19041+ | 最新版 | 🔶 WinUI需要 |

## 🔧 核心环境安装

### 1. 安装 .NET 9 SDK

.NET 9 是项目的核心依赖，提供了最新的语言特性和性能优化。

#### Windows 安装

**方法一：官方安装器（推荐）**

1. 访问 [.NET 下载页面](https://dotnet.microsoft.com/download/dotnet/9.0)
2. 下载 ".NET 9.0 SDK" Windows 安装器
3. 运行安装器，按向导完成安装

**方法二：使用 winget（Windows 10/11）**

```powershell
# 安装最新版 .NET 9 SDK
winget install Microsoft.DotNet.SDK.9

# 或指定具体版本
winget install Microsoft.DotNet.SDK.9 --version 9.0.0
```

#### macOS 安装

**方法一：官方安装器**

1. 访问 [.NET 下载页面](https://dotnet.microsoft.com/download/dotnet/9.0)
2. 选择 macOS 对应的架构（Intel 或 Apple Silicon）
3. 下载并运行 .pkg 安装器

**方法二：使用 Homebrew**

```bash
# 安装 Homebrew（如果未安装）
/bin/bash -c "$(curl -fsSL https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh)"

# 安装 .NET 9
brew install --cask dotnet-sdk
```

#### Linux 安装

**Ubuntu/Debian：**

```bash
# 添加 Microsoft 包存储库
wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb

# 安装 .NET 9 SDK
sudo apt-get update
sudo apt-get install -y dotnet-sdk-9.0
```

**CentOS/RHEL/Fedora：**

```bash
# 添加 Microsoft 包存储库
sudo rpm -Uvh https://packages.microsoft.com/config/centos/8/packages-microsoft-prod.rpm

# 安装 .NET 9 SDK
sudo dnf install dotnet-sdk-9.0
```

#### 验证安装

```bash
# 检查版本
dotnet --version
# 应该显示 9.0.x

# 查看安装的 SDK
dotnet --list-sdks

# 查看运行时
dotnet --list-runtimes
```

### 2. 安装 Git

Git 用于版本控制和获取项目源码。

#### Windows
```powershell
# 使用 winget
winget install Git.Git

# 或下载安装器：https://git-scm.com/download/win
```

#### macOS
```bash
# 使用 Homebrew
brew install git

# 或使用 Xcode Command Line Tools
xcode-select --install
```

#### Linux
```bash
# Ubuntu/Debian
sudo apt-get install git

# CentOS/RHEL/Fedora
sudo dnf install git
```

#### Git 配置

```bash
# 配置用户信息
git config --global user.name "您的姓名"
git config --global user.email "您的邮箱"

# 配置编辑器（可选）
git config --global core.editor "code --wait"  # VS Code
# 或
git config --global core.editor "notepad"      # Windows 记事本
```

## 🛠️ 开发工具选择

### Option 1: Visual Studio 2022（推荐 Windows 用户）

Visual Studio 是微软官方的集成开发环境，对 .NET 项目支持最完整。

#### 安装 Visual Studio 2022

1. 访问 [Visual Studio 下载页面](https://visualstudio.microsoft.com/downloads/)
2. 选择 "Community"（免费版）或其他版本
3. 在安装器中选择以下工作负载：

**必需工作负载：**
- ✅ ".NET 桌面开发"
- ✅ "ASP.NET 和 Web 开发"

**可选工作负载（根据需要）：**
- 🔶 ".NET Multi-platform App UI 开发" (MAUI)
- 🔶 "通用 Windows 平台开发" (WinUI)

#### 推荐扩展

安装这些扩展提升开发体验：

```
- GitHub Extension for Visual Studio
- Visual Studio IntelliCode
- CodeMaid
- Productivity Power Tools
```

### Option 2: Visual Studio Code（跨平台首选）

VS Code 是轻量级但功能强大的跨平台编辑器。

#### 安装 VS Code

**Windows:**
```powershell
winget install Microsoft.VisualStudioCode
```

**macOS:**
```bash
brew install --cask visual-studio-code
```

**Linux:**
```bash
# Ubuntu/Debian
wget -qO- https://packages.microsoft.com/keys/microsoft.asc | gpg --dearmor > packages.microsoft.gpg
sudo install -o root -g root -m 644 packages.microsoft.gpg /etc/apt/trusted.gpg.d/
sudo sh -c 'echo "deb [arch=amd64,arm64,armhf signed-by=/etc/apt/trusted.gpg.d/packages.microsoft.gpg] https://packages.microsoft.com/repos/code stable main" > /etc/apt/sources.list.d/vscode.list'
sudo apt update
sudo apt install code
```

#### 必需扩展

```json
{
  "recommendations": [
    "ms-dotnettools.csharp",
    "ms-dotnettools.csdevkit",
    "ms-vscode.vscode-json",
    "ms-vscode.powershell",
    "eamodio.gitlens",
    "ms-vscode.vscode-markdown"
  ]
}
```

安装扩展：

```bash
# C# 开发套件
code --install-extension ms-dotnettools.csdevkit

# PowerShell 支持
code --install-extension ms-vscode.powershell

# Git 增强
code --install-extension eamodio.gitlens
```

## 🔧 项目特定配置

### Windows 开发（WinUI 支持）

如果您计划开发 WinUI 应用，需要额外配置：

```powershell
# 检查 Windows SDK 版本
reg query "HKEY_LOCAL_MACHINE\SOFTWARE\Wow6432Node\Microsoft\Microsoft SDKs\Windows\v10.0" /s

# 启用开发者模式
# 设置 > 更新和安全 > 开发者选项 > 开发者模式
```

### 音频开发支持

Verdure Assistant 包含音频处理功能，可能需要额外的系统组件：

**Windows:**
```powershell
# 确保 Windows Media Player 功能已安装
dism /online /enable-feature /featurename:MediaPlayback
```

**Linux:**
```bash
# 安装音频开发库
sudo apt-get install libasound2-dev  # ALSA
sudo apt-get install libpulse-dev    # PulseAudio
```

**macOS:**
```bash
# 通常不需要额外配置，系统自带音频支持
```

## ✅ 环境验证

创建一个简单的测试项目来验证环境：

```bash
# 创建测试目录
mkdir dotnet-test && cd dotnet-test

# 创建控制台项目
dotnet new console

# 运行项目
dotnet run
```

如果看到 "Hello, World!" 输出，说明基础环境配置成功。

### 高级验证

```bash
# 检查支持的项目模板
dotnet new list

# 测试 Web API 项目
dotnet new webapi -n TestApi
cd TestApi
dotnet run

# 应该在 https://localhost:5001 启动 API 服务
```

## 🐛 常见问题解决

### .NET SDK 安装问题

**问题：** 提示版本不匹配或找不到 SDK

**解决方案：**
```bash
# 清理已安装的版本
dotnet --list-sdks
# 卸载旧版本后重新安装

# Windows: 使用 "添加或删除程序"
# macOS: 删除 /usr/local/share/dotnet 目录
# Linux: 使用包管理器卸载
```

### Visual Studio 工作负载问题

**问题：** 缺少项目模板或编译错误

**解决方案：**
1. 打开 Visual Studio Installer
2. 修改现有安装
3. 确保安装了必需的工作负载
4. 重启 Visual Studio

### VS Code 扩展问题

**问题：** C# 智能提示不工作

**解决方案：**
```bash
# 重新安装 C# 扩展
code --uninstall-extension ms-dotnettools.csharp
code --install-extension ms-dotnettools.csdevkit

# 重启 VS Code
```

### 权限问题（Linux/macOS）

**问题：** 无法安装全局工具或访问端口

**解决方案：**
```bash
# 设置 dotnet 工具路径
export PATH="$PATH:$HOME/.dotnet/tools"

# 添加到 shell 配置文件
echo 'export PATH="$PATH:$HOME/.dotnet/tools"' >> ~/.bashrc
```

## 📚 下一步

环境搭建完成后，您可以继续：

1. **[快速开始](/zh/guide/getting-started)** - 运行第一个项目
2. **[技术栈介绍](/zh/guide/tech-stack)** - 了解项目技术体系
3. **[项目架构](/zh/guide/architecture)** - 理解系统设计
4. **[Visual Studio 开发](/zh/development/visual-studio)** - 深入 VS 开发技巧

## 💡 开发技巧

### 性能优化配置

```xml
<!-- 项目文件中的性能优化设置 -->
<PropertyGroup>
  <PublishReadyToRun>true</PublishReadyToRun>
  <PublishSingleFile>true</PublishSingleFile>
  <PublishTrimmed>true</PublishTrimmed>
</PropertyGroup>
```

### 开发脚本

创建常用的开发脚本：

**Windows (PowerShell):**
```powershell
# dev-setup.ps1
Write-Host "检查开发环境..."
dotnet --version
git --version
Write-Host "环境检查完成！"

# 添加到 PATH 或创建快捷方式
```

**Linux/macOS (Bash):**
```bash
#!/bin/bash
# dev-setup.sh
echo "检查开发环境..."
dotnet --version
git --version
echo "环境检查完成！"

chmod +x dev-setup.sh
```

---

恭喜！您已经完成了 Verdure Assistant 开发环境的搭建。现在可以开始愉快地开发了！🎉