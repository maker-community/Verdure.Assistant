# Verdure.Assistant.Bumi

把 绿荫助手 的语音对话能力做成一个 **HTTP 服务**，跑在布米（Bumi）机器人本机上，音频设备直接用机器人的麦克风和喇叭，不经过运行程序的电脑声卡。外部程序（桌面端、网页、剧本脚本、定时任务）通过 RESTful 接口触发它说话。

和 [Verdure.Assistant.Api](../Verdure.Assistant.Api/README.md) 的关系：同样是"用 HTTP 控制助手"，区别只在于音频的去处。

| | Verdure.Assistant.Api | Verdure.Assistant.Bumi |
|---|---|---|
| 音频设备 | 运行程序的机器声卡（SoundFlow） | 布米机器人的麦克风/扬声器 |
| 机器人相关服务 | 显示屏、WiFi 配网、表情动作 | 无 |
| 典型部署 | 树莓派 / PC | 机器人本体（aarch64） |

## 前置条件

1. **官方 SDK 的原生库**：`libnoetix_bumi_c.so`，需要按
   [noetix_sdk_bumi_csharp](https://github.com/maker-community/noetix_sdk_bumi_csharp) 的说明在机器人上用 GCC 11 编译。
   本仓库不分发这个库。
2. **`config/dds.xml`**：取自官方 SDK 的 `official-sdk/config/dds.xml`。
3. 机器人已开机、DDS 可达（默认对端 `192.168.55.102`）。

## 本机（电脑）能做什么、不能做什么

原生库是 aarch64 Linux 的，所以在 Windows / x64 上：

- ✅ 服务能正常启动，`GET /` 与 `GET /api/voicechat/status` 都可用，方便调接口和前端。
- ❌ 任何真正碰音频的调用（`initialize` / `start`）会返回
  `500 ... Unable to load DLL 'noetix_bumi_c'`，这是预期行为，不是配置写错。

要听到声音必须在机器人上跑。

## 本地运行

```bash
dotnet run --project src/Verdure.Assistant.Bumi
```

默认监听 `http://0.0.0.0:5031`（见 `appsettings.json` 的 `Urls`）。

## API

| 方法 | 路径 | 说明 |
|---|---|---|
| GET | `/` | 服务自述与接口清单 |
| GET | `/api/voicechat/status` | 连接状态、当前状态机状态、聆听模式 |
| POST | `/api/voicechat/initialize` | 连上服务端（进程启动时若 `AutoStartVoiceChat` 为真已自动做过） |
| POST | `/api/voicechat/start` | 开始一轮语音对话 |
| POST | `/api/voicechat/stop` | 停止本轮语音对话 |
| POST | `/api/voicechat/toggle` | 在开始/停止之间切换 |
| POST | `/api/voicechat/send-text` | 让助手直接说一段文本，不动麦克风 |
| POST | `/api/voicechat/interrupt` | 打断当前说话 |
| POST | `/api/voicechat/auto-mode` | 开关自动对话模式 |
| POST | `/api/voicechat/disconnect` | 断开与服务端的连接 |

示例：

```bash
# 看一眼状态
curl http://192.168.55.1:5031/api/voicechat/status

# 让机器人说话（不经过麦克风）
curl -X POST http://192.168.55.1:5031/api/voicechat/send-text \
     -H "Content-Type: application/json" \
     -d '{"message":"用一句话介绍你自己"}'

# 开始收音对话，说完停下
curl -X POST http://192.168.55.1:5031/api/voicechat/start
curl -X POST http://192.168.55.1:5031/api/voicechat/stop
```

## 配置

`appsettings.json` 中与机器人相关的部分：

```json
{
  "Urls": "http://0.0.0.0:5031",
  "AutoStartVoiceChat": true,
  "AutoConversationMode": true,
  "Bumi": {
    "DdsConfigPath": "config/dds.xml",
    "Volume": 120,
    "OutputGainDb": -6,
    "HighPassHz": 250,
    "PlaybackFrameMs": 20,
    "MuteBuiltInAgent": true,
    "MuteAudioCue": true,
    "RestoreOnExit": true
  }
}
```

- `AutoConversationMode`：`true` 时 `listen` 下发 `mode: "auto"`，由服务端判断一句话说完。
  机器人没有"松开按钮结束说话"这个动作，设成 `false` 会让会话一直停在聆听状态，服务端最终关闭连接。
- `MuteBuiltInAgent` / `MuteAudioCue`：只关内置智能体的**输出**和提示音，**不动**麦克风到智能体的输入通道。
  关掉那个输入会连扬声器一起停掉且无法恢复（路线 A 的实测结论，见
  [voice-assistant.md](https://github.com/maker-community/noetix_sdk_bumi_csharp/blob/main/docs/voice-assistant.md)）。
- `RestoreOnExit`：进程退出时把改过的开关恢复原值，**建议保持开启**，否则机器人可能停在静音状态。
- `Urls` / 监听地址见上文；`Volume`（0–200）、`OutputGainDb`、`HighPassHz`：真机实测结果，2 声道 / 16 kHz 且
  250 Hz 高通 + −6 dB 时机身小喇叭最干净；音量 120 是折中值。
- `InputGainDb`：采集增益，默认 +18 dB。机器人头顶麦克风远场电平很低（1–2 m 约 −34 dBFS），
  不加增益服务端识别不出来。近距离大声说话时峰值会接近满幅，必要时调到 +12 dB。

## 部署到机器人

```bash
# 在开发机上打包（需要事先把 Noetix.Sdk.Bumi 放进 local-nuget/）
# 必须自包含：算力板通常只装了 Microsoft.NETCore.App，没有 ASP.NET Core 运行时。
dotnet publish src/Verdure.Assistant.Bumi -c Release -r linux-arm64 --self-contained true -o out/bumi-api

# 拷到机器人
scp -r out/bumi-api noetix@<robot>:/home/noetix/bumi-api
```

机器人上还需要补两样发布产物里没有的东西：

```bash
cd ~/bumi-api
mkdir -p config
cp /path/to/noetix_sdk_bumi/official-sdk/config/dds.xml config/   # 或改 DdsConfigPath 指向绝对路径
export LD_LIBRARY_PATH=/path/to/noetix_sdk_bumi_csharp/native/build:$LD_LIBRARY_PATH
dotnet Verdure.Assistant.Bumi.dll
```

systemd 单元示例：

```ini
[Unit]
Description=Verdure Assistant (Bumi voice API)
After=network-online.target

[Service]
WorkingDirectory=/home/noetix/bumi-api
Environment=LD_LIBRARY_PATH=/home/noetix/native/build
ExecStart=/usr/bin/dotnet /home/noetix/bumi-api/Verdure.Assistant.Bumi.dll
Restart=on-failure

[Install]
WantedBy=multi-user.target
```

## 说明

- **上行必须严格 60 ms 一帧**。机器人 DDS 回调每次只给 160 样本（10 ms），而 Opus 编码器要求 960 样本，
  长度不符时它会补零或截断，结果是"10 ms 语音 + 50 ms 静音"的碎片，服务端完全识别不出来。
  `BumiAudioRecorder` 因此先缓冲、凑满整帧再交出去。
- **播放要先攒 300 ms 前导缓冲**。TTS 帧是网络突发到达的，边到边放会被抖动切成断续；
  时间戳也必须跨句单调递增，否则原生播放侧会以为是回退。
- **不会启用本地关键词唤醒**（`Microsoft.CognitiveServices.Speech` 的 KWS）。机器人自带唤醒词
  "你好布米" 仍在工作，本服务可以只做对话与播放，见 `docs/voice-assistant.md` 的路线 A。
  因此发布产物里不包含 `ModelFiles/*.table`（启动时会报"找不到关键词模型文件"，属预期）。
- 服务端返回的 `tts` 音频按 16 kHz 解码播放，即使它声明 `sample_rate: 24000`——Opus 自带采样率，
  不需要重采样。
- `send-text` 依赖官方协议的 `listen state=detect`，服务端目前只把它当唤醒词上报，
  对任意文本不一定有回应；要稳定"让机器人说指定的话"需要本地 TTS。
- 本项目只是替换音频设备，语音协议、状态机、打断逻辑都在 `Verdure.Assistant.Core`。
