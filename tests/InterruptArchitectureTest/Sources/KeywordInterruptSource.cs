using Microsoft.Extensions.Logging;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;

namespace InterruptArchitectureTest.Sources;

/// <summary>
/// 关键字打断源 - 基于Microsoft认知服务进行关键字检测
/// 参考Verdure.Assistant.Core.Services.KeywordSpottingService实现
/// 支持使用.table模型文件进行离线关键词识别
/// </summary>
public class KeywordInterruptSource : Core.InterruptSourceBase
{
    private readonly List<string> _keywords = new();
    private SpeechConfig? _speechConfig;
    private KeywordRecognizer? _keywordRecognizer;
    private AudioConfig? _audioConfig;
    private KeywordRecognitionModel? _keywordModel;
    private readonly string _keywordModelPath;
    private bool _isModelLoaded = false;
    private bool _continuousRecognition = false;

    public KeywordInterruptSource(string? keywordModelPath = null, ILogger<KeywordInterruptSource>? logger = null)
        : base("KeywordDetector", Core.InterruptTypes.Keyword, logger)
    {
        _keywordModelPath = keywordModelPath ?? GetDefaultModelPath();
        InitializeSpeechConfig();
    }

    /// <summary>
    /// 添加监听的关键词（用于过滤）
    /// </summary>
    public void AddKeyword(string keyword)
    {
        if (!_keywords.Contains(keyword))
        {
            _keywords.Add(keyword);
            _logger?.LogInformation("Added keyword filter: {Keyword}", keyword);
        }
    }

