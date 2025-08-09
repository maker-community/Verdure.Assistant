using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Constants;
using Verdure.Assistant.Core.Interfaces;

namespace Verdure.Assistant.Core.Services;

/// <summary>
/// 基于Microsoft认知服务的关键词唤醒服务
/// 参考py-xiaozhi的WakeWordDetector实现模式，使用Microsoft.CognitiveServices.Speech进行离线关键词检测
/// 支持使用.table模型文件进行离线关键词识别，无需订阅密钥
/// </summary>
public class KeywordSpottingService : IKeywordSpottingService
{    private readonly ILogger<KeywordSpottingService>? _logger;
    private readonly IVoiceChatService _voiceChatService;
    private readonly AudioStreamManager _audioStreamManager;

    // Microsoft认知服务相关
    private SpeechConfig? _speechConfig;
    private KeywordRecognizer? _keywordRecognizer;
    private KeywordRecognitionModel? _keywordModel;

    // 关键词模型配置
    private readonly string[] _keywordModels = {
        "keyword_xiaodian.table",  // 对应py-xiaozhi的"你好小天"等
        "keyword_cortana.table"    // 对应"Cortana"关键词
    };    
    // 状态管理
    private bool _isRunning = false;
    private bool _isPaused = false;
    private bool _isEnabled = true;
    private DeviceState _lastDeviceState = DeviceState.Idle;    // 音频处理
    private IAudioRecorder? _audioRecorder;
    private bool _useExternalAudioSource = false;
    private PushAudioInputStream? _pushStream;
    private Task? _audioPushTask;
    private EventHandler<byte[]>? _audioDataHandler;    // 线程安全
    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
    private CancellationTokenSource? _cancellationTokenSource;
    
    // 状态同步 - 防止关键词检测和语音对话状态变化的竞争条件
    private readonly SemaphoreSlim _stateChangeSemaphore = new SemaphoreSlim(1, 1);
    private volatile bool _isProcessingKeywordDetection = false;

    // 事件
    public event EventHandler<KeywordDetectedEventArgs>? KeywordDetected;
    public event EventHandler<string>? ErrorOccurred;

    public bool IsRunning => _isRunning && !_isPaused;
    public bool IsPaused => _isPaused;
    public bool IsEnabled => _isEnabled;    
    public KeywordSpottingService(IVoiceChatService voiceChatService, AudioStreamManager audioStreamManager, ILogger<KeywordSpottingService>? logger = null)
    {
        _voiceChatService = voiceChatService;
        _audioStreamManager = audioStreamManager;
        _logger = logger;

        // 订阅设备状态变化，实现py-xiaozhi的状态协调逻辑
        _voiceChatService.DeviceStateChanged += OnDeviceStateChanged;

        InitializeSpeechConfig();
    }

