# Visual Studio 开发环境

使用 Visual Studio 2022 开发 Verdure Assistant 项目的完整指南。

## 环境配置

### 必需组件

- Visual Studio 2022 17.8 或更高版本
- .NET 9.0 SDK
- 以下工作负载：
  - .NET 桌面开发
  - ASP.NET 和 Web 开发
  - .NET Multi-platform App UI 开发

## 项目配置

### 打开解决方案

```bash
# 克隆项目
git clone https://github.com/maker-community/Verdure.Assistant.git
cd Verdure.Assistant

# 使用 Visual Studio 打开
start Verdure.Assistant.sln
```

### 设置启动项目

1. 在解决方案资源管理器中右键点击项目
2. 选择"设为启动项目"
3. 选择合适的项目（Console、WinUI、API 等）

## 调试技巧

### 断点调试

- 在关键代码行设置断点
- 使用条件断点筛选特定情况
- 利用数据断点监控变量变化

### 日志查看

- 使用输出窗口查看调试信息
- 配置日志级别和输出格式

## 扩展推荐

- **GitHub Extension** - Git 集成
- **Visual Studio IntelliCode** - AI 辅助编程
- **CodeMaid** - 代码清理和格式化