    /// <summary>
    /// 初始化语音配置（离线模式）
    /// </summary>
    private void InitializeSpeechConfig()
    {
        try
        {
            // 创建离线语音配置 - 参考KeywordSpottingService的实现
            _speechConfig = SpeechConfig.FromSubscription("dummy", "dummy");
            
            // 设置为离线模式和中文
            _speechConfig.SetProperty("SPEECH-UseOfflineRecognition", "true");
            _speechConfig.SpeechRecognitionLanguage = "zh-CN";

            _logger?.LogInformation("语音配置初始化成功（离线模式）");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "初始化语音配置失败");
            throw;
        }
    }

    /// <summary>
    /// 获取默认模型路径
    /// </summary>
    private string GetDefaultModelPath()
    {
        // 查找ModelFiles目录下的关键词模型
        var currentDir = Directory.GetCurrentDirectory();
        var modelFiles = new[]
        {
            Path.Combine(currentDir, "ModelFiles", "keyword_xiaodian.table"),
            Path.Combine(currentDir, "ModelFiles", "keyword_cortana.table"),
        };

        foreach (var modelFile in modelFiles)
        {
            if (File.Exists(modelFile))
            {
                _logger?.LogInformation("Found keyword model: {ModelPath}", modelFile);
                return modelFile;
            }
        }

        _logger?.LogWarning("No keyword model found in ModelFiles directory");
        return "";
    }

    /// <summary>
    /// 加载关键词模型
    /// </summary>
    private bool LoadKeywordModel()
    {
        try
        {
            if (string.IsNullOrEmpty(_keywordModelPath) || !File.Exists(_keywordModelPath))
            {
                _logger?.LogError("关键词模型文件不存在: {ModelPath}", _keywordModelPath);
                return false;
            }

            // 释放之前的模型
            _keywordModel?.Dispose();
            
            // 从.table文件创建关键词模型
            _keywordModel = KeywordRecognitionModel.FromFile(_keywordModelPath);
            _isModelLoaded = true;

            _logger?.LogInformation("成功加载关键词模型: {ModelPath}", _keywordModelPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "加载关键词模型失败");
            return false;
        }
    }

    protected override async Task OnStartAsync()
    {
        try
        {
            // 加载关键词模型
            if (!LoadKeywordModel())
            {
                _logger?.LogError("无法加载关键词模型，启动失败");
                return;
            }

            // 创建音频配置 - 使用默认麦克风
            _audioConfig = AudioConfig.FromDefaultMicrophoneInput();

            _logger?.LogInformation("关键词检测已启动，模型: {ModelPath}", _keywordModelPath);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "启动关键词检测失败");
            throw;
        }
    }

    protected override async Task OnStopAsync()
    {
        try
        {
            // 停止关键词识别
            await StopKeywordRecognition();

            // 清理音频配置
            _audioConfig?.Dispose();
            _audioConfig = null;

            _logger?.LogInformation("关键词检测已停止");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "停止关键词检测时发生错误");
            throw;
        }
    }

    protected override async Task MonitoringLoopAsync()
    {
        _logger?.LogInformation("关键词监控循环已启动");

        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                if (!_isPaused && IsEnabled && _isModelLoaded)
                {
                    // 启动连续关键词识别
                    await StartContinuousRecognition();
                    
                    // 等待检测或取消
                    await Task.Delay(5000, _cancellationTokenSource.Token);
                }
                else
                {
                    await Task.Delay(1000, _cancellationTokenSource.Token);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "关键词监控循环出现错误");
                
                // 重启识别器
                await RestartRecognition();
                await Task.Delay(2000, _cancellationTokenSource.Token);
            }
        }

        _logger?.LogInformation("关键词监控循环已退出");
    }

    /// <summary>
    /// 启动连续关键词识别
    /// </summary>
    private async Task StartContinuousRecognition()
    {
        if (_continuousRecognition || _keywordModel == null || _audioConfig == null)
        {
            return;
        }

        try
        {
            // 创建新的识别器实例
            _keywordRecognizer?.Dispose();
            _keywordRecognizer = new KeywordRecognizer(_audioConfig);
            
            // 订阅事件
            _keywordRecognizer.Recognized += OnKeywordRecognized;
            _keywordRecognizer.Canceled += OnRecognitionCanceled;

            // 启动持续识别 - 参考KeywordSpottingService的实现
            await _keywordRecognizer.RecognizeOnceAsync(_keywordModel);
            _continuousRecognition = true;
            
            _logger?.LogDebug("连续关键词识别已启动");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "启动连续关键词识别失败");
        }
    }

    /// <summary>
    /// 停止关键词识别
    /// </summary>
    private async Task StopKeywordRecognition()
    {
        if (_keywordRecognizer != null)
        {
            _keywordRecognizer.Recognized -= OnKeywordRecognized;
            _keywordRecognizer.Canceled -= OnRecognitionCanceled;
            
            try
            {
                await _keywordRecognizer.StopRecognitionAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "停止关键词识别时出现警告");
            }
            
            _keywordRecognizer.Dispose();
            _keywordRecognizer = null;
        }
        
        _continuousRecognition = false;
    }

    /// <summary>
    /// 重启识别器
    /// </summary>
    private async Task RestartRecognition()
    {
        try
        {
            _logger?.LogInformation("重启关键词识别器...");
            
            await StopKeywordRecognition();
            await Task.Delay(1000); // 等待资源释放
            
            if (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                await StartContinuousRecognition();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "重启关键词识别器失败");
        }
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
                var recognizedText = e.Result.Text;
                _logger?.LogInformation("检测到关键词: {Text}", recognizedText);

                // 检查是否匹配预设关键词过滤器
                var matchedKeyword = _keywords.Count == 0 ? recognizedText : 
                    _keywords.FirstOrDefault(k => recognizedText.Contains(k, StringComparison.OrdinalIgnoreCase));

                if (matchedKeyword != null || _keywords.Count == 0) // 如果没有指定关键字过滤器，接受所有
                {
                    // 触发中断事件
                    TriggerInterrupt(
                        $"关键词 '{matchedKeyword ?? recognizedText}' 被检测到", 
                        new { 
                            Keyword = matchedKeyword ?? recognizedText, 
                            FullText = recognizedText,
                            ModelPath = _keywordModelPath,
                            Confidence = 1.0f // Microsoft Speech Service不提供详细置信度
                        }, 
                        priority: 10 // 关键词中断优先级较高
                    );
                }

                // 重启连续识别 - KeywordRecognizer的RecognizeOnceAsync检测到关键词后会停止
                _ = Task.Run(async () =>
                {
                    await Task.Delay(500); // 短暂延迟避免资源冲突
                    if (!_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        await RestartRecognition();
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理关键词识别事件时发生错误");
        }
    }

    /// <summary>
    /// 识别取消事件处理
    /// </summary>
    private void OnRecognitionCanceled(object? sender, SpeechRecognitionCanceledEventArgs e)
    {
        try
        {
            _continuousRecognition = false;
            
            _logger?.LogWarning("关键词识别被取消: {Reason}", e.Reason);
            
            if (e.Reason == CancellationReason.Error)
            {
                _logger?.LogError("关键词识别错误: {ErrorCode} - {ErrorDetails}", 
                    e.ErrorCode, e.ErrorDetails);
                
                // 自动重启识别
                _ = Task.Run(async () =>
                {
                    await Task.Delay(1000);
                    if (!_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        await RestartRecognition();
                    }
                });
            }
            else if (e.Reason == CancellationReason.EndOfStream)
            {
                _logger?.LogInformation("音频流结束，重启识别");
                
                // 音频流结束，重启识别
                _ = Task.Run(async () =>
                {
                    await Task.Delay(500);
                    if (!_cancellationTokenSource.Token.IsCancellationRequested)
                    {
                        await RestartRecognition();
                    }
                });
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理识别取消事件时发生错误");
        }
    }

    /// <summary>
    /// 资源清理
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopKeywordRecognition().GetAwaiter().GetResult();
            _keywordModel?.Dispose();
            _speechConfig = null;
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// 简化的关键字检测源 - 用于测试，不依赖外部服务
/// </summary>
public class SimpleKeywordInterruptSource : Core.InterruptSourceBase
{
    private readonly List<string> _keywords = new() { "停止", "暂停", "打断" };
    private readonly Random _random = new();

    public SimpleKeywordInterruptSource(ILogger<SimpleKeywordInterruptSource>? logger = null)
        : base("SimpleKeywordDetector", Core.InterruptTypes.Keyword, logger)
    {
    }

    public void AddKeyword(string keyword)
    {
        if (!_keywords.Contains(keyword))
        {
            _keywords.Add(keyword);
            _logger?.LogInformation("Added keyword: {Keyword}", keyword);
        }
    }

    protected override async Task MonitoringLoopAsync()
    {
        _logger?.LogInformation("Simple keyword monitoring started. Keywords: {Keywords}", 
            string.Join(", ", _keywords));

        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                if (!_isPaused && IsEnabled)
                {
                    // 模拟关键字检测 - 随机触发（仅用于测试）
                    if (_random.Next(1, 100) <= 2) // 2% 概率
                    {
                        var randomKeyword = _keywords[_random.Next(_keywords.Count)];
                        TriggerInterrupt(
                            $"Simulated keyword '{randomKeyword}' detected", 
                            new { Keyword = randomKeyword }, 
                            priority: 10
                        );
                    }
                }

                await Task.Delay(1000, _cancellationTokenSource.Token); // 每秒检查一次
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in simple keyword monitoring loop");
                await Task.Delay(1000, _cancellationTokenSource.Token);
            }
        }
    }
}
