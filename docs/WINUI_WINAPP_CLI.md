# WinUI 与 winapp CLI

WinUI 项目（`src/Verdure.Assistant.WinUI`）已适配 [Windows App Development CLI（winapp）](https://learn.microsoft.com/windows/apps/dev-tools/winapp-cli/)，
无需 Visual Studio 也可以运行、调试、打包。

## 已做的适配

| 项 | 说明 |
|---|---|
| `Microsoft.Windows.SDK.BuildTools.WinApp` | 已加入项目。`dotnet run` 会自动调用 `winapp run`：生成松散布局包、注册调试用包标识、以包标识启动应用 |
| `Package.appxmanifest` | 声明了 `runFullTrust`、`internetClient`，以及 **`microphone` 设备能力** |
| 未打包运行回退 | 没有包标识（`-p:WindowsPackageType=None`）时，设置存储自动改用 `%APPDATA%\Verdure.Assistant` 下的 JSON 文件，而不是因访问 `ApplicationData` 抛异常 |
| 单实例 | 重复启动只会激活已有窗口，避免两个进程同时占用麦克风与唤醒词模型 |
| 日志 | 滚动日志写入 `%LOCALAPPDATA%\Verdure.Assistant\logs`（打包运行时位于包的 `LocalCache\Local` 下），便于 `winapp run` 之外的现场排查 |

> 为什么要声明 `microphone`：打包运行的应用没有该声明时，初始化音频采集设备可能被系统静默拦截或挂起；声明后首次运行系统会弹出
> "是否允许访问麦克风"的授权框，授权后才会继续启动采集。

## 常用命令

```powershell
# 安装 winapp CLI（一次性）
winget install Microsoft.WinAppCli

cd src\Verdure.Assistant.WinUI

# 一步构建并以包标识运行（等价于 VS 的 F5）
dotnet run -p:Platform=x64

# 运行已构建好的输出
winapp run .\bin\x64\Debug\net10.0-windows10.0.19041.0\win-x64

# 无包标识运行（验证未打包回退逻辑）
winapp run -p WindowsPackageType=None

# 运行并捕获 OutputDebugString / 崩溃转储
dotnet run -p:Platform=x64 -p:WinAppRunDebugOutput=true

# 打包 MSIX（先 winapp cert generate 生成与清单 Publisher 匹配的开发证书）
winapp pack .\bin\x64\Release\net10.0-windows10.0.19041.0\win-x64 --cert .\devcert.pfx
```

关闭 `dotnet run` 的 winapp 集成：在命令行加 `-p:EnableWinAppRunSupport=false`。

## 启动流程（为什么界面不会再"卡白屏/崩溃无提示"）

1. 构建依赖注入宿主（不启动）→ **先显示主窗口**。
2. 后台逐步启动宿主、主题、MCP 设备。每一步独立容错，主窗口顶部的 `InfoBar` 显示
   *正在启动 / 部分功能不可用（含重试按钮）/ 启动失败*。
3. 未处理异常会被记录并以提示形式展示，而不是静默崩溃；关闭窗口时按顺序停止录音、唤醒词、连接，再释放宿主。
