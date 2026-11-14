# 绿荫助手（Verdure Assistant）更新日志

本项目的所有重要更改都会记录在此文件中。

格式基于 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.0.0/)，
版本号遵循 [语义化版本](https://semver.org/lang/zh-CN/)。

## [未发布]

### 稳定性与新功能（本次大版本重构）
- 🧠 **DeepSeek 接入**：完整的 DeepSeek API 客户端（流式/非流式、思考模式与思考强度、`reasoning_content` 回传规则、工具调用、联网工具、JSON 输出、FIM/前缀续写、模型列表、余额、错误码与重试），WinUI 新增 DeepSeek 对话页。详见 [docs/DEEPSEEK_INTEGRATION.md](docs/DEEPSEEK_INTEGRATION.md)
- 🔄 **对话状态机重写**：事件锁外有序派发、动作单队列串行执行、连接超时看门狗、断线回 Idle、唤醒词/打断按状态明确处理、过期回调保护与转换历史。详见 [docs/STABILITY_REFACTORING.md](docs/STABILITY_REFACTORING.md)
- 🎯 **唤醒词服务重写**：监督循环 + 冷却 + 退避，修复 `Pause/Stop/Dispose` 阻塞到下一次唤醒才返回的问题；对话期间自动暂停，避免与上传/VAD 抢占音频流
- 🔌 **连接生命周期**：等待小智 `hello` 握手、连接/关闭超时、`DisconnectAsync()` 取代对单例 `Dispose()`、`InitializeAsync()` 幂等、OTA 失败不再阻断启动、麦克风帧有序有界发送
- 🪟 **WinUI**：支持 winapp CLI（`dotnet run` / `winapp run` / `winapp pack`），清单声明麦克风能力，未打包运行回退文件设置；启动先显示窗口并展示加载/降级/失败状态条；单实例；全局异常记录；有序关闭；滚动文件日志；页面导航缓存。详见 [docs/WINUI_WINAPP_CLI.md](docs/WINUI_WINAPP_CLI.md)
- 🔐 API Key 保存到 Windows 凭据库（`ISecretStore`），不写入设置 JSON
- ✅ 新增 `ConversationStateMachine.Tests`（状态机/上下文/唤醒词/VoiceChatService/播放器抖动缓冲，69 个）与 `DeepSeek.Tests`（含语音客户端、端点检测、语音对话编排、图像理解，251 个）
- 🔑 **设备身份与鉴权修复**：设置页的 Device ID / Token / OTA / WebSocket 地址现在真正生效（此前只保存不使用，WebSocket 的 Token 被写死为 `test`）；取不到物理网卡 MAC 时（如只有虚拟网卡）不再得到空 Device ID（会导致 OTA 报 `Device ID is required`、拿不到激活验证码），改为生成稳定的本机标识；握手阶段被服务器断开时给出明确提示
- 📋 **日志更清晰**：连接日志显示 Device ID/Token 来源（Token 脱敏）、服务器关闭连接的状态码与原因；唤醒词每 30 秒输出心跳（音频帧数与峰值电平，可判断麦克风是否在送音频），命中时记录模型名、监听时长与音量；修复连接被取消后出现"未被观察的任务异常"；修复连接失败后按住说话被永久锁定（`IsWaitingForResponse` 卡 true）
- ⏱️ **DeepSeek 语音响应更快、更稳**：每轮输出"判停 → 识别 → 模型首字 → 首段语音"的延迟拆分日志；语音对话默认关闭思考模式（首字更快，可在设置中开启）；判停等待默认由 900ms 降到 700ms 并可调（400~1500ms）；端点检测的噪声底改为自适应（稳定背景噪声会被学成噪声，不再让一句话被拖长），并新增"环境噪声"档位；识别的同时提前建立 TTS 连接，第一个短句在逗号处就先送出
- 👁️ **DeepSeek 图像理解 + 真实摄像头/截屏**：DeepSeek 页支持附图提问（选择文件、粘贴、摄像头拍照、截屏）；"拍照看看/看看屏幕"工具现在真正取图并交给 DeepSeek 图像理解（此前相机是模拟的，返回 1×1 假图，识图总是失败）；清单新增 `webcam` 能力。详见 [docs/DEEPSEEK_INTEGRATION.md](docs/DEEPSEEK_INTEGRATION.md)
- 🗣️ **DeepSeek 连续语音对话**：接入 DeepSeek 官方语音识别（ASR）与流式语音合成（TTS），实现"麦克风 → 端点检测 → DeepSeek 识别 → 思考/工具 → 边生成边朗读 → 自动继续聆听"，支持连续对话、按住说话、打断、唤醒词进入；首页新增"小智 / DeepSeek"后端切换，DeepSeek 页新增语音按钮与音色（stella/tide）。详见 [docs/DEEPSEEK_INTEGRATION.md](docs/DEEPSEEK_INTEGRATION.md)
- 🔊 **TTS 播放抖动缓冲**：播放器原先几乎没有缓冲（约 62ms 就开播），小智服务器按实时速度下发音频，网络稍有抖动就会"断断续续"。现在首帧预缓冲 240ms，缓冲被播空时暂停并重新缓冲 160ms；打断/停止现在会真正停止播放并丢弃服务器后续下发的音频（此前 `StopAsync` 的停止调用被注释，打断后声音仍会继续）；TTS 无音频或比预缓冲更短时也能正确结束，避免状态机卡在"说话中"；结束时输出帧数、最大帧间隔与欠载次数
- 🎨 **WinUI 界面焕新**：自定义标题栏 + 紧凑侧边导航；首页改为“状态主卡（头像光环 + 状态胶囊 + 回复气泡 + 连续对话/按住说话/打断）+ 对话记录 + 唤醒词/音乐/系统侧栏”，按 Offline/Idle/Connecting/Listening/Speaking 切换配色，宽屏双栏、窄屏单栏；DeepSeek 页与设置页居中卡片化并加入空状态；修复设置页协议下拉框不显示/保存异常的问题；`HomePageViewModel` 新增 `IsServiceReady` / `UiPhase`，修复“连接”按钮语义失效

### 🌟 重大技术突破

#### **MCP (Model Context Protocol) 协议集成** 
绿荫助手成为首个完整实现 xiaozhi-esp32 MCP 协议规范的 .NET 平台智能助手，实现了：
- 85% 协议合规性，支持完整的 JSON-RPC 2.0 通信流程
- IoT 设备自动发现和工具动态注册
- 实时设备状态同步和双向通信
- 语音聊天系统与 IoT 设备的无缝集成

### 新增
- 🚀 **MCP (Model Context Protocol) 协议支持** - 完整实现了 xiaozhi-esp32 MCP 协议规范
  - **MCP 服务器集成** - 支持 IoT 设备的 JSON-RPC 2.0 通信协议
  - **设备自动发现** - 自动检测和注册支持 MCP 的 IoT 设备
  - **工具动态注册** - 从 MCP 设备自动加载可用工具和功能
  - **设备状态同步** - 实时设备状态管理和更新
  - **协议合规性** - 85% MCP 协议规范符合度，完整的错误处理
- 🎙️ **语音活动检测 (VAD) 增强** - 实现连续监控和智能打断功能
  - **连续监控** - 在 AI 语音播放期间监控用户语音输入
  - **智能打断** - 检测到用户语音时立即中断 AI 回复
  - **状态感知处理** - 根据设备状态使用不同的能量阈值和语音窗口
  - **自动激活** - 空闲状态下检测到语音自动开始监听
- 🔄 **自动对话模式** - 实现基于 py-xiaozhi 的连续对话功能
  - **持续监听** - `KeepListening` 模式实现无缝对话体验
  - **状态管理** - 完整的设备状态机 (Idle, Connecting, Listening, Speaking)
  - **对话状态切换** - `ToggleChatStateAsync()` 手动控制对话流程
- 🔍 **关键词检测服务** - WinUI 平台连续关键词识别
  - **连续识别** - 解决 Microsoft Cognitive Services 的单次识别限制
  - **自动重启机制** - 关键词检测后自动重启识别器
  - **竞态条件防护** - 防止并发检测处理，确保状态一致性
- ⚙️ **完整设置系统** - 基于 Microsoft TemplateStudio 的泛型设置管理
  - **跨平台接口** - 泛型 `ISettingsService<T>` 接口设计
  - **Windows 平台实现** - 使用 ApplicationData 的本地存储
  - **导入导出功能** - 设置文件的导入导出和备份恢复
- 🏗️ **MVVM 架构重构** - WinUI 项目完整 MVVM 模式实现
  - **命令绑定** - 所有按钮事件转换为命令绑定模式
  - **属性绑定修复** - 修复所有 ViewModel 和 View 之间的绑定
  - **转换器实现** - 布尔值取反等自定义转换器
- 📸 **应用截图** - 添加 WinUI 应用界面截图展示
- 📖 **文档完善** - 更新 README 主页，添加应用截图区域

### 改进
- 🔧 **MCP 协议实现优化**
  - **响应处理修复** - 完善初始化、工具列表、工具调用响应处理
  - **错误恢复机制** - 增强 JSON-RPC 错误处理和自动恢复
  - **设备集成服务** - `McpIntegrationService` 桥接语音聊天和 IoT 设备
  - **WebSocket 集成** - MCP 消息封装和 WebSocket 协议整合
- 🎤 **语音中断功能增强**
  - **实时处理** - 16kHz 16-bit 单声道音频 20ms 帧分析
  - **智能阈值** - 说话状态 300.0 能量阈值，空闲状态 500.0 阈值
  - **快速响应** - 100ms 内检测并执行中断操作
- 🔊 **音频系统优化**
  - **资源冲突修复** - 解决 PortAudio 和系统音频服务的资源竞争
  - **编解码优化** - Opus 音频编解码器性能提升
  - **流管理改进** - 音频流的创建、管理和销毁优化
- 🔗 **连接稳定性提升**
  - **自动重连** - WebSocket 连接断线自动恢复机制
  - **连接状态管理** - 实时连接状态监控和用户界面更新
  - **错误处理** - 网络错误的智能处理和用户反馈
- 🧪 **测试覆盖扩展**
  - **MCP 协议测试** - 完整的协议合规性测试套件
  - **集成测试** - MCP WebSocket 集成测试和验证
  - **响应处理测试** - JSON-RPC 响应解析和处理验证

### 变更
- 🖼️ **资源管理** - 规范化应用截图和资源文件组织
- 📁 **目录结构** - 重新组织项目文件结构
  - 将主要项目移动到 `src/` 目录
  - 将测试项目移动到 `tests/` 目录  
  - 将示例代码移动到 `samples/` 目录
  - 将文档移动到 `docs/` 目录
  - 将构建脚本移动到 `scripts/` 目录
- 🔧 **解决方案文件** - 更新项目引用路径
- 📖 **README增强** - 添加应用截图展示，完善功能特性说明
- 🏛️ **架构升级** - 从单一"小智"服务扩展为多服务智能助手平台

### 修复
- 🐛 **SPXERR_INVALID_HANDLE 错误** - 修复语音识别服务的句柄管理问题
- 🔄 **线程同步问题** - 解决多线程环境下的竞态条件和死锁问题
- 🔌 **音频资源冲突** - 修复 PortAudio 资源占用和释放问题
- 🌐 **WebSocket 连接** - 修复连接管理和消息处理的稳定性问题
- 🎯 **关键词检测** - 修复连续识别中断和状态不一致问题
- 🔗 **依赖关系** - 优化 NuGet 包引用和项目间依赖
- 🐛 **路径问题** - 修复项目间引用路径

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
- 🔄 **MCP 协议基础** - 为 IoT 设备集成奠定基础架构

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
