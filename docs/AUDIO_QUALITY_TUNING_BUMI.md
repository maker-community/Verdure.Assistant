# Bumi 机器人扬声器音质调优记录（可复现）

本文把"从听不出来为什么难听"到"最终定稿的音色链"这一整轮工作记录下来，包含**结论参数**、
**判断依据的数字**、**复现步骤**和**验证标准**。目标很明确：换一台机器、重刷系统、或者过几个月
回来，只要照本文做，就能拿回同一个效果，不需要重新盲听一遍。

- 代码位置：[BumiAudioPlayer.cs](../src/Verdure.Assistant.Bumi/BumiAudioPlayer.cs)（播放）、
  [BumiToneShaper.cs](../src/Verdure.Assistant.Bumi/BumiToneShaper.cs)（音色链）、
  [BumiAudioOptions.cs](../src/Verdure.Assistant.Bumi/BumiAudioOptions.cs)（全部参数）
- 权威默认值：`BumiAudioOptions` 的字段默认值 + [appsettings.json](../src/Verdure.Assistant.Bumi/appsettings.json)
- 机器人侧（SDK / 硬件）的定量报告：`noetix_sdk_bumi_csharp` 仓库的 `docs/audio-quality.md` 第 3.4、4.7 节

---

## 1. 最终参数（照抄即可）

`appsettings.json` 的 `Bumi` 段：

```json
"Bumi": {
  "DdsConfigPath": "config/dds.xml",
  "Volume": 100,
  "OutputGainDb": 1,
  "HighPassHz": 150,
  "PresenceHz": 2500,
  "PresenceGainDb": 6,
  "LimiterCeilingDb": -3,
  "PlaybackFrameMs": 20,
  "MuteBuiltInAgent": true,
  "MuteAudioCue": true,
  "RestoreOnExit": true
}
```

系统服务里的环境变量（`deploy/verdure-assistant-bumi.service`）会再覆盖一次音量：

```
Environment=Bumi__DdsConfigPath=/home/noetix/noetix-csharp-arm64-build-20261118/official-sdk/config/dds.xml
Environment=Bumi__Volume=100
```

信号流：

```
服务端 Opus(16 kHz 单声道) → 解码 → 单声道 PCM
  → 高通 150 Hz (2 阶, RBJ)
  → 高架 2500 Hz +6 dB (RBJ high shelf)
  → ×(+1 dB)
  → 峰值限幅 −3 dBFS (释放 120 ms)
  → 上混立体声 → 20 ms 一帧按墙钟发布到 ExternalCustomAudioPlayback
```

**不要**改回历史参数：`OutputGainDb=-6`、`HighPassHz=250`、`PresenceGainDb=0` 是"更闷更轻"的旧配置。

---

## 2. 为什么是这几个数（判断依据）

### 2.1 问题不在播放路径，而在内容

把助手**真正推给扬声器的采样**落盘（`Bumi:DumpTtsDirectory`），再用 SDK 仓库的 `MediaTest playfile`
在**服务已停**的状态下独立播放同一个文件：

| 观察 | 含义 |
| --- | --- |
| 落盘文件独立播放 = 助手的现场听感 | 播放路径（节拍/开关/缓冲）没有额外损失 |
| 落盘波形无削波、相邻样本最大跳变 0.094 | 数字域干净，不是破音 |
| 落盘峰值 −17 dBFS（当时加了 −12 dB 增益） | 回推后内容峰值只有 **−5 dBFS**，凭空丢了 12 dB 余量 |

### 2.2 内容本身又轻又闷

服务端 TTS：Opus，193–266 字节 / 60 ms（≈26–35 kbps），16 kHz 单声道。频谱（相对总能量）：

| 频段 | 相对电平 |
| --- | --- |
| 300–1000 Hz | −1.8 dBc（主能量） |
| 1000–2000 Hz | −12.5 dBc |
| 2000–4000 Hz | **−17.3 dBc** |
| 6000–8000 Hz | −24.4 dBc |

人耳判断"清脆"的 2–4 kHz 比主能量低 16 dB，6 kHz 以上几乎为空 —— 这就是"发闷、不干净"的根因，
与扬声器无关。**所以正确做法是补电平 + 抬 2–4 kHz，而不是继续加音量。**

### 2.3 模块音量：60 干净，100 略脏但响得多

同一段音频、同一条通路，只改模块音量：

| 模块音量 | 主观 |
| --- | --- |
| 60 | 干净 |
| 100 | 略脏，但**明显更响** |
| >100 | 只增加脏，几乎不增加响度（60→180 声学仅 +4 dB） |

取 100：响度优先，脏的程度可接受。

