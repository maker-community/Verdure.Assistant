# VS Code 开发环境

使用 Visual Studio Code 开发 Verdure Assistant 项目的配置指南。

## 环境配置

### 必需扩展

```bash
# 安装 C# 开发扩展
code --install-extension ms-dotnettools.csdevkit
code --install-extension ms-vscode.powershell
code --install-extension eamodio.gitlens
```

## 项目配置

### 打开项目

```bash
# 克隆并打开项目
git clone https://github.com/maker-community/Verdure.Assistant.git
cd Verdure.Assistant
code .
```

### 配置调试

创建 `.vscode/launch.json`：

```json
{
    "version": "0.2.0",
    "configurations": [
        {
            "name": "Console",
            "type": "coreclr",
            "request": "launch",
            "program": "${workspaceFolder}/src/Verdure.Assistant.Console/bin/Debug/net10.0/Verdure.Assistant.Console.dll",
            "args": [],
            "cwd": "${workspaceFolder}/src/Verdure.Assistant.Console",
            "console": "internalConsole",
            "stopAtEntry": false
        }
    ]
}
```

## 开发技巧

### 快捷键

- **Ctrl+Shift+P** - 命令面板
- **F5** - 开始调试
- **Ctrl+F5** - 运行不调试

### 任务配置

创建 `.vscode/tasks.json` 用于构建任务。