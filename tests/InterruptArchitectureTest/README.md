# 打断架构测试项目

这个项目是一个独立的测试程序，用于验证新的打断架构设计的可行性和功能。

## 项目结构

```
InterruptArchitectureTest/
├── Core/                          # 核心接口和基类
│   ├── IInterruptSource.cs        # 打断源接口
│   ├── IInterruptService.cs       # 打断服务接口
│   ├── InterruptSourceBase.cs     # 打断源基类
│   ├── InterruptService.cs        # 打断服务实现
│   └── InterruptEventArgs.cs      # 事件参数定义
├── Sources/                       # 具体打断源实现
│   ├── HotkeyInterruptSource.cs   # 热键打断源
│   ├── KeywordInterruptSource.cs  # 关键字打断源
│   ├── VoiceActivityInterruptSource.cs # 语音活动打断源
│   └── ManualInterruptSource.cs   # 手动打断源
├── Program.cs                     # 主程序
└── README.md                      # 本文档
```

## 功能特性

### 1. 打断源类型

- **热键打断源 (HotkeyInterruptSource)**
  - 监听 F3 键
  - 使用 Windows API 注册全局热键
  - 支持防抖机制

- **关键字打断源 (KeywordInterruptSource)**
  - 支持 Microsoft 认知服务离线关键字检测
  - 提供简化版本用于测试（模拟检测）
  - 可配置多个关键字

- **语音活动打断源 (VoiceActivityInterruptSource)**
  - 基于 SoundFlow 的 VAD 检测
  - 提供简化版本用于测试（模拟检测）
  - 支持连续帧检测和阈值配置

- **手动打断源 (ManualInterruptSource)**
  - 支持程序化触发
  - 即时触发和队列触发两种模式

- **定时器打断源 (TimerInterruptSource)**
  - 按设定间隔触发打断
  - 用于演示定期任务触发

### 2. 核心架构

- **IInterruptSource 接口**: 统一的打断源抽象
- **InterruptSourceBase 基类**: 提供通用功能实现
- **IInterruptService 接口**: 打断服务管理接口
- **InterruptService 实现**: 统一的打断事件协调器

### 3. 关键特性

- **字符串类型的打断类型**: 便于扩展和自定义
- **优先级系统**: 支持不同优先级的打断处理
- **冷却期机制**: 防止重复触发
- **暂停/恢复功能**: 运行时控制打断源状态
- **事件驱动架构**: 解耦的设计模式

## 使用说明

### 编译和运行

```bash
cd tests/InterruptArchitectureTest
dotnet build
dotnet run
```

### 操作指南

程序启动后，支持以下操作：

1. **按 F3 键**: 触发热键打断
2. **输入 'manual'**: 触发手动打断
3. **输入 'pause <source>'**: 暂停指定打断源
4. **输入 'resume <source>'**: 恢复指定打断源
5. **输入 'status'**: 查看所有打断源状态
6. **输入 'quit'**: 退出程序

### 自动演示

程序会自动演示以下功能：
- 关键字检测模拟（随机触发）
- 语音活动检测模拟（每30秒左右触发）
- 定时器打断（每30秒固定触发）

## 测试场景

### 1. 基础功能测试
- 验证各种打断源能否正确启动和停止
- 测试事件触发和处理机制
- 验证冷却期和防抖功能

### 2. 并发处理测试
- 同时触发多个打断源
- 验证优先级处理逻辑
- 测试资源竞争情况

### 3. 状态管理测试
- 运行时暂停和恢复打断源
- 测试异常情况下的状态恢复
- 验证资源清理机制

### 4. 扩展性测试
- 添加自定义打断源
- 测试新的打断类型
- 验证配置灵活性

## 设计优势

1. **模块化设计**: 每个打断源独立实现，便于维护
2. **可扩展性**: 可以轻松添加新的打断源类型
3. **测试友好**: 每个组件都可以独立测试
4. **配置灵活**: 支持运行时启用/禁用打断源
5. **性能优化**: 事件驱动减少CPU占用

## 下一步计划

1. 集成到主项目的 VoiceChatService
2. 添加更多实际的打断源实现
3. 完善错误处理和恢复机制
4. 添加配置文件支持
5. 性能优化和压力测试

## 注意事项

- 热键功能需要管理员权限才能注册全局热键
- 语音功能需要麦克风权限
- 部分功能在调试模式下可能表现不同
- 建议在真实环境中测试所有功能