    /// <summary>
    /// 初始化语音配置（离线模式，无需订阅密钥）
    /// </summary>
    private void InitializeSpeechConfig()
    {
        try
        {
            // 创建离线语音配置
            // 对于关键词检测，可以使用空的配置，因为我们使用本地.table文件
            _speechConfig = SpeechConfig.FromSubscription("dummy", "dummy");

            // 设置为离线模式
            _speechConfig.SetProperty("SPEECH-UseOfflineRecognition", "true");

            _logger?.LogInformation("语音配置初始化成功（离线模式）");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "初始化语音配置失败");
            _isEnabled = false;
        }
    }

    /// <summary>
    /// 启动关键词检测（对应py-xiaozhi的start方法）
    /// </summary>
    public async Task<bool> StartAsync(IAudioRecorder? audioRecorder = null)
    {
        if (!_isEnabled)
        {
            _logger?.LogWarning("关键词检测功能未启用");
            return false;
        }

        if (_isRunning)
        {
            _logger?.LogWarning("关键词检测已在运行");
            return true;
        }

        try
        {
            await _semaphore.WaitAsync();

            _cancellationTokenSource = new CancellationTokenSource();

            // 设置音频源（对应py-xiaozhi的多种启动模式）
            if (audioRecorder != null)
            {
                _audioRecorder = audioRecorder;
                _useExternalAudioSource = true;
                _logger?.LogInformation("使用外部音频源启动关键词检测");
            }
            else
            {
                _useExternalAudioSource = false;
                _logger?.LogInformation("使用独立音频模式启动关键词检测");
            }

            // 加载关键词模型
            if (!LoadKeywordModels())
            {
                _logger?.LogError("加载关键词模型失败");
                return false;
            }

            // 配置音频输入 - 使用共享音频流管理器
            var audioConfig = await ConfigureSharedAudioInput();
            if (audioConfig == null)
            {
                _logger?.LogError("配置音频输入失败");
                return false;
            }

            // 创建关键词识别器 - 确保每次启动都是全新实例
            _keywordRecognizer = new KeywordRecognizer(audioConfig);

            // 订阅事件
            SubscribeToRecognizerEvents();

            // 开始关键词识别
            await _keywordRecognizer.RecognizeOnceAsync(_keywordModel);
            _logger?.LogInformation("关键词识别已启动");

            _isRunning = true;
            _isPaused = false;

            _logger?.LogInformation("关键词检测启动成功");
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "启动关键词检测失败");
            return false;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// 加载关键词模型（使用Assets目录中的.table文件）
    /// 每次调用都创建新的模型实例以避免句柄错误
    /// </summary>
    private bool LoadKeywordModels()
    {
        try
        {
            // 先清理现有模型
            if (_keywordModel != null)
            {
                try
                {
                    _keywordModel.Dispose();
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "清理现有关键词模型时出现警告（这是正常的）");
                }
                finally
                {
                    _keywordModel = null;
                }
            }

            // 获取Assets目录路径
            var assetsPath = GetAssetsPath();
            var keywordsPath = Path.Combine(assetsPath, "keywords");

            if (!Directory.Exists(keywordsPath))
            {
                _logger?.LogError($"关键词模型目录不存在: {keywordsPath}");
                return false;
            }

            // 优先使用xiaodian模型（对应py-xiaozhi的主要唤醒词）
            var primaryModelPath = Path.Combine(keywordsPath, "keyword_xiaodian.table");

            if (!File.Exists(primaryModelPath))
            {
                _logger?.LogError($"主要关键词模型文件不存在: {primaryModelPath}");
                return false;
            }

            // 从.table文件创建关键词模型 - 每次都创建新实例
            _keywordModel = KeywordRecognitionModel.FromFile(primaryModelPath);

            _logger?.LogInformation($"成功加载关键词模型: {primaryModelPath}");
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "加载关键词模型失败");
            return false;
        }
    }

    /// <summary>
    /// 获取Assets目录路径
    /// </summary>
    private string GetAssetsPath()
    {
        // 从当前程序集位置推断Assets路径
        var assemblyPath = AppDomain.CurrentDomain.BaseDirectory;

        // 向上查找到解决方案根目录，然后定位到WinUI项目的Assets
        var currentDir = new DirectoryInfo(assemblyPath);
        while (currentDir != null && !File.Exists(Path.Combine(currentDir.FullName, "Verdure.Assistant.sln")))
        {
            currentDir = currentDir.Parent;
        }

        if (currentDir != null)
        {
            return Path.Combine(currentDir.FullName, "src", "Verdure.Assistant.WinUI", "Assets");
        }

        // 如果找不到解决方案目录，使用相对路径
        return Path.Combine(assemblyPath, "..", "..", "..", "..", "Verdure.Assistant.WinUI", "Assets");
    }

    /// <summary>
    /// 从共享音频流推送数据到语音服务（参考 py-xiaozhi 的 AudioCodec 集成模式）
    /// </summary>    
    private async Task PushSharedAudioDataAsync(AudioStreamManager audioStreamManager, CancellationToken cancellationToken)
    {
        if (_pushStream == null)
            return;

        try
        {
            // 确保清理之前的订阅
            if (_audioDataHandler != null)
            {
                audioStreamManager.UnsubscribeFromAudioData(_audioDataHandler);
                _audioDataHandler = null;
            }

            _audioDataHandler = (sender, audioData) =>
            {                // 检查暂停状态和取消令牌
                if (!cancellationToken.IsCancellationRequested && _pushStream != null && !_isPaused)
                {
                    try
                    {
                        // 将音频数据推送到语音识别服务
                        _pushStream.Write(audioData);
                    }
                    catch (ObjectDisposedException)
                    {
                        // 推送流已被释放，停止处理
                        _logger?.LogDebug("推送流已释放，停止音频数据处理");
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "写入音频数据到推送流时出错");
                        
                        // 在严重错误时触发错误事件
                        if (ex is InvalidOperationException || ex is ArgumentException)
                        {
                            OnErrorOccurred($"音频流错误: {ex.Message}");
                        }
                    }
                }
            };

            // 订阅共享音频流数据
            audioStreamManager.SubscribeToAudioData(_audioDataHandler);
            _logger?.LogInformation("已订阅共享音频流数据，开始推送到关键词识别器");

            // 保持推送直到取消
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 正常的取消操作
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "推送音频数据时发生错误");
            OnErrorOccurred($"音频数据推送错误: {ex.Message}");
        }
        finally
        {
            // 清理订阅
            if (_audioDataHandler != null)
            {
                audioStreamManager.UnsubscribeFromAudioData(_audioDataHandler);
                _logger?.LogInformation("已取消订阅共享音频流数据");
                _audioDataHandler = null;
            }
        }
    }

    /// <summary>
    /// 订阅识别器事件
    /// </summary>
    private void SubscribeToRecognizerEvents()
    {
        if (_keywordRecognizer == null) return;

        _keywordRecognizer.Recognized += (s, e) => OnKeywordRecognized(s, e);
        _keywordRecognizer.Canceled += (s, e) => OnRecognitionCanceled(s, e);
    }    
    
    /// <summary>
    /// 关键词识别事件处理
    /// </summary>
    private void OnKeywordRecognized(object? sender, KeywordRecognitionEventArgs e)
    {
        try
        {
            if (e.Result.Reason == ResultReason.RecognizedKeyword)
            {
                var keyword = e.Result.Text;
                _logger?.LogInformation($"检测到关键词: {keyword}");

                // 触发关键词检测事件（对应py-xiaozhi的_trigger_callbacks）
                var eventArgs = new KeywordDetectedEventArgs
                {
                    Keyword = keyword,
                    FullText = keyword,
                    Confidence = 1.0f, // Microsoft认知服务不提供详细置信度
                    ModelName = "Microsoft Speech Services"
                };

                KeywordDetected?.Invoke(this, eventArgs);

                // 实现py-xiaozhi的状态协调逻辑
                //HandleKeywordDetection(keyword);

                // 关键：重新启动关键词识别以实现连续检测
                // KeywordRecognizer的RecognizeOnceAsync检测到关键词后会停止，需要手动重启
                //RestartContinuousRecognition();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理关键词识别事件时发生错误");
            OnErrorOccurred($"关键词识别处理错误: {ex.Message}");
        }
    }
      /// <summary>
    /// 处理关键词检测（实现py-xiaozhi的状态协调逻辑）
    /// 只负责状态管理和暂停/恢复逻辑，不直接调用业务操作
    /// </summary>
    private void HandleKeywordDetection(string keyword)
    {
        Task.Run(async () =>
        {
            // 防止并发处理关键词检测事件
            if (_isProcessingKeywordDetection)
            {
                _logger?.LogDebug("关键词检测正在处理中，跳过当前检测");
                return;
            }

            await _stateChangeSemaphore.WaitAsync();
            try
            {
                _isProcessingKeywordDetection = true;

                switch (_lastDeviceState)
                {
                    case DeviceState.Idle:
                        // 在空闲状态检测到关键词，暂停检测避免干扰
                        _logger?.LogInformation("在空闲状态检测到关键词，暂停关键词检测");
                        Pause(); // 暂停检测避免干扰
                        
                        // 短暂延迟确保暂停操作完成
                        await Task.Delay(100);
                        // 注意：不在这里调用 StartVoiceChatAsync，让 VoiceChatService 的事件处理负责
                        break;

                    case DeviceState.Speaking:
                        // 在AI说话时检测到关键词，准备中断对话
                        _logger?.LogInformation("在AI说话时检测到关键词，准备中断当前对话");
                        // 注意：不在这里调用 StopVoiceChatAsync，让 VoiceChatService 的事件处理负责
                        break;

                    case DeviceState.Listening:
                        // 在监听状态检测到关键词，可能是误触发，忽略
                        _logger?.LogDebug("在监听状态检测到关键词，忽略（可能是误触发）");
                        break;

                    default:
                        _logger?.LogDebug($"在状态 {_lastDeviceState} 检测到关键词，暂不处理");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "处理关键词检测时发生错误");
                OnErrorOccurred($"关键词检测处理错误: {ex.Message}");
            }
            finally
            {
                _isProcessingKeywordDetection = false;
                _stateChangeSemaphore.Release();
            }
        });
    }
      /// <summary>
    /// 识别取消事件处理
    /// </summary>
    private void OnRecognitionCanceled(object? sender, SpeechRecognitionCanceledEventArgs e)
    {
        _logger?.LogWarning($"关键词识别被取消: {e.Reason}, 错误代码: {e.ErrorCode}, 详情: {e.ErrorDetails}");

        if (e.Reason == CancellationReason.Error)
        {
            _logger?.LogWarning($"识别错误: {e.ErrorDetails}");
            OnErrorOccurred($"识别错误: {e.ErrorDetails}");
        }
        
        // 如果是因为错误被取消且服务仍在运行，尝试重启识别
        if (e.Reason == CancellationReason.Error && _isRunning && !_isPaused)
        {
            _logger?.LogInformation("检测到识别错误，尝试重启关键词识别");
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
    
    /// <summary>
    /// 重启连续关键词识别（实现持续检测功能）
    /// 为了避免 SPXERR_INVALID_HANDLE 错误，每次重启都创建全新的识别器实例
    /// </summary>
    private void RestartContinuousRecognition()
    {
        if (!_isRunning || _isPaused)
        {
            return;
        }

        // 在后台任务中重启识别，避免阻塞当前处理
        _ = Task.Run(async () =>
        {
            try
            {
                // 增加延迟时间以确保SDK完全释放资源
                await Task.Delay(500);
                
                // 再次检查状态，防止在延迟期间服务被停止
                if (!_isRunning || _isPaused)
                {
                    _logger?.LogDebug("服务状态已变更，跳过重启识别");
                    return;
                }

                // 使用信号量确保线程安全
                await _semaphore.WaitAsync();
                try
                {
                    // 最终状态检查
                    if (_isRunning && !_isPaused)
                    {
                        _logger?.LogDebug("开始重新创建关键词识别器...");
                        
                        // 完全重建识别器以避免句柄错误
                        await RecreateKeywordRecognizer();
                        
                        _logger?.LogDebug("关键词识别器已重新创建并启动，继续监听");
                    }
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "重启连续关键词识别时发生错误");
                
                // 如果重建失败，尝试再次重建
                if (_isRunning && !_isPaused)
                {
                    _logger?.LogInformation("重建失败，1秒后尝试再次重建...");
                    await Task.Delay(1000);
                    
                    if (_isRunning && !_isPaused)
                    {
                        try
                        {
                            await _semaphore.WaitAsync();
                            try
                            {
                                await RecreateKeywordRecognizer();
                                _logger?.LogInformation("延迟重建成功");
                            }
                            finally
                            {
                                _semaphore.Release();
                            }
                        }
                        catch (Exception retryEx)
                        {
                            _logger?.LogError(retryEx, "延迟重建仍然失败");
                            OnErrorOccurred($"重启关键词识别失败: {retryEx.Message}");
                        }
                    }
                }
            }
        });
    }
    
    
    /// <summary>
    /// 停止关键词检测（对应py-xiaozhi的stop方法）
    /// </summary>
    public async Task StopAsync()
    {
        try
        {
            await _semaphore.WaitAsync();

            if (!_isRunning) return;

            _cancellationTokenSource?.Cancel();

            // 使用新的清理方法
            await CleanupKeywordRecognizer();

            // 等待音频推送任务完成
            if (_audioPushTask != null)
            {
                try
                {
                    await _audioPushTask;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "等待音频推送任务完成时发生警告");
                }
                finally
                {
                    _audioPushTask = null;
                }
            }

            _isRunning = false;
            _isPaused = false;

            _logger?.LogInformation("关键词检测已停止");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "停止关键词检测时发生错误");
        }
        finally
        {
            _semaphore.Release();
        }
    }    
    
    /// <summary>
    /// 暂停检测（对应py-xiaozhi的pause方法）
    /// </summary>
    public void Pause()
    {
        if (_isRunning && !_isPaused)
        {
            _isPaused = true;
            
            // 停止Microsoft认知服务的关键词识别器
            _ = Task.Run(async () =>
            {
                try
                {
                    await _semaphore.WaitAsync();
                    try
                    {
                        if (_keywordRecognizer != null)
                        {
                            await _keywordRecognizer.StopRecognitionAsync();
                            _logger?.LogDebug("关键词识别器已停止");
                        }
                    }
                    finally
                    {
                        _semaphore.Release();
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "暂停关键词检测时发生错误");
                }
            });
            
            _logger?.LogInformation("关键词检测已暂停");
        }
    }

    /// <summary>
    /// 恢复检测（对应py-xiaozhi的resume方法）
    /// </summary>
    public void Resume()
    {
        if (_isRunning && _isPaused)
        {
            try
            {
                _isPaused = false;
                
                // 验证音频源是否可用
                if (_useExternalAudioSource && _audioRecorder != null && !_audioRecorder.IsRecording)
                {
                    _logger?.LogWarning("外部音频源未录制，无法恢复关键词检测");
                    _isPaused = true; // 回滚状态
                    return;
                }
                
                // 重新启动Microsoft认知服务的关键词识别器
                // 使用RestartContinuousRecognition方法重启关键词识别
                // 这确保了正确的连续识别逻辑并避免句柄错误
                RestartContinuousRecognition();
                
                _logger?.LogInformation("关键词检测已恢复");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "恢复关键词检测时发生错误");
                _isPaused = true; // 回滚状态
                OnErrorOccurred($"恢复关键词检测失败: {ex.Message}");
            }
        }
        else
        {
            if (!_isRunning)
            {
                _logger?.LogDebug("关键词检测器未运行，无法恢复");
            }
            else if (!_isPaused)
            {
                _logger?.LogDebug("关键词检测器未暂停，无需恢复");
            }
        }
    }

    /// <summary>
    /// 更新音频源（对应py-xiaozhi的update_stream方法）
    /// </summary>
    public bool UpdateAudioSource(IAudioRecorder audioRecorder)
    {
        if (!_isRunning)
        {
            _logger?.LogWarning("关键词检测器未运行，无法更新音频源");
            return false;
        }

        try
        {
            _semaphore.Wait();

            _audioRecorder = audioRecorder;
            _useExternalAudioSource = true;
            _logger?.LogInformation("已更新关键词检测器的音频源");
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "更新音频源时发生错误");
            return false;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// 设备状态变化处理（实现py-xiaozhi的状态协调）
    /// </summary>
    private void OnDeviceStateChanged(object? sender, DeviceState newState)
    {
        _lastDeviceState = newState;
        _logger?.LogDebug($"设备状态变化: {newState}");

        // 实现py-xiaozhi的状态协调逻辑
        switch (newState)
        {
            case DeviceState.Listening:
                // 开始监听时暂停关键词检测，避免干扰
                Pause();
                break;

            case DeviceState.Speaking:
                // AI说话时暂停检测，避免误触发（匹配py-xiaozhi行为）
                Pause();
                break;

            case DeviceState.Idle:
                // 空闲时恢复检测，等待下次唤醒
                // 延迟恢复确保音频流稳定
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(200); // 短暂延迟确保状态稳定
                        if (_lastDeviceState == DeviceState.Idle && _isRunning && _isPaused)
                        {
                            Resume();
                            _logger?.LogDebug("在空闲状态恢复关键词检测");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "空闲状态恢复关键词检测时发生错误");
                    }
                });
                break;

            case DeviceState.Connecting:
                // 连接时暂停检测
                Pause();
                break;
        }
    }

    /// <summary>
    /// 触发错误事件
    /// </summary>
    private void OnErrorOccurred(string error)
    {
        ErrorOccurred?.Invoke(this, error);
    }    public void Dispose()
    {
        // Use the async method but wait for completion during disposal
        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "停止关键词检测时发生错误 (在Dispose中)");
        }

        _voiceChatService.DeviceStateChanged -= OnDeviceStateChanged;

        _keywordModel?.Dispose();

        // SpeechConfig不实现IDisposable，无需手动释放
        _cancellationTokenSource?.Dispose();
        _semaphore.Dispose();
        
        // 清理新添加的同步对象
        _stateChangeSemaphore?.Dispose();

        _logger?.LogInformation("关键词检测服务已释放");
    }
    
    
    /// <summary>
    /// 配置共享音频输入（类似 py-xiaozhi 的 AudioCodec 共享流模式）
    /// 每次调用都创建新的音频流实例以避免句柄错误
    /// </summary>
    private async Task<AudioConfig?> ConfigureSharedAudioInput()
    {
        try
        {
            // 清理现有的推送流
            if (_pushStream != null)
            {
                try
                {
                    _pushStream.Close();
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "关闭现有推送流时出现警告（这是正常的）");
                }
                finally
                {
                    _pushStream = null;
                }
            }

            // 停止现有的音频推送任务
            if (_audioPushTask != null)
            {
                _cancellationTokenSource?.Cancel();
                try
                {
                    await _audioPushTask;
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "停止现有音频推送任务时出现警告（这是正常的）");
                }
                finally
                {
                    _audioPushTask = null;
                }
            }

            // 重新创建取消令牌
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();

            // 启动共享音频流管理器
            await _audioStreamManager.StartRecordingAsync();

            // 创建新的推送音频流用于关键词检测
            var format = AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1); // 16kHz, 16-bit, mono
            _pushStream = AudioInputStream.CreatePushStream(format);

            // 启动音频数据推送任务，从共享流获取数据
            _audioPushTask = Task.Run(() => PushSharedAudioDataAsync(_audioStreamManager, _cancellationTokenSource.Token));

            return AudioConfig.FromStreamInput(_pushStream);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "配置共享音频输入失败，回退到默认输入");
            return AudioConfig.FromDefaultMicrophoneInput();
        }
    }

    /// <summary>
    /// 重新创建关键词识别器以避免 SPXERR_INVALID_HANDLE 错误
    /// 每次重启都创建全新的实例，确保资源完全重置
    /// </summary>
    private async Task RecreateKeywordRecognizer()
    {
        try
        {
            // 1. 完全清理现有资源
            await CleanupKeywordRecognizer();

            // 2. 重新加载关键词模型
            if (!LoadKeywordModels())
            {
                throw new InvalidOperationException("重新加载关键词模型失败");
            }

            // 3. 重新配置音频输入
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
                _logger?.LogDebug("新的关键词识别器实例已创建并启动");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "重新创建关键词识别器失败");
            throw;
        }
    }

    /// <summary>
    /// 清理关键词识别器资源
    /// </summary>
    private async Task CleanupKeywordRecognizer()
    {
        // 停止现有识别器
        if (_keywordRecognizer != null)
        {
            try
            {
                await _keywordRecognizer.StopRecognitionAsync();
                await Task.Delay(200); // 给SDK时间完全停止
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "停止识别器时出现警告（这是正常的）");
            }

            try
            {
                _keywordRecognizer.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "释放识别器时出现警告（这是正常的）");
            }
            finally
            {
                _keywordRecognizer = null;
            }
        }

        // 重新创建关键词模型（避免模型实例重用）
        if (_keywordModel != null)
        {
            try
            {
                _keywordModel.Dispose();
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "释放关键词模型时出现警告（这是正常的）");
            }
            finally
            {
                _keywordModel = null;
            }
        }

        // 清理音频流
        if (_pushStream != null)
        {
            try
            {
                _pushStream.Close();
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "关闭推送流时出现警告（这是正常的）");
            }
            finally
            {
                _pushStream = null;
            }
        }
    }
}
