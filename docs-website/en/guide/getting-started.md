# Getting Started

Welcome to the world of Verdure Assistant! This guide will help you quickly understand and run the project.

## 🎯 Project Introduction

Verdure Assistant is an intelligent voice assistant project based on .NET 10, providing a complete voice interaction solution. Whether you're a beginner or an experienced developer, you can learn the essence of modern .NET development from this project.

### Core Features

- **🎤 Voice Interaction**: Supports "Hello Xiaodian" and "Hello Xiaonan" wake words
- **🌐 Cross-platform**: Supports Windows, Linux, macOS
- **📱 Multi-device**: API, MAUI, WinUI, Console deployment options
- **🏗️ Modern Architecture**: Dependency injection, asynchronous programming, modular design

## 🚀 5-Minute Quick Experience

### Prerequisites

Ensure your development environment meets the following requirements:

- ✅ [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or higher
- ✅ [Git](https://git-scm.com/) version control tool
- ✅ Code editor (recommended [Visual Studio 2026](https://visualstudio.microsoft.com/) or [VS Code](https://code.visualstudio.com/))

### Step 1: Clone the Project

```bash
git clone https://github.com/maker-community/Verdure.Assistant.git
cd Verdure.Assistant
```

### Step 2: Check Environment

Verify that the .NET environment is correctly installed:

```bash
dotnet --version
# Should display 9.0.x or higher
```

### Step 3: Build the Project

```bash
# Restore NuGet packages
dotnet restore

# Build all projects
dotnet build --configuration Release
```

### Step 4: Run Console Version (Recommended for First Experience)

The console version is the simplest way to get started:

```bash
cd src/Verdure.Assistant.Console
dotnet run
```

You will see an interactive menu similar to the following:

```
===============================
    Verdure Assistant
===============================
Please select an operation:
1. Start voice chat
2. Stop voice chat  
3. Toggle chat state (auto mode)
4. Toggle auto chat mode
5. Send text message
6. View connection status
7. Exit
===============================
```

## 🎮 Try Different Versions

### WinUI Desktop App (Windows Only)

If you're using Windows 10/11, you can experience the graphical interface version:

```bash
cd src/Verdure.Assistant.WinUI
dotnet run
```

Features:
- Modern Windows application interface
- Visual voice status display
- Intuitive control buttons

### API Service Version (Suitable for Server Deployment)

Suitable for deployment to servers or Raspberry Pi:

```bash
cd src/Verdure.Assistant.Api
dotnet run
```

Features:
- RESTful API interface
- Suitable for embedded devices
- Supports remote calls

## 📖 Next Learning Steps

Congratulations! You have successfully run Verdure Assistant. Now you can dive deeper:

### 🏗️ Understand Architecture
- **[Project Architecture](/en/guide/architecture)** - Understand system design philosophy
- **[Tech Stack Introduction](/en/guide/tech-stack)** - Master the technologies and frameworks used

### 🔧 Development Environment
- **[Environment Setup](/en/guide/environment-setup)** - Configure complete development environment
- **[Visual Studio Development](/en/development/visual-studio)** - Develop with VS
- **[VS Code Development](/en/development/vs-code)** - Develop with lightweight editor

### 📱 Deep Dive into Projects
Choose the project type that interests you for in-depth learning:

- **[API Service](/en/projects/api)** - Learn backend service development
- **[MAUI App](/en/projects/maui)** - Master cross-platform mobile development
- **[WinUI App](/en/projects/winui)** - Learn Windows desktop app development
- **[Console App](/en/projects/console)** - Learn console application development

## ❓ Frequently Asked Questions

### Q: Missing dependencies error when running?

**A:** Make sure .NET 10 SDK is installed and run `dotnet restore` to restore packages:

```bash
dotnet restore
```

### Q: Cannot run WinUI version on Windows?

**A:** WinUI requires Windows 10 1809 or higher, ensure your system meets the requirements:

```bash
# Check Windows version
winver
```

### Q: Voice functionality not working?

**A:** Voice functionality requires:
1. Microphone permissions
2. Network connection (to connect to voice services)
3. Audio devices working properly

### Q: Compilation errors?

**A:** Common solutions:
1. Clean and rebuild: `dotnet clean && dotnet build`
2. Check .NET version: `dotnet --version`
3. Update Visual Studio to the latest version

## 🔍 Troubleshooting

If you encounter problems:

1. **Check logs**: Console output usually contains detailed error information
2. **Check network**: Ensure access to external services
3. **Verify permissions**: Ensure the app has necessary system permissions
4. **Refer to documentation**: See [Debugging Guide](/en/development/debugging)

## 💬 Get Help

- **GitHub Issues**: [Submit issues](https://github.com/maker-community/Verdure.Assistant/issues)
- **Discussions**: [Technical discussions](https://github.com/maker-community/Verdure.Assistant/discussions)
- **Contributing Guide**: [Participate in development](https://github.com/maker-community/Verdure.Assistant/blob/main/CONTRIBUTING.md)

---

Now you have successfully got started with Verdure Assistant! Next, choose the direction that interests you to continue your in-depth learning. 🎉