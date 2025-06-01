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
{
    private readonly ILogger<KeywordSpottingService>? _logger;
    private readonly IVoiceChatService _voiceChatService;

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
    private DeviceState _lastDeviceState = DeviceState.Idle;

    // 音频处理
    private IAudioRecorder? _audioRecorder;
    private bool _useExternalAudioSource = false;
    private AudioInputStream? _audioInputStream;
    private PushAudioInputStream? _pushStream;

    // UI thread dispatcher for cross-platform thread marshaling
    private IUIDispatcher _uiDispatcher;

    // 线程安全
    private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);
    private CancellationTokenSource? _cancellationTokenSource;

    // 事件
    public event EventHandler<KeywordDetectedEventArgs>? KeywordDetected;
    public event EventHandler<string>? ErrorOccurred;

    public bool IsRunning => _isRunning && !_isPaused;
    public bool IsPaused => _isPaused;
    public bool IsEnabled => _isEnabled;

    public KeywordSpottingService(IVoiceChatService voiceChatService, ILogger<KeywordSpottingService>? logger = null, IUIDispatcher? uiDispatcher = null)
    {
        _voiceChatService = voiceChatService;
        _logger = logger;

        // 订阅设备状态变化，实现py-xiaozhi的状态协调逻辑
        _voiceChatService.DeviceStateChanged += OnDeviceStateChanged;

        InitializeSpeechConfig();
        _uiDispatcher = uiDispatcher ?? new DefaultUIDispatcher();
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

            // 配置音频输入
            // var audioConfig = ConfigureAudioInput();

            // 使用默认麦克风输入
            var audioConfig = AudioConfig.FromDefaultMicrophoneInput();
            if (audioConfig == null)
            {
                _logger?.LogError("配置音频输入失败");
                return false;
            }

            // 创建关键词识别器
            _keywordRecognizer = new KeywordRecognizer(audioConfig);

            // 订阅事件
            SubscribeToRecognizerEvents();

            // 开始识别
            _keywordRecognizer?.RecognizeOnceAsync(_keywordModel);

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
    /// </summary>
    private bool LoadKeywordModels()
    {
        try
        {
            // 获取Assets目录路径
            var assetsPath = GetAssetsPath();
            var keywordsPath = Path.Combine(assetsPath, "keywords");

            if (!Directory.Exists(keywordsPath))
            {
                _logger?.LogError($"关键词模型目录不存在: {keywordsPath}");
                return false;
            }

            // 优先使用xiaodian模型（对应py-xiaozhi的主要唤醒词）
            // With the correct method call based on the provided type signatures:  

            var primaryModelPath = Path.Combine(keywordsPath, "keyword_xiaodian.table");


            if (!File.Exists(primaryModelPath))
            {
                _logger?.LogError($"主要关键词模型文件不存在: {primaryModelPath}");
                return false;
            }

            // 从.table文件创建关键词模型
            _keywordModel = KeywordRecognitionModel.FromFile(primaryModelPath);

            //_keywordRecognizer?.RecognizeOnceAsync(_keywordModel);

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
    /// 配置音频输入
    /// </summary>
    private AudioConfig? ConfigureAudioInput()
    {
        try
        {
            if (_useExternalAudioSource && _audioRecorder != null)
            {
                // 使用外部音频源（对应py-xiaozhi的AudioCodec集成模式）
                return ConfigureExternalAudioSource();
            }
            else
            {
                // 使用默认麦克风（对应py-xiaozhi的独立模式）
                return AudioConfig.FromDefaultMicrophoneInput();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "配置音频输入失败");
            return null;
        }
    }

    /// <summary>
    /// 配置外部音频源
    /// </summary>
    private AudioConfig? ConfigureExternalAudioSource()
    {
        try
        {
            // 创建推送音频流
            var format = AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1); // 16kHz, 16-bit, mono
            _pushStream = AudioInputStream.CreatePushStream(format);

            // 启动音频数据推送任务
            _ = Task.Run(() => PushAudioDataAsync());

            return AudioConfig.FromStreamInput(_pushStream);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "配置外部音频源失败");
            return null;
        }
    }

    /// <summary>
    /// 推送音频数据到语音服务
    /// </summary>
    private async Task PushAudioDataAsync()
    {
        if (_audioRecorder == null || _pushStream == null)
            return;

        try
        {
            var buffer = new byte[3200]; // 100ms at 16kHz, 16-bit, mono

            while (_isRunning && !_cancellationTokenSource!.Token.IsCancellationRequested)
            {
                if (_isPaused)
                {
                    await Task.Delay(100);
                    continue;
                }

                // 这里需要从_audioRecorder读取数据
                // 注意：这是一个简化版本，实际实现需要根据IAudioRecorder接口设计
                // var bytesRead = await _audioRecorder.ReadAsync(buffer, 0, buffer.Length);
                // if (bytesRead > 0)
                // {
                //     _pushStream.Write(buffer, (uint)bytesRead);
                // }

                await Task.Delay(50); // 避免过于频繁的循环
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "推送音频数据时发生错误");
            OnErrorOccurred($"音频数据推送错误: {ex.Message}");
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
                // 使用UI调度器确保线程安全的事件处理
                _ = _uiDispatcher.InvokeAsync(() => HandleKeywordDetection(keyword));
                
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
    /// </summary>
    private void HandleKeywordDetection(string keyword)
    {
        Task.Run(async () =>
        {
            try
            {
                switch (_lastDeviceState)
                {
                    case DeviceState.Idle:
                        // 在空闲状态检测到关键词，启动对话（对应py-xiaozhi的唤醒逻辑）
                        _logger?.LogInformation("在空闲状态检测到关键词，启动语音对话");
                        Pause(); // 暂停检测避免干扰
                        await _voiceChatService.StartVoiceChatAsync();
                        break;

                    case DeviceState.Speaking:
                        // 在AI说话时检测到关键词，中断对话（对应py-xiaozhi的中断逻辑）
                        _logger?.LogInformation("在AI说话时检测到关键词，中断当前对话");
                        await _voiceChatService.StopVoiceChatAsync();
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
            OnErrorOccurred($"识别错误: {e.ErrorDetails}");
        }
    }

    /// <summary>
    /// 停止关键词检测（对应py-xiaozhi的stop方法）
    /// </summary>
    public void Stop()
    {
        try
        {
            _semaphore.Wait();

            if (!_isRunning) return;

            _cancellationTokenSource?.Cancel();

            if (_keywordRecognizer != null)
            {
                _keywordRecognizer.StopRecognitionAsync();
                _keywordRecognizer.Dispose();
                _keywordRecognizer = null;
            }

            _pushStream?.Close();
            _pushStream = null;

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
            _isPaused = false;
            _logger?.LogInformation("关键词检测已恢复");
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
                // AI说话时保持检测，以便中断
                Resume();
                break;

            case DeviceState.Idle:
                // 空闲时恢复检测，等待下次唤醒
                Resume();
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
    }

    public void Dispose()
    {
        Stop();

        _voiceChatService.DeviceStateChanged -= OnDeviceStateChanged;

        _keywordModel?.Dispose();

        // SpeechConfig不实现IDisposable，无需手动释放
        _cancellationTokenSource?.Dispose();
        _semaphore.Dispose();

        _logger?.LogInformation("关键词检测服务已释放");
    }
}
