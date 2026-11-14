# 稳定性重构：状态机、唤醒词与连接生命周期

本文记录这一轮针对"唤醒词监听与其他功能冲突、软件不够健壮"所做的分析与修复。对应测试在
`tests/ConversationStateMachine.Tests`（69 个）与 `tests/DeepSeek.Tests`（227 个，其中若干为需设置环境变量才访问真实服务的冒烟测试）。

## 发现的问题 → 处理

| # | 问题 | 后果 | 处理 |
|---|---|---|---|
| 1 | 状态机在**锁内**触发事件；上下文用 `async void` 处理转换，快速连续转换时进入/退出动作交叉执行 | 重入死锁、状态与实际资源不一致 | 事件在锁外按序派发（重入安全）；上下文改为**单消费者队列**，严格 先退出后进入，被后续转换取代的状态不再执行进入动作 |
| 2 | `ServerDisconnected` 对 Connecting 的转换是 Connecting；连接失败后没有任何超时 | 状态永远停在"连接中" | 断线/连接失败一律回 Idle；Connecting 带**超时看门狗**；连接本身带超时与取消 |
| 3 | 聆听中检测到唤醒词会被当成**重新唤醒**（转 Connecting）| 对话被自己打断、重复连接 | Connecting/Listening 下忽略唤醒词；Speaking 下唤醒词是打断（转 Listening）；增加去抖 |
| 4 | 唤醒词识别 `RecognizeOnceAsync` 只识别一次；重启识别时**持有信号量等待下一次唤醒** | `Pause/Stop/Dispose` 会一直阻塞到再次说出唤醒词（界面卡死、退出卡住）；每次回到 Idle 都要 Stop+Start | 重写为**监督循环**：识别→冷却→自动重启；`Pause/Resume/Stop/切换模型` 只是向循环发信号，不再持锁等待；失败指数退避 |
| 5 | 对话期间唤醒词识别与上传/VAD 同时占用音频流 | 抢占、回声误触发 | 按状态协调：Idle 识别；Connecting/Listening 暂停；Speaking 可配置（`AllowKeywordInterruptWhileSpeaking`）|
| 6 | `StartVoiceChat` 直接进入 Listening，但没有连接时 `StartListening` 静默返回 | 停在 Listening 却什么都没发生 | 开始对话统一经过 Connecting（已连接时立即完成）|
| 7 | 连接完成事件在 TCP 连上时就切到 Listening，早于小智 `hello` 握手 | 在握手前发送 `listen` | 连接等待服务器 `hello`（有超时兼容）后才进入 Listening |
| 8 | `WebSocketClient.DisconnectAsync` 的 `CloseAsync(CancellationToken.None)`；`Dispose` 里 `.Wait()` | 对端不回应关闭帧时**无限期挂起**（关闭窗口卡死）| 关闭带 3 秒超时并 `Abort`；`Dispose` 有总超时 |
| 9 | 麦克风帧逐帧 `async void` 发送；错误时每帧都弹错误并触发恢复 | 乱序、错误刷屏 | 有界队列 + 单发送循环（队满丢最旧帧）；错误提示限频 |
| 10 | 界面"断开"调用 `Dispose()` 单例服务；API 的 disconnect 同理 | 断开后无法重新连接 | 新增 `DisconnectAsync()`，`InitializeAsync()` 幂等 |
| 11 | OTA 检查失败会让整个初始化失败 | 网络差时唤醒词/录音都起不来 | OTA 失败/超时不再阻断启动 |
| 12 | 打断源用空日志工厂创建；`AudioDataDistributor.Unsubscribe` 重建集合有竞态 | 无法诊断；偶发丢订阅 | 注入真实日志；订阅表改为写时复制 |
| 13 | 打包运行缺少 `microphone` 能力声明 | 初始化采集设备挂起 | 清单声明并在文档说明授权流程 |
| 14 | HomePage 每次导航都新建且从不清理 | 事件重复订阅、重复初始化 | 页面启用导航缓存；ViewModel 初始化幂等并显示失败原因 |
| 15 | `OnLaunched` 是 `async void` 且无任何异常处理，无单实例 | 启动失败静默崩溃；双开抢麦克风 | 分步容错启动 + 状态条；全局异常记录；单实例；有序关闭 |

## 新的状态转换（`ConversationStateMachine`）

| 当前 \ 触发 | 唤醒词 | 手动打断 | VAD 打断 | 开始对话 | 连接成功 | 断线/失败 | TTS 开始 | 回复结束 |
|---|---|---|---|---|---|---|---|---|
| **Idle** | Connecting | Connecting | Connecting | Connecting | – | Idle | **Speaking**（手动模式回复）| – |
| **Connecting** | 忽略 | Idle（取消）| 忽略 | 忽略 | Listening | Idle | – | – |
| **Listening** | 忽略 | Connecting（重新开始一轮）| Connecting | 忽略 | 忽略 | Idle | Speaking | – |
| **Speaking** | Listening（打断）| Listening | Listening | 忽略 | 忽略 | Idle | 忽略 | KeepListening ? Listening : Idle |

`RequestTransition(trigger, expectedState, context)` 允许异步回调带前置状态校验，避免"迟到的连接成功"把已取消的会话拉回 Listening。
`Epoch` 与最近 64 条转换历史（`GetHistory()`）用于诊断。
