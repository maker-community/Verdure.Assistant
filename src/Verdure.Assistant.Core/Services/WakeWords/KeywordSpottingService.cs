using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Models;

namespace Verdure.Assistant.Core.Services;

/// <summary>
/// 基于Microsoft认知服务的关键词唤醒服务（离线 .table 模型，无需订阅密钥）。
/// <para>
/// 实现采用"监督循环"：单个后台任务反复执行 创建识别器 → 识别一次 → 释放 的周期。
/// 这样 <see cref="Pause"/> / <see cref="Resume"/> / <see cref="StopAsync"/> / 模型切换都只是向循环发信号，
/// 不会再持有锁等待下一次唤醒词（旧实现里重启识别会一直占用信号量，导致停止/释放时界面卡死）。
/// 识别到唤醒词后自动进入冷却并重启识别，调用方无需再 Stop+Start。
/// </para>
/// </summary>
public class KeywordSpottingService : IKeywordSpottingService
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);
    private const int MaxBackoffSeconds = 15;

    private readonly ILogger<KeywordSpottingService>? _logger;
    private readonly ISharedAudioRecorder _audioStreamManager;
    private readonly KeywordModelLocator _modelLocator;

    private readonly object _gate = new();
    private VerdureConfig? _config;
    private bool _isEnabled = true;

    // 生命周期（受 _gate 保护）
    private CancellationTokenSource? _runCts;
    private Task? _loopTask;
    private TaskCompletionSource _resumeSignal = NewSignal();
    private volatile bool _running;
    private volatile bool _paused;

    // 当前正在识别的识别器，用于从外部中断 RecognizeOnceAsync
    private KeywordRecognizer? _activeRecognizer;

    public event EventHandler<KeywordDetectedEventArgs>? KeywordDetected;
    public event EventHandler<string>? ErrorOccurred;

    /// <summary>
    /// 检测到唤醒词后的冷却时间，期间不会再次触发（抑制回声/重复唤醒）
    /// </summary>
    public TimeSpan DetectionCooldown { get; set; } = TimeSpan.FromMilliseconds(1500);

    public bool IsRunning => _running && !_paused;
    public bool IsPaused => _paused;
    public bool IsEnabled => _isEnabled;

    public KeywordSpottingService(ISharedAudioRecorder audioStreamManager,
        ILogger<KeywordSpottingService>? logger = null,
        IPlatformResourceService? platformResourceService = null)
    {
        _audioStreamManager = audioStreamManager;
        _logger = logger;
        _modelLocator = new KeywordModelLocator(platformResourceService, logger);
    }

    private static TaskCompletionSource NewSignal()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// 设置配置信息（在启动前调用）
    /// </summary>
    public void SetConfig(VerdureConfig config)
    {
        _config = config;
        _logger?.LogInformation("关键词检测服务配置已更新");
    }

    /// <summary>
    /// 启动关键词检测。重复调用是安全的（已在运行时直接返回 true）。
    /// </summary>
    public async Task<bool> StartAsync(IAudioRecorder? audioRecorder = null)
    {
        if (!_isEnabled)
        {
            _logger?.LogWarning("关键词检测功能未启用");
            return false;
        }

        lock (_gate)
        {
            if (_running)
            {
                _logger?.LogDebug("关键词检测已在运行");
                return true;
            }
        }

        // 先确认模型存在，缺失时直接返回失败而不是让后台循环无休止重试
        var modelPath = await _modelLocator.ResolveModelPathAsync(_config).ConfigureAwait(false);
        if (modelPath == null)
        {
            _logger?.LogError("找不到关键词模型文件: {Model}", _config?.KeywordModels.CurrentModel);
            return false;
        }

        lock (_gate)
        {
            if (_running)
            {
                return true;
            }

            _runCts = new CancellationTokenSource();
            _paused = false;
            _resumeSignal = NewSignal();
            _running = true;

            var token = _runCts.Token;
            _loopTask = Task.Run(() => RunLoopAsync(token));
        }

        _logger?.LogInformation("关键词检测启动成功（模型: {Path}）", modelPath);
        return true;
    }

    /// <summary>
    /// 停止关键词检测并等待后台循环退出
    /// </summary>
    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        Task? loop;

        lock (_gate)
        {
            if (!_running)
            {
                return;
            }

            _running = false;
            _paused = false;
            cts = _runCts;
            loop = _loopTask;
            _runCts = null;
            _loopTask = null;
        }

        try { cts?.Cancel(); } catch (ObjectDisposedException) { }
        CompleteResumeSignal();
        InterruptActiveRecognition();

        if (loop != null)
        {
            try
            {
                await loop.WaitAsync(StopTimeout).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                _logger?.LogWarning("等待关键词识别循环退出超时（{Timeout}s），已放弃等待", StopTimeout.TotalSeconds);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "关键词识别循环退出时出现异常");
            }
        }

        cts?.Dispose();
        _logger?.LogInformation("关键词检测已停止");
    }

    /// <summary>
    /// 暂停检测：立即中断当前识别，之后不再占用音频流，直到 <see cref="Resume"/>。
    /// </summary>
    public void Pause()
    {
        if (!_running || _paused)
        {
            return;
        }

        ResetResumeSignalIfCompleted();
        _paused = true;
        InterruptActiveRecognition();
        _logger?.LogInformation("关键词检测已暂停");
    }

    /// <summary>
    /// 恢复检测
    /// </summary>
    public void Resume()
    {
        if (!_running)
        {
            _logger?.LogDebug("关键词检测器未运行，无法恢复");
            return;
        }

        if (!_paused)
        {
            return;
        }

        _paused = false;
        CompleteResumeSignal();
        _logger?.LogInformation("关键词检测已恢复");
    }

    /// <summary>
    /// 更新音频源。识别始终使用共享音频流，因此这里只需让识别器重新绑定一次。
    /// </summary>
    public bool UpdateAudioSource(IAudioRecorder audioRecorder)
    {
        if (!_running)
        {
            _logger?.LogWarning("关键词检测器未运行，无法更新音频源");
            return false;
        }

        InterruptActiveRecognition();
        return true;
    }

    /// <summary>
    /// 获取可用的关键词模型列表
    /// </summary>
    public Task<string[]> GetAvailableKeywordModelsAsync() => _modelLocator.GetAvailableModelsAsync(_config);

    /// <summary>
    /// 切换关键词模型。运行中会让识别循环在下一个周期加载新模型。
    /// </summary>
    public async Task<bool> SwitchKeywordModelAsync(string modelFileName)
    {
        if (_config == null)
        {
            _logger?.LogWarning("配置未设置，无法切换关键词模型");
            return false;
        }

        var path = await _modelLocator.ResolveModelPathAsync(_config, modelFileName).ConfigureAwait(false);
        if (path == null)
        {
            _logger?.LogError("关键词模型文件不存在: {FileName}", modelFileName);
            return false;
        }

        _config.KeywordModels.CurrentModel = modelFileName;

        if (_running)
        {
            _logger?.LogInformation("正在切换关键词模型: {Model}", modelFileName);
            InterruptActiveRecognition();
        }

        return true;
    }

    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(30);

    private long _framesSinceReport;
    private int _peakSinceReport;
    private long _listenStartTicks = Environment.TickCount64;

    private void ReportHeartbeat()
    {
        var frames = Interlocked.Exchange(ref _framesSinceReport, 0);
        var peak = Interlocked.Exchange(ref _peakSinceReport, 0) / 32768.0;

        if (_paused)
        {
            _logger?.LogDebug("唤醒词监听暂停中（对话进行中）");
        }
        else if (frames == 0)
        {
            _logger?.LogWarning("唤醒词监听：近 {Seconds:0} 秒没有收到任何麦克风音频，唤醒词无法工作，请检查麦克风权限/默认录音设备", HeartbeatInterval.TotalSeconds);
        }
        else if (peak < 0.01)
        {
            _logger?.LogWarning("唤醒词监听：近 {Seconds:0} 秒收到 {Frames} 帧音频，但音量几乎为 0（峰值 {Peak:P1}），麦克风可能被静音或选错设备", HeartbeatInterval.TotalSeconds, frames, peak);
        }
        else
        {
            _logger?.LogInformation("唤醒词监听正常：近 {Seconds:0} 秒收到 {Frames} 帧音频，峰值电平 {Peak:P0}", HeartbeatInterval.TotalSeconds, frames, peak);
        }
    }

    private void TrackAudioLevel(byte[] data)
    {
        Interlocked.Increment(ref _framesSinceReport);

        var peak = 0;
        for (var i = 0; i + 1 < data.Length; i += 2)
        {
            var sample = Math.Abs((int)(short)(data[i] | (data[i + 1] << 8)));
            if (sample > peak) peak = sample;
        }

        int current;
        while (peak > (current = Volatile.Read(ref _peakSinceReport)) &&
               Interlocked.CompareExchange(ref _peakSinceReport, peak, current) != current)
        {
        }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        var consecutiveFailures = 0;

        // 心跳：定期报告是否真的有麦克风音频送入识别器，便于区分"没听到"与"没识别出"
        using var heartbeat = new Timer(_ => ReportHeartbeat(), null, HeartbeatInterval, HeartbeatInterval);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_paused)
                {
                    await WaitForResumeAsync(ct).ConfigureAwait(false);
                    continue;
                }

                var keyword = await RecognizeOnceAsync(ct).ConfigureAwait(false);
                consecutiveFailures = 0;

                if (keyword != null)
                {
                    RaiseKeywordDetected(keyword);

                    // 冷却期内不创建识别器，避免对同一次发音重复触发
                    await Task.Delay(DetectionCooldown, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                consecutiveFailures++;
                _logger?.LogError(ex, "关键词识别周期失败（连续 {Count} 次）", consecutiveFailures);

                if (consecutiveFailures == 1 || consecutiveFailures % 5 == 0)
                {
                    RaiseError($"关键词识别异常: {ex.Message}");
                }

                var delay = TimeSpan.FromSeconds(Math.Min(MaxBackoffSeconds, 1 << Math.Min(consecutiveFailures - 1, 4)));
                try
                {
                    await Task.Delay(delay, ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task WaitForResumeAsync(CancellationToken ct)
    {
        TaskCompletionSource signal;
        lock (_gate)
        {
            signal = _resumeSignal;
        }

        await signal.Task.WaitAsync(ct).ConfigureAwait(false);
    }

    private void CompleteResumeSignal()
    {
        lock (_gate)
        {
            _resumeSignal.TrySetResult();
        }
    }

    // 每次暂停都换一个新的信号，避免上一轮遗留的已完成信号让循环空转
    private void ResetResumeSignalIfCompleted()
    {
        lock (_gate)
        {
            if (_resumeSignal.Task.IsCompleted)
            {
                _resumeSignal = NewSignal();
            }
        }
    }

    /// <summary>
    /// 创建全新的识别器/模型/音频流并识别一次；被暂停、停止或切换模型中断时返回 null。
    /// 每个周期都使用新实例，规避 SPXERR_INVALID_HANDLE。
    /// </summary>
    private async Task<string?> RecognizeOnceAsync(CancellationToken ct)
    {
        var modelPath = await _modelLocator.ResolveModelPathAsync(_config).ConfigureAwait(false)
            ?? throw new FileNotFoundException("关键词模型文件不存在", _config?.KeywordModels.CurrentModel);

        if (!_audioStreamManager.IsRecording)
        {
            await _audioStreamManager.StartRecordingAsync().ConfigureAwait(false);
        }

        using var model = KeywordRecognitionModel.FromFile(modelPath);
        var format = AudioStreamFormat.GetWaveFormatPCM(16000, 16, 1);
        using var pushStream = AudioInputStream.CreatePushStream(format);
        using var audioConfig = AudioConfig.FromStreamInput(pushStream);
        using var recognizer = new KeywordRecognizer(audioConfig);

        var closed = false;
        EventHandler<byte[]> handler = (_, data) =>
        {
            if (closed || _paused)
            {
                return;
            }

            TrackAudioLevel(data);

            try
            {
                pushStream.Write(data);
            }
            catch (ObjectDisposedException)
            {
                // 识别周期已结束
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "写入关键词识别音频流失败");
            }
        };

        _audioStreamManager.SubscribeToAudioData(handler);
        _activeRecognizer = recognizer;

        // 在订阅之后再检查一次，避免 Pause/Stop 恰好发生在创建识别器期间而漏掉中断
        if (_paused || ct.IsCancellationRequested)
        {
            _activeRecognizer = null;
            _audioStreamManager.UnsubscribeFromAudioData(handler);
            return null;
        }

        _listenStartTicks = Environment.TickCount64;

        try
        {
            var result = await recognizer.RecognizeOnceAsync(model).ConfigureAwait(false);

            if (_paused || ct.IsCancellationRequested)
            {
                return null;
            }

            if (result.Reason == ResultReason.RecognizedKeyword)
            {
                return result.Text;
            }

            if (result.Reason == ResultReason.Canceled)
            {
                var details = CancellationDetails.FromResult(result);
                if (details.Reason == CancellationReason.Error)
                {
                    throw new InvalidOperationException($"识别错误 {details.ErrorCode}: {details.ErrorDetails}");
                }
            }

            return null;
        }
        finally
        {
            closed = true;
            _activeRecognizer = null;
            _audioStreamManager.UnsubscribeFromAudioData(handler);
            try { pushStream.Close(); } catch (Exception ex) { _logger?.LogDebug(ex, "关闭推送流时出现警告"); }
        }
    }

    private void InterruptActiveRecognition()
    {
        var recognizer = _activeRecognizer;
        if (recognizer == null)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await recognizer.StopRecognitionAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "中断关键词识别时出现警告（识别器可能已结束）");
            }
        });
    }

    private void RaiseKeywordDetected(string keyword)
    {
        var modelName = _config?.KeywordModels.CurrentModel ?? "keyword_xiaodian.table";
        _logger?.LogInformation("唤醒词命中：模型 {Model}，本轮监听 {Seconds:0.0}s，近期音频峰值 {Peak:P0}（SDK 返回文本: \"{Text}\"，该文本由模型内置，并非实际语音转写）",
            modelName, (Environment.TickCount64 - _listenStartTicks) / 1000.0, Volatile.Read(ref _peakSinceReport) / 32768.0, keyword);

        try
        {
            KeywordDetected?.Invoke(this, new KeywordDetectedEventArgs
            {
                Keyword = keyword,
                FullText = keyword,
                Confidence = 1.0f, // Microsoft认知服务不提供详细置信度
                ModelName = _config?.KeywordModels.CurrentModel ?? "Microsoft Speech Services"
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "处理关键词检测事件时发生错误");
            RaiseError($"关键词识别处理错误: {ex.Message}");
        }
    }

    private void RaiseError(string error)
    {
        try
        {
            ErrorOccurred?.Invoke(this, error);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "关键词错误事件订阅者抛出异常");
        }
    }

    public void Dispose()
    {
        try
        {
            // 不在 UI 线程上无限期阻塞：StopAsync 内部带有超时
            StopAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "停止关键词检测时发生错误 (在Dispose中)");
        }

        _logger?.LogInformation("关键词检测服务已释放");
    }
}
