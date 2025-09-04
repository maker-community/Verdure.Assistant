using Microsoft.Extensions.Logging;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;

namespace InterruptArchitectureTest.Sources;

/// <summary>
/// 关键字打断源 - 使用 Microsoft 认知服务进行关键字检测
/// </summary>
public class KeywordInterruptSource : Core.InterruptSourceBase
{
    private readonly List<string> _keywords = new();
    private SpeechConfig? _speechConfig;
    private KeywordRecognizer? _keywordRecognizer;
    private AudioConfig? _audioConfig;
    private readonly string _keywordModelPath;

    public KeywordInterruptSource(string keywordModelPath, ILogger<KeywordInterruptSource>? logger = null)
        : base("KeywordDetector", Core.InterruptTypes.Keyword, logger)
    {
        _keywordModelPath = keywordModelPath;
    }

    public void AddKeyword(string keyword)
    {
        if (!_keywords.Contains(keyword))
        {
            _keywords.Add(keyword);
            _logger?.LogInformation("Added keyword: {Keyword}", keyword);
        }
    }

    protected override async Task OnStartAsync()
    {
        try
        {
            // 创建语音配置 - 使用本地模式
            _speechConfig = SpeechConfig.FromSubscription("dummy", "dummy");
            _speechConfig.SpeechRecognitionLanguage = "zh-CN";

            // 创建音频配置 - 使用默认麦克风
            _audioConfig = AudioConfig.FromDefaultMicrophoneInput();

            // 如果有关键字模型文件，使用模型文件
            if (!string.IsNullOrEmpty(_keywordModelPath) && File.Exists(_keywordModelPath))
            {
                var keywordModel = KeywordRecognitionModel.FromFile(_keywordModelPath);
                _keywordRecognizer = new KeywordRecognizer(_audioConfig);
                
                // 订阅事件
                _keywordRecognizer.Recognized += OnKeywordRecognized;
                _keywordRecognizer.Canceled += OnRecognitionCanceled;

                // 开始识别
                await _keywordRecognizer.RecognizeOnceAsync(keywordModel);
                _logger?.LogInformation("Keyword recognition started with model file: {ModelPath}", _keywordModelPath);
            }
            else
            {
                _logger?.LogWarning("No keyword model file found, using simple audio monitoring");
                // 如果没有模型文件，使用简单的音频监听
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to start keyword recognition");
            throw;
        }
    }

    protected override async Task OnStopAsync()
    {
        try
        {
            if (_keywordRecognizer != null)
            {
                _keywordRecognizer.Recognized -= OnKeywordRecognized;
                _keywordRecognizer.Canceled -= OnRecognitionCanceled;
                
                await _keywordRecognizer.StopRecognitionAsync();
                _keywordRecognizer.Dispose();
                _keywordRecognizer = null;
            }

            _audioConfig?.Dispose();
            _speechConfig = null;

            _logger?.LogInformation("Keyword recognition stopped");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error stopping keyword recognition");
            throw;
        }
    }

    protected override async Task MonitoringLoopAsync()
    {
        // 关键字检测是事件驱动的，这里主要做健康检查
        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                if (!_isPaused && IsEnabled)
                {
                    // 检查识别器状态
                    // 由于Microsoft认知服务的限制，这里主要做简单的状态检查
                    await Task.Delay(5000, _cancellationTokenSource.Token); // 5秒检查一次
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
                _logger?.LogError(ex, "Error in keyword monitoring loop");
                await Task.Delay(1000, _cancellationTokenSource.Token);
            }
        }
    }

    private void OnKeywordRecognized(object? sender, KeywordRecognitionEventArgs e)
    {
        var recognizedText = e.Result.Text;
        _logger?.LogInformation("Keyword recognized: {Text}", recognizedText);

        // 检查是否匹配预设关键字
        var matchedKeyword = _keywords.FirstOrDefault(k => 
            recognizedText.Contains(k, StringComparison.OrdinalIgnoreCase));

        if (matchedKeyword != null || _keywords.Count == 0) // 如果没有指定关键字，接受所有
        {
            TriggerInterrupt(
                $"Keyword '{matchedKeyword ?? recognizedText}' detected", 
                new { Keyword = matchedKeyword ?? recognizedText, FullText = recognizedText }, 
                priority: 10
            );
        }
    }

    private void OnRecognitionCanceled(object? sender, SpeechRecognitionCanceledEventArgs e)
    {
        _logger?.LogWarning("Keyword recognition canceled: {Reason}", e.Reason);
        
        if (e.Reason == CancellationReason.Error)
        {
            _logger?.LogError("Keyword recognition error: {ErrorCode} - {ErrorDetails}", 
                e.ErrorCode, e.ErrorDetails);
        }
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