### 2.4 A/B 盲听选出参数

用同一段真实 TTS 生成 4 个版本（生成脚本见第 5 节），在音量 100/80 下依次播放：

| 版本 | 处理 | 峰值 / RMS | 结论 |
| --- | --- | --- | --- |
| A | 原样（−17 dBFS） | −17.0 / −32.4 | 闷、轻 |
| B | 只补电平到 −3 dBFS | −3.0 / −18.4 | 响了但依旧闷 |
| C | 补电平 + 2.5 kHz **+5 dB** + 限幅 −3 | −5.4 / −21.2 | **明显好转** |
| D | 补电平 + 2.5 kHz **+6 dB** + 150 Hz 高通 + 限幅 −3 | −3.4 / −19.3 | **最佳（选定）** |
| E | 3 kHz **+8 dB** + 120 Hz 高通 + 限幅 −2 | −3.2 / −19.2 | 更响，但齿音开始刺 → 弃用 |

结论：**2.5 kHz / +6 dB 是"清脆"和"刺"之间的分界**，再加就过头；150 Hz 高通省掉喇叭发不出的低频。

---

## 3. 音乐/密集内容的保护（内容自适应）

语音调好之后，**音乐仍然有尖锐破音**。原因不是"音量大"这么简单：语音那条链（+6 dB @2.5 kHz、
+1 dB 增益、−3 dBFS 限幅）是按**稀疏内容**设计的，对 0 dBFS 的连续音乐不成立。

### 3.1 判据：密度，而不是电平

```
密度 = 最近 500 ms 内 |x| 高于「自身 1 s 峰值包络 −34 dB」的时间占比
```

| 内容 | 密度 |
| --- | --- |
| 语音 | 0.6 ~ 0.8（有停顿、有弱音） |
| 音乐 | 0.87 ~ 0.93（几乎不中断） |

阈值跟着峰值包络走，所以**与母带音量无关**。门限 0.85 → 0.94 之间线性过渡，混合系数再平方，
语音侧基本不触发。

### 3.2 密集时做的三件事

| 环节 | 语音 | 密集内容 |
| --- | --- | --- |
| 2.5 kHz 高架 +6 dB | 100% | **保留 40%**（+2.4 dB） |
| 高通 | 150 Hz | **300 Hz**（这台喇叭连 500 Hz 以下都发不出来，留着只换失真） |
| 低通 | 无 | **6.5 kHz**（"尖锐"就在这段） |
| 额外电平 | 0 | 0（响度靠前两项平衡，不再额外让） |

实测（真实 C# 音色链 + 真实素材）：语音 RMS 变化 **−0.1 dB**、逐样本最大差 4.5%；
音乐 RMS −3.7 dB（几乎全是喇叭发不出的低频与过度的高频提升）。用户验收：**"不刺了，也不重了"**。

### 3.3 踩过的坑（务必记住）

1. **判据必须在高通之前算**。第一版用"绝对电平 > −34 dBFS"并且放在 150 Hz 高通**之后**，
   而音乐的能量主要在 100 Hz 以下 —— 高通正好把能触发判据的部分削掉了，保护**从未启动**，
   离线回放显示输出与改动前逐样本相同（差异 0.000000）。这就是"改了却没用"的原因。
2. **不能把存在感提升全额收掉**。全收回会让音乐相对变"重"（高频少 6 dB，低频显突出），
   听感是"闷"而不是"干净"。保留 40% 才平衡。
3. **用峰均比（crest）区分语音/音乐在这个素材上不成立**：两者重叠严重（见下表），
   所以最终选的是密度而不是 crest。

| 判据 | 语音 | 音乐 | 可用性 |
| --- | --- | --- | --- |
| 绝对电平（高通后） | — | — | ✗ 完全失效 |
| 峰均比 p50 | 11.6 dB | 10.6 dB | ✗ 重叠 |
| 密度（相对峰值） | 0.72 | 0.91 | ✓ |

## 4. 复现步骤

### 4.1 装到机器人上

```bash
# 交叉编译（Windows / Linux 均可）
dotnet publish src/Verdure.Assistant.Bumi/Verdure.Assistant.Bumi.csproj \
  -c Release -r linux-arm64 --self-contained true -o build/bumi-arm64

tar -cf build/bumi-arm64.tar -C build/bumi-arm64 .
scp -P 2222 build/bumi-arm64.tar noetix@<robot>:/tmp/

# 机器人上（注意：tar 会丢掉可执行位）
ssh -p 2222 noetix@<robot>
cd ~/bumi-api && tar -xf /tmp/bumi-arm64.tar && chmod +x Verdure.Assistant.Bumi
```

