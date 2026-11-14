# DeepSeek 接入

除了小智（xiaozhi）服务器，绿荫助手现在可以直接对接 [DeepSeek API](https://api-docs.deepseek.com/zh-cn/)。
WinUI 顶部导航新增 **DeepSeek** 页面：流式显示思维链与回答、调用本地工具（音乐/设备等 MCP 工具）、显示用量与余额。

## 能力对照

| DeepSeek 能力 | 对应实现 |
|---|---|
| 对话补全 `POST /chat/completions`（流式/非流式） | `DeepSeekClient.CreateChatCompletionAsync / StreamChatCompletionAsync` |
| 思考模式 `thinking` + `reasoning_effort`（low/high/max） | `DeepSeekOptions.ThinkingEnabled / ReasoningEffort`；思考模式下不发送 `temperature`，并校验 `tool_choice` |
| `reasoning_content` 回传规则 | 请求带 `tools` 时，历史 assistant 消息的 `reasoning_content` 全部回传（否则 API 400）；不带 `tools` 时自动剔除以节省 token |
| 工具调用 / `strict`（Beta） | `DeepSeekChatSession` 内置工具循环（最多 8 轮、并行执行、失败以错误 JSON 回传给模型）；`McpDeepSeekToolExecutor` 把本地 MCP 工具暴露给模型 |
| 联网工具 `deepseek_web_search` / `deepseek_read_url` | `DeepSeekOptions.EnableWebSearch / EnableReadUrl` |
| JSON 输出 | `DeepSeekChatSession.AskJsonAsync<T>` |
| FIM 补全 / 对话前缀续写（Beta） | `CreateFimCompletionAsync` / `CreatePrefixCompletionAsync` |
| 模型列表、余额 | `ListModelsAsync` / `GetBalanceAsync`（页面显示余额） |
| 错误码 400/401/402/422/429/500/503 | `DeepSeekApiException`（`IsAuthenticationError / IsInsufficientBalance / IsRateLimited`），429/5xx/网络错误指数退避重试并遵循 `Retry-After` |
| 缓存命中统计 | 回答下方显示 `缓存命中 / 思考 token` |

模型：`deepseek-flash`（默认）、`deepseek-v4-pro`（旧名 `deepseek-v4-flash` 仍可用）。

## 使用

1. 打开 **DeepSeek** 页 → **设置**，填入 API Key（也可以预先设置环境变量 `DEEPSEEK_API_KEY`）。
   API Key 保存在 **Windows 凭据库**，不会写入设置 JSON，也不会出现在导出的设置里。
2. 选择模型、是否思考及强度、是否联网、是否允许调用本地工具、是否朗读回复，点击保存。
3. 输入消息，`Enter` 发送、`Shift+Enter` 换行；生成过程中可点击 **停止**，被停止/失败的一轮不会进入上下文。

## DeepSeek 图像理解

`deepseek-flash` 支持在文本之外输入图片（[官方文档](https://api-docs.deepseek.com/zh-cn/guides/vision)：JPEG / PNG / GIF / WebP，内联 base64 或 URL，图片可出现在 user/system/tool 消息中）。接入后有三种用法：

| 用法 | 怎么用 |
|---|---|
| **DeepSeek 页发图提问** | 输入栏右侧：📎 选择图片（可多选）、📷 用摄像头拍一张、🖥 截取当前屏幕；也可以直接 `Ctrl+V` 粘贴剪贴板里的图片。附件显示在输入框上方，输入问题（可留空，默认"请描述这张图片"）后发送。图片会保留在上下文里，可以继续追问。 |
| **让 AI 自己拍照看** | 说/打字"拍张照看看这是什么"：模型调用 `self.camera.take_photo`，真正用摄像头拍照，再交给 DeepSeek 图像理解，把结果念给你。语音对话（DeepSeek 或小智后端）同样可用。 |
| **让 AI 看屏幕** | "看看我屏幕上写了什么"：调用 `self.screen.take_screenshot`，截取主显示器后识别。 |

要点：

- 图片在本机缩放到长边 ≤ 1280（屏幕 ≤ 1600）并压成 JPEG 后再上传（DeepSeek 内部也会把图片缩放到约 1300×1300，更大的图只会增加上传时间）；**图片只在内存中处理，不会保存到磁盘**。
- 拍照/截屏工具用独立的一次请求（`DeepSeekVisionService`，不开思考，更快），不污染对话历史；DeepSeek 页里直接附图则走正常对话（`DeepSeekChatSession.SendAsync(text, images)`）。
- 摄像头通过 `MediaCapture` 访问，清单已声明 `webcam` 能力，首次使用 Windows 会请求授权；被拒绝/被占用/没有摄像头时，会返回明确的原因而不是假装拍成功。
- 没有配置 DeepSeek API Key 时，相机工具回退到小智服务器的识图接口（需要服务器提供地址）。
- 以前相机工具只是"模拟拍照"（返回一张 1×1 像素的假 JPEG），所以识图总是"服务找不到"；现在是真实取图。

| 类型 | 位置 |
|---|---|
| 图片/内容块模型 | `DeepSeekImage`、`DeepSeekContentPart`、`DeepSeekMessage.User(text, images)`（`content` 自动变成块数组） |
| 图像理解 | `IVisionService` / `DeepSeekVisionService` |
| 取图 | `IImageCaptureService`（Core）、`WinUIImageCaptureService`（摄像头 + GDI 截屏） |
| 设备工具 | `McpCameraDevice`：`self.camera.capture`、`self.camera.take_photo`、`self.screen.take_screenshot` |

## 代码接入

```csharp
services.AddDeepSeek(o => o.ApiKey = key);        // 客户端 + 选项
services.AddDeepSeekMcpTools();                     // 把已注册的 McpServer 暴露为工具

var session = provider.GetRequiredService<DeepSeekChatSession>();
await foreach (var ev in session.SendAsync("现在播放一首周杰伦的歌"))
{
    switch (ev)
    {
        case DeepSeekChatEvent.ReasoningDelta r: /* 思维链增量 */ break;
        case DeepSeekChatEvent.ContentDelta c:   /* 回答增量 */ break;
        case DeepSeekChatEvent.ToolCallRequested t: /* 模型请求调用工具 */ break;
        case DeepSeekChatEvent.Completed done: /* 完整回答 + 用量 */ break;
        case DeepSeekChatEvent.Failed f: /* 错误或取消；历史已自动回滚 */ break;
    }
}
```

## DeepSeek 语音对话

DeepSeek 官方提供 [语音识别](https://api-docs.deepseek.com/zh-cn/guides/asr)（`POST /v1/tools/asr`，整段上传、最长 120 秒、不支持流式）和
[语音合成](https://api-docs.deepseek.com/zh-cn/guides/tts)（`wss://api.deepseek.com/v1/tools/tts` 流式，输出 24 kHz 单声道 PCM，音色 `stella` 女声 / `tide` 男声）。
绿荫助手用它们拼出完整的连续语音对话，不依赖小智服务器：

```
麦克风 ──► 端点检测(UtteranceDetector) ──► DeepSeek ASR ──► DeepSeekChatSession（思考/工具/历史）
                                                                  │ 流式回答
                      播放器(抖动缓冲) ◄── DeepSeek TTS(WebSocket) ◄── SpeechTextChunker(按句切分)
```

- **切换后端**：首页右上角的下拉框选择 **小智 / DeepSeek**，选择会被保存。之后**唤醒词（小点）、连续对话、按住说话、打断**都按所选后端执行；
  在 DeepSeek 页点"语音对话"等同于切到 DeepSeek 后端。两个后端互斥，切换时会先停掉另一个。
- **连续对话**：回复读完自动继续听；12 秒内没人说话（首句）或 25 秒无人接话就自动结束。**按住说话**：按住录音，松开后识别并回复一次。
- **打断**：点"打断"、或在朗读时再说唤醒词，会立刻停止思考/朗读并回到聆听。思考与朗读期间不收音，避免喇叭回声被当成用户说话。
- **边生成边朗读**：回复按句子切分后立即送入 TTS，不必等整段生成完。
- **同一份对话历史**：语音与文字共用 `DeepSeekChatSession`，语音轮次也会显示在 DeepSeek 页；本地 MCP 工具（音乐、设备）在语音中同样可用。
- **设置**：DeepSeek 页设置里可选"语音音色"；API Key、模型等在启动时就会应用，不必先打开 DeepSeek 页。
- **计费**：ASR 按音频时长，TTS 按字符数（中文按 2 个字符计），见官方文档。

### 响应速度：从"说完"到"开口"花在哪

每轮对话结束时日志会输出一行拆分，例如：

```
延迟拆分（从你说完算起）：判停等待 700ms → 上传+识别 1032ms → 模型首字 843ms → 首段语音 2015ms；说完到开口共 3747ms（思考模式：关）
```

| 阶段 | 说明 | 怎么缩短 |
|---|---|---|
| 判停等待 | 检测到你停了多久才认为"说完了"（默认 700ms，句尾静音必须连续这么久） | DeepSeek 页设置里的"说完后等待多久再识别"滑块（400~1500ms）。越小越快，但句中停顿可能被当成说完 |
| 上传+识别 | DeepSeek ASR 整句上传、不支持流式，耗时约 0.6~1.3 秒，**随音频长度增加** | 环境杂音/旁人说话会让一句话越拖越长（日志里"整句/有效语音/句内停顿杂音"可以看出），在"环境噪声"里选"嘈杂"可减少 |
| 模型首字 | 第一个回答字出现的时间。**思考模式会让它慢几秒**，所以语音对话默认关闭思考（可在设置里打开） | 保持"语音对话时思考"关闭 |
| 首段语音 | 第一句话送入 TTS 到出声。识别的同时已提前建立 TTS 连接，第一个短句在逗号处就送出 | — |

句尾判停用的是"有声阈值"（环境噪声 × 3，且不低于最低音量）的迟滞判断，噪声底取最近 3 秒里最安静的部分来估计，稳定的风扇/空调声会被自动学成背景，而说话（起伏明显）不会。

| 类型 | 位置 |
|---|---|
| ASR / 流式 TTS 客户端 | `DeepSeekSpeechClient`（`IDeepSeekSpeechClient`），含 429/503 重试与错误映射 |
| 端点检测 | `UtteranceDetector`：自适应噪声底 + 起始确认 + 迟滞，把麦克风流切成一句话 |
| 对话编排 | `DeepSeekVoiceChatService`：Listening → Transcribing → Thinking → Speaking |
| 后端协调 | `VoiceBackendCoordinator`：持久化选择、给唤醒词安装路由（`IVoiceChatService.KeywordInterceptor`）、互斥 |

> 另外，"朗读回复"开关（文字对话时朗读）仍使用 Windows 自带 TTS；语音对话中的朗读由 DeepSeek 语音合成负责，不会重复发声。
