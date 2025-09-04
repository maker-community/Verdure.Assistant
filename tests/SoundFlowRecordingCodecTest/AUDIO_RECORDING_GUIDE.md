# SoundFlow 音频录制与文件保存测试

## 功能概述

本测试项目现在支持**将录制的音频保存为WAV文件**，让您可以测试Opus处理前的原始音频质量。

## 音频保存功能

### 保存的音频格式
- **文件格式**: WAV (PCM)
- **采样率**: 16kHz
- **通道数**: 1 (单声道)
- **位深度**: 16-bit
- **文件名**: `recorded_audio.wav`

### 数据流程
```
麦克风输入 → SoundFlow (F32) → 转换为Int16 → 保存WAV文件 → Opus编码测试
```

## 使用方法

1. **运行测试程序**:
   ```powershell
   dotnet run
   ```

2. **开始录音**:
   - 程序启动后会自动初始化音频文件
   - 看到"🎤 请说话测试录音转码功能..."提示后开始说话
   - 实时统计信息会显示录音状态

3. **停止录音**:
   - 按任意键停止录音
   - 程序会自动完成WAV文件写入并显示统计信息

4. **测试音频文件**:
   - 在项目目录中会生成 `recorded_audio.wav` 文件
   - 可以用任何音频播放器播放测试音质
   - 文件包含Opus处理前的原始音频数据

## 输出信息示例

```
=== SoundFlow录音转码测试 ===
目标格式: S16, 1ch, 16kHz (960 samples/frame)
测试目标: 验证最优参数配置的转码性能
音频保存: recorded_audio.wav (WAV格式)

1. 初始化音频引擎和设备...
✅ 音频文件已准备: recorded_audio.wav
✅ 音频系统初始化成功

💾 音频文件保存完成:
   文件: recorded_audio.wav
   格式: 16kHz, 1ch, 16-bit PCM WAV
   时长: 15.2 秒
   大小: 487.3 KB
   采样点: 243,840

✅ 音频文件保存: recorded_audio.wav (243840 samples)
```

## 音质测试建议

### 1. 基本质量检查
- **播放测试**: 使用媒体播放器播放WAV文件
- **清晰度**: 检查语音是否清晰，无明显失真
- **噪声**: 观察背景噪声水平是否合理

### 2. 技术质量验证
- **采样率**: 确认为16kHz (适合语音)
- **动态范围**: 检查音频信号幅度是否充分利用16-bit范围
- **连续性**: 验证录音无断续或卡顿

### 3. 与目标兼容性
- **帧大小**: 每60ms一帧，960 samples
- **格式**: Int16 PCM格式，直接兼容OpusSharp
- **延迟**: 实时录音，满足语音助手需求

## 文件位置

录制的音频文件保存在项目目录中：
```
C:\github\Verdure.Assistant\tests\SoundFlowRecordingCodecTest\recorded_audio.wav
```

## 技术细节

### WAV文件结构
```
RIFF Header (12 bytes)
├─ RIFF chunk ID: "RIFF"
├─ Chunk size: file_size - 8
└─ Format: "WAVE"

fmt Subchunk (24 bytes)
├─ Subchunk ID: "fmt "
├─ Subchunk size: 16
├─ Audio format: 1 (PCM)
├─ Channels: 1
├─ Sample rate: 16000
├─ Byte rate: 32000
├─ Block align: 2
└─ Bits per sample: 16

data Subchunk (8 + audio_data bytes)
├─ Subchunk ID: "data"
├─ Subchunk size: audio_data_size
└─ Audio data: Int16 PCM samples
```

### 数据转换过程
1. **SoundFlow输出**: F32格式 [-1.0, 1.0]
2. **范围限制**: Math.Clamp确保有效范围
3. **格式转换**: F32 → Int16 (乘以32767)
4. **文件写入**: 小端序Int16数据
5. **头更新**: 录音结束后更新WAV文件头

这样您就可以方便地测试SoundFlow录制的音频质量，确保在Opus编码前的原始数据是高质量的。
