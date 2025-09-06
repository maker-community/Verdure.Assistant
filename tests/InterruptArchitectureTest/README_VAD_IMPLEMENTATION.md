# InterruptArchitectureTest - 真实VAD实现

本项目基于 **SoundFlow.Samples.VoiceInterruption** 的VAD实现和 **SoundFlowRecordingCodecTest** 的录音编码实现，完善了InterruptArchitectureTest的语音活动检测功能。

## 📋 实现概述

### 参考项目分析

1. **SoundFlow.Samples.VoiceInterruption**
   - 使用 `VoiceActivityDetector` 进行实时人声检测
   - 配置参数：能量阈值、激活时间、保持时间、频带范围
   - 音频格式：48kHz, 2ch, F32 (DvdHq)
   - 事件驱动的VAD触发机制

2. **SoundFlowRecordingCodecTest**
   - 最优音频格式：16kHz, 1ch, S16
   - 帧大小：960 samples (60ms)
   - 转码路径：S16→F32→Int16→byte[]
   - 兼容 AudioStreamManager 和 OpusSharpAudioCodec

### 新实现特性

#### 🎤 RealVoiceActivityInterruptSource
- **真实VAD检测**：基于麦克风实时录音
- **优化音频格式**：采用16kHz/1ch/S16格式，减少转换开销
- **可配置参数**：支持动态调整VAD阈值和时间参数
- **调试支持**：提供详细的音频统计和状态信息

#### 🔧 InterruptAudioProcessor
- **音频转码**：F32→Int16→byte[]完整转换链
- **兼容性验证**：确保与现有编码器兼容
- **性能监控**：实时统计处理效率和音频质量
- **错误处理**：完善的异常处理和资源管理

## 🚀 功能特性

### VAD配置参数
```csharp
var vadConfig = new RealVoiceActivityInterruptSource.VadConfiguration
{
    EnergyThreshold = 0.001f,      // 能量阈值：0.001-0.1
    ActivationTimeMs = 100f,       // 激活确认时间：50-300ms
    HangoverTimeMs = 500f,         // 保持时间：200-1000ms
    SpeechLowBand = 200,           // 人声频带下限：100-300Hz
    SpeechHighBand = 4000,         // 人声频带上限：3000-8000Hz
    FftSize = 1024,                // FFT大小：512/1024/2048
    DebugOutput = true             // 调试输出开关
};
```

### 音频格式优化
```csharp
// 最优化配置 (参考SoundFlowRecordingCodecTest)
AudioFormat OptimalFormat = new()
{
    Format = SampleFormat.S16,     // 接近目标Int16格式
    Channels = 1,                  // 单声道减少处理开销
    SampleRate = 16000            // 标准语音采样率
};
```

### 设备配置优化
```csharp
MiniAudioDeviceConfig DeviceConfig = new()
{
    PeriodSizeInFrames = 960,      // 60ms帧 @ 16kHz
    Periods = 3,                   // 3个缓冲区
    Capture = new() { ShareMode = ShareMode.Shared },
    Wasapi = new() { Usage = WasapiUsage.ProAudio }
};
```

## 🎯 交互命令

### 基本操作
- `manual` - 触发手动打断
- `pause <source>` - 暂停指定打断源
- `resume <source>` - 恢复指定打断源
- `status` - 查看所有打断源状态

### VAD专用命令
- `vad` - 查看VAD详细状态和统计信息
- `adjust` - 动态调整VAD参数
- `F3` - 热键触发打断

### VAD状态信息
```
📡 真实VAD状态:
   状态: 运行中 (启用)
   配置: 阈值=0.001, 激活=100ms, 保持=500ms
   频带: 200Hz - 4000Hz, FFT=1024
   统计: 音频帧=1234, VAD触发=56
   当前: VAD状态=false, 录音=true
   时间: 最后音频=0.1s前, 最后VAD=5.2s前
```

## 📊 技术细节

### 音频处理流程
1. **音频捕获**：MiniAudio引擎 → S16格式录音
2. **实时转换**：S16 → F32 (SoundFlow内部)
3. **VAD分析**：FFT频谱分析 → 人声检测
4. **格式转换**：F32 → Int16 → byte[] (兼容现有系统)
5. **事件触发**：语音活动 → 打断事件

### 兼容性保证
- ✅ **AudioStreamManager**：帧大小和数据格式完全兼容
- ✅ **OpusSharpAudioCodec**：严格符合16kHz/1ch/60ms要求
- ✅ **现有打断架构**：无缝集成到InterruptService
- ✅ **资源管理**：自动清理音频设备和流

### 性能优化
- **最小转换开销**：S16→F32→Int16 (最短路径)
- **合适缓冲区**：960样本/帧，3个周期缓冲
- **共享音频模式**：减少独占资源占用
- **异步处理**：非阻塞的监控和处理循环

## 🛠️ 使用方法

### 快速启动
```powershell
# 运行演示脚本
.\demo-vad.ps1

# 或直接运行
dotnet run
```

### 集成到现有项目
```csharp
// 1. 创建VAD配置
var vadConfig = new RealVoiceActivityInterruptSource.VadConfiguration
{
    EnergyThreshold = 0.001f,
    ActivationTimeMs = 100f,
    HangoverTimeMs = 500f,
    DebugOutput = true
};

// 2. 创建VAD源
var vadSource = new RealVoiceActivityInterruptSource(vadConfig);

// 3. 注册到中断服务
interruptService.RegisterInterruptSource(vadSource);

// 4. 启动服务
await interruptService.StartAllAsync();
```

### 动态参数调整
```csharp
// 运行时调整VAD参数
vadSource.UpdateVadConfiguration(config => 
{
    config.EnergyThreshold = 0.005f;    // 提高阈值
    config.ActivationTimeMs = 150f;     // 增加确认时间
});
```

## 🔍 调试和监控

### 实时统计
```csharp
var stats = vadSource.GetStatistics();
Console.WriteLine($"音频帧: {stats.AudioFrameCount}");
Console.WriteLine($"VAD触发: {stats.VadTriggerCount}");
Console.WriteLine($"录音状态: {stats.IsRecording}");
```

### 音频质量监控
```csharp
var processor = new InterruptAudioProcessor();
var result = processor.ProcessFrame(audioSamples);

Console.WriteLine($"RMS: {result.AudioStatistics.RMS:F6}");
Console.WriteLine($"dB: {result.AudioStatistics.DB:F2}");
Console.WriteLine($"峰值: {result.AudioStatistics.Peak:F4}");
```

## 📦 依赖项

- `SoundFlow` (1.2.1) - 音频处理核心
- `SoundFlow.Extensions.WebRtc.Apm` (1.0.4) - VAD检测器
- `Microsoft.CognitiveServices.Speech` (1.45.0) - 语音服务支持
- `Microsoft.Extensions.*` - 依赖注入和日志

## 🎉 总结

本实现成功地将SoundFlow的强大音频处理能力与InterruptArchitectureTest的打断框架相结合，提供了：

1. **真实的语音活动检测** - 基于麦克风实时录音
2. **优化的音频处理** - 最小化转换开销，最大化兼容性
3. **灵活的参数配置** - 支持运行时动态调整
4. **完善的监控和调试** - 详细的统计信息和状态反馈
5. **无缝的系统集成** - 与现有编码器和打断架构完美配合

通过这个实现，开发者可以轻松地在任何需要语音打断功能的应用中集成高质量的VAD检测能力。
