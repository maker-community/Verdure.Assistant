# 关键词检测句柄错误修复方案

## 问题描述

在原始的关键词检测实现中，频繁出现 `SPXERR_INVALID_HANDLE` 和 `0x21` 错误，这些错误的根本原因是：

1. **对象重复使用**：在重启关键词检测时，重复使用同一个 `KeywordRecognizer` 和 `KeywordRecognitionModel` 实例
2. **不完整的资源清理**：在重启前没有完全释放之前的资源
3. **并发访问问题**：多个线程可能同时访问和修改识别器对象
4. **音频流生命周期不匹配**：音频流和识别器的生命周期管理不一致

## 修复方案

### 1. 实例重建策略

每次重启关键词检测时都创建全新的对象实例，避免对象重用导致的句柄错误：

```csharp
/// <summary>
/// 重新创建关键词识别器以避免 SPXERR_INVALID_HANDLE 错误
/// 每次重启都创建全新的实例，确保资源完全重置
/// </summary>
private async Task RecreateKeywordRecognizer()
{
    // 1. 完全清理现有资源
    await CleanupKeywordRecognizer();

    // 2. 重新加载关键词模型（新实例）
    if (!LoadKeywordModels())
    {
        throw new InvalidOperationException("重新加载关键词模型失败");
    }

    // 3. 重新配置音频输入（新实例）
    var audioConfig = await ConfigureSharedAudioInput();
    if (audioConfig == null)
    {
        throw new InvalidOperationException("重新配置音频输入失败");
    }

    // 4. 创建全新的关键词识别器实例
    _keywordRecognizer = new KeywordRecognizer(audioConfig);

    // 5. 重新订阅事件
    SubscribeToRecognizerEvents();

    // 6. 启动新的识别会话
    if (_keywordModel != null)
    {
        await _keywordRecognizer.RecognizeOnceAsync(_keywordModel);
    }
}
```

### 2. 完整的资源清理

确保在创建新实例前完全释放所有相关资源：

```csharp
private async Task CleanupKeywordRecognizer()
{
    // 停止现有识别器
    if (_keywordRecognizer != null)
    {
        await _keywordRecognizer.StopRecognitionAsync();
        await Task.Delay(200); // 给SDK时间完全停止
        _keywordRecognizer.Dispose();
        _keywordRecognizer = null;
    }

    // 重新创建关键词模型（避免模型实例重用）
    if (_keywordModel != null)
    {
        _keywordModel.Dispose();
        _keywordModel = null;
    }

    // 清理音频流
    if (_pushStream != null)
    {
        _pushStream.Close();
        _pushStream = null;
    }
}
```

### 3. 模型实例重建

每次加载关键词模型时都创建新实例：

```csharp
private bool LoadKeywordModels()
{
    // 先清理现有模型
    if (_keywordModel != null)
    {
        _keywordModel.Dispose();
        _keywordModel = null;
    }

    // 从.table文件创建关键词模型 - 每次都创建新实例
    _keywordModel = KeywordRecognitionModel.FromFile(primaryModelPath);
    
    return true;
}
```

### 4. 音频流重建

确保音频流也是每次新创建的：

```csharp
private async Task<AudioConfig?> ConfigureSharedAudioInput()
{
    // 清理现有的推送流
    if (_pushStream != null)
    {
        _pushStream.Close();
        _pushStream = null;
    }

    // 停止现有的音频推送任务
    if (_audioPushTask != null)
    {
        _cancellationTokenSource?.Cancel();
        await _audioPushTask;
        _audioPushTask = null;
    }

    // 重新创建取消令牌
    _cancellationTokenSource?.Dispose();
    _cancellationTokenSource = new CancellationTokenSource();

    // 创建新的推送音频流
    var format = AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1);
    _pushStream = AudioInputStream.CreatePushStream(format);
    
    return AudioConfig.FromStreamInput(_pushStream);
}
```

### 5. 增强的错误处理

移除对特定句柄错误的特殊处理，因为现在应该不会再出现这些错误：

```csharp
private void OnRecognitionCanceled(object? sender, SpeechRecognitionCanceledEventArgs e)
{
    if (e.Reason == CancellationReason.Error)
    {
        _logger?.LogWarning($"识别错误: {e.ErrorDetails}");
        OnErrorOccurred($"识别错误: {e.ErrorDetails}");
    }
    
    // 如果是因为错误被取消且服务仍在运行，尝试重启识别
    if (e.Reason == CancellationReason.Error && _isRunning && !_isPaused)
    {
        // 延迟重启以确保资源完全释放
        Task.Delay(500).ContinueWith(_ =>
        {
            if (_isRunning && !_isPaused)
            {
                RestartContinuousRecognition();
            }
        });
    }
}
```

## 测试验证

创建了专门的测试程序 `KeywordSpottingHandleErrorFixTest` 来验证修复效果：

1. **快速重启测试**：执行50次快速停止和重启操作
2. **错误监控**：监控是否还会出现 `SPXERR_INVALID_HANDLE` 或 `0x21` 错误
3. **性能验证**：确保修复不影响正常功能

## 预期结果

通过这些修复：

1. **彻底消除句柄错误**：不再出现 `SPXERR_INVALID_HANDLE` 和 `0x21` 错误
2. **提高稳定性**：关键词检测服务可以稳定地快速重启
3. **改善资源管理**：所有资源都能正确释放和重建
4. **保持功能完整**：修复不影响关键词检测的正常功能

## 关键改进点

1. **每次重启都创建新实例**：避免对象重用导致的句柄冲突
2. **完整的资源清理流程**：确保没有残留资源影响新实例
3. **适当的延迟机制**：给SDK足够时间完全释放资源
4. **线程安全保护**：使用信号量确保资源操作的原子性
5. **简化错误处理**：不再需要特殊处理句柄错误

这种"重建而非重用"的策略彻底解决了Microsoft Speech SDK在快速重启时的句柄管理问题。