`appsettings.json` 会被发布产物覆盖，**装完记得确认 `Bumi` 段仍是本文第 1 节的数值**
（或者把机器人上那份先备份再还原）。

### 4.2 验证数字（不用耳朵也能判断）

打开落盘：

```json
"Bumi": { "DumpTtsDirectory": "/home/noetix/tts-dump" }
```

正常对话一次，然后：

```bash
scp -P 2222 noetix@<robot>:/home/noetix/tts-dump/tts-*.wav ./tts-live.wav
python tools/audio/analyze_wav.py "live" tts-live.wav
```

**合格标准**（实测值，偏差 1 dB 以内即重现成功）：

| 指标 | 期望 |
| --- | --- |
| 峰值 | ≈ **−3.0 dBFS** |
| RMS | ≈ **−20 dBFS** |
| 削波样本 | **0** |
| 300–1000 Hz | ≈ −1.8 dBc |
| 2000–4000 Hz | ≈ **−13 dBc**（旧配置是 −17.3） |

落盘文件可以直接用 SDK 仓库的 `MediaTest playfile` 播回去，听感应与助手现场一致。

> 验证完请把 `DumpTtsDirectory` 清空：它会把每一句 TTS 都写进磁盘，长期开着会一直占空间。

### 4.3 想再调参时的顺序

1. 先只改 `PresenceGainDb`（±2 dB 一档），别同时动别的；
2. 再改 `PresenceHz`（2000 / 2500 / 3000）；
3. 电平用 `OutputGainDb`，但**保持 `LimiterCeilingDb=-3`**，否则高架后的瞬态会硬削波（沙沙声）；
4. `HighPassHz` 150 已足够；四阶（`HighPassStages=2`）在人声上会变薄，实测负优化；
5. 每改一次都用 3.2 的数字复核 —— 耳朵会说谎，数字不会。

---

## 5. 已知边界（不要再往这些方向试）

| 尝试 | 结果 |
| --- | --- |
| 提高模块音量换响度 | 60→180 声学仅 +4 dB，功放已在压缩，只会更脏 |
| 四阶高通（300 Hz） | 人声变薄，负优化 |
| 3 kHz +8 dB | 音量 100 时齿音刺 |
| 逐样本瞬时限幅（无释放） | 瞬态被压平，听感更差 |
| 换采样率（44.1 kHz） | 模块内部重采样，比 16 kHz 更差 |
| 改前导缓冲 / 帧长 / 灌满策略 | 与音质无关（只影响断续，且当前已无断续） |
| 通过关闭"麦克风→内置智能体"路由来独占扬声器 | 会同时杀掉唤醒与播放，且无法恢复（详见 SDK 仓库 `docs/audio-quality.md`） |

---

## 6. 参考实现（离线做 A/B 用）

完整的分析与变体生成脚本：

- [tools/audio/analyze_wav.py](../tools/audio/analyze_wav.py)：峰值/RMS/削波/相邻跳变/分频段能量
- [tools/audio/make_variants.py](../tools/audio/make_variants.py)：高通、高架、限幅的参考实现，批量生成 A/B 版本

两者都只依赖 `numpy`：

```bash
pip install numpy
python tools/audio/make_variants.py tts-live.wav          # 生成 -B-level/-C-bright/-D-bright-hp/-E-loud-bright
python tools/audio/analyze_wav.py "D" tts-live-D-bright-hp.wav
```

把生成的版本 scp 到机器人，用 `MediaTest playfile` 依次播放即可复现第 2.4 节的听感对比：

```bash
cd ~/board-media
export LD_LIBRARY_PATH=/path/to/native/build:$LD_LIBRARY_PATH
for f in tts-live-D-bright-hp tts-live-C-bright tts-live-E-loud-bright tts-live; do
  dotnet MediaTest.dll /path/to/config/dds.xml playfile ~/$f.wav /tmp/out
  rm -rf /tmp/out/*
  sleep 3
done
```

---

## 7. 相关文档

| 文档 | 内容 |
| --- | --- |
| `noetix_sdk_bumi_csharp/docs/audio-quality.md` | 机器人扬声器/功放的定量测量（音量扫描、低频失真、缺口统计、TTS 频谱） |
| `noetix_sdk_bumi_csharp/docs/voice-assistant.md` | 自建语音助手方案（唤醒词、路由、音量） |
| `noetix_sdk_bumi_csharp/docs/verification.md` | 哪些已验证、哪些没有 |
| [CONSOLE_AUDIO_QUALITY_FIX.md](CONSOLE_AUDIO_QUALITY_FIX.md) | 桌面端（本机声卡）的音质问题，与机器人无关 |