# 让内置智能体只当唤醒词用（休眠隔离）

本文记录一个实测结论和它的用法：**把机器人内置的语音智能体（豆包）按回休眠，它就退化成纯粹的
唤醒词检测器**——不参与对话、不跑自己的识别/大模型、不发出自己的声音，而**外部播放和唤醒词都照常工作**。

这套做法是自建助手（`Verdure.Assistant.Bumi`）现在的默认行为，配置项 `Bumi:KeepBuiltInAgentAsleep`。

---

## 1. 为什么需要它

在自建助手的方案里，麦克风必须继续流向内置智能体（`InternalCaptureAudioDataToAgent = true`），
否则唤醒检测和扬声器会一起失效，而且**无法恢复**。于是之前的隔离手段只有"静音它的输出"
（`InternalAgentAudioDataToPlayback = false`），问题是：

- 它依然**醒着**：自己的 ASR/大模型照跑，网络与算力都在浪费；
- 它依然可能自己发声（实测中表现为"机器人一直在响别的声音"）；
- 语音内容仍然进它的云端，隐私上说不清。

## 2. 实测结论（2026-11-21，Bumi EDU，SDK 3.1.0）

| 验证项 | 方法 | 结果 |
| --- | --- | --- |
| 命令可用 | `SetSystemControl(ToWakeup/ToSleep, isNeedReply:false)` | 分别收到 `Wakeuped(CmdWakeuped)`、`Sleeped(CmdSleeped)` |
| **休眠时外部播放** | 休眠后播放 4 s 音乐（模块音量 100） | **正常出声** |
| **休眠时唤醒词** | 休眠后说"你好布米" | **上报 `Wakeuped(AudioWakeuped)`** |
| **唤醒 → 休眠 → 再唤醒** | 唤醒后立刻 `ToSleep`，再说一次唤醒词 | **再次上报 `AudioWakeuped`**，可循环 |
| 休眠时我们的采集 | 休眠状态下 `MediaTest record` | 8 通道采集照常出帧、有信号 |
| 状态上报会重复 | 一次切换收到 2 条相同事件 | 需要去抖（代码用 1 s） |

对照：`InternalCaptureAudioDataToAgent = false` **仍然禁止使用**——它会让唤醒和播放一起失效且不可恢复。

## 3. 实现细节

- 配置：`Bumi:KeepBuiltInAgentAsleep`（默认 `true`），见 [BumiAudioOptions.cs](../src/Verdure.Assistant.Bumi/BumiAudioOptions.cs)；
- 逻辑在 [BumiWakeWordBridge.cs](../src/Verdure.Assistant.Bumi/BumiWakeWordBridge.cs)：
  - 启动订阅状态后立刻 `ToSleep`；
  - 收到任何非 `Sleeped` 状态（`Ready` / `Wakeuped`）就安排一次休眠；
  - 唤醒之后再等 **500 ms** 才休眠，先让状态机落定，避免打断它自己的唤醒流程；
  - **1 s 去抖**，因为同一次切换会上报两条相同事件；
  - 休眠动作 `Task.Run` 到线程池，绝不在原生回调线程上阻塞；
  - 不再额外 `ToWakeup`：休眠本来就是模块的开机默认状态，服务退出后让它保持休眠即可。
- 保留 `MuteBuiltInAgent`（静音它的输出）作为双保险：一个关嘴，一个关耳朵。

## 4. 上线后的正常日志长相

```
Bumi media ready: volume 100, agent->speaker ..., cue ..., mic->agent True
语音模块状态: Sleeped（CmdSleeped）          ← 启动即休眠
语音模块状态: Wakeuped（AudioWakeuped）      ← 用户说"你好布米"
唤醒词触发，已开始一轮对话
语音模块状态: Sleeped（CmdSleeped）          ← 500 ms 后按回休眠
TTS started → Speaking                      ← 我们的回答照常播出
```

## 5. 复现 / 回归验证

不用耳朵也能判定的最小清单（服务停掉后执行）：

```bash
cd ~/board-media
export LD_LIBRARY_PATH=/path/to/native/build:$LD_LIBRARY_PATH
config=/path/to/config/dds.xml

dotnet MediaTest.dll "$config" system sleep      # 让它睡
dotnet MediaTest.dll "$config" playfile clip4s.wav /tmp/out   # 应能听到声音
dotnet MediaTest.dll "$config" status 60         # 说"你好布米"，应出现 Wakeuped reason=AudioWakeuped
dotnet MediaTest.dll "$config" system sleep      # 立刻再睡
dotnet MediaTest.dll "$config" status 60         # 再说一次，应再次出现 AudioWakeuped
```

> `MediaTest` 的 `status` 只在结束时打印事件明细（前 5 条），所以要在同一个窗口里完成"说话"这个动作。

## 6. 相关文档

| 文档 | 内容 |
| --- | --- |
| `noetix_sdk_bumi_csharp/docs/voice-assistant.md` | 自建助手整体方案、路由开关表、隐私边界 |
| `noetix_sdk_bumi_csharp/docs/verification.md` | 全部验证台账 |
| [AUDIO_QUALITY_TUNING_BUMI.md](AUDIO_QUALITY_TUNING_BUMI.md) | 扬声器音色链（另一条线：怎么让声音好听） |
