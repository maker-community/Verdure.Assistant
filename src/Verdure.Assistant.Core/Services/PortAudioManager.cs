using PortAudioSharp;
using Microsoft.Extensions.Logging;

namespace Verdure.Assistant.Core.Services;

/// <summary>
/// PortAudio 单例管理器 - 确保全局只有一次初始化
/// 参考 py-xiaozhi 的 AudioCodec 单例模式，避免重复初始化导致的资源冲突
/// </summary>
public sealed class PortAudioManager : IDisposable
{    
    private static readonly Lazy<PortAudioManager> _instance = new(() => new PortAudioManager());
    private readonly object _lock = new();
    private bool _isInitialized = false;
    private bool _isDisposed = false;
    private int _referenceCount = 0;

    public static PortAudioManager Instance => _instance.Value;

    private PortAudioManager()
    {
        // Private constructor for singleton
    }

    /// <summary>
    /// 获取并增加引用计数，确保 PortAudio 已初始化
    /// </summary>
    public bool AcquireReference()
    {
        lock (_lock)
        {
            if (_isDisposed)
            {
                return false;
            }

            try
            {
                if (!_isInitialized)
                {
                    PortAudio.Initialize();
                    _isInitialized = true;
                    Console.WriteLine("PortAudio 全局初始化成功");
                }

                _referenceCount++;
                Console.WriteLine($"PortAudio 引用计数增加到: {_referenceCount}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PortAudio 初始化失败: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// 释放引用计数，当引用计数为 0 时终止 PortAudio
    /// 优化版本：增强异常处理和状态检查
    /// </summary>
    public void ReleaseReference()
    {
        lock (_lock)
        {
            if (_referenceCount > 0)
            {
                _referenceCount--;
                Console.WriteLine($"PortAudio 引用计数减少到: {_referenceCount}");

                // 只有当没有任何引用时才终止 PortAudio
                if (_referenceCount == 0 && _isInitialized)
                {
                    try
                    {
                        Console.WriteLine("正在终止 PortAudio...");
                        
                        // 增强的超时机制和重试逻辑
                        var maxRetries = 2;
                        var currentRetry = 0;
                        bool terminateSuccess = false;

                        while (currentRetry < maxRetries && !terminateSuccess)
                        {
                            try
                            {
                                var terminateTask = Task.Run(() => PortAudio.Terminate());
                                var completed = terminateTask.Wait(5000); // 增加到5秒超时
                                
                                if (completed)
                                {
                                    terminateSuccess = true;
                                    _isInitialized = false;
                                    Console.WriteLine("PortAudio 已终止");
                                }
                                else
                                {
                                    currentRetry++;
                                    Console.WriteLine($"PortAudio 终止超时，重试 {currentRetry}/{maxRetries}");
                                    
                                    if (currentRetry < maxRetries)
                                    {
                                        // 短暂等待后重试
                                        Thread.Sleep(500);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                currentRetry++;
                                Console.WriteLine($"PortAudio 终止重试 {currentRetry}/{maxRetries} 时出现异常: {ex.Message}");
                                
                                if (currentRetry < maxRetries)
                                {
                                    Thread.Sleep(500);
                                }
                            }
                        }

                        if (!terminateSuccess)
                        {
                            Console.WriteLine("PortAudio 终止失败，强制设置状态");
                            _isInitialized = false; // 强制设置状态，避免资源泄漏
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"PortAudio 终止时出现严重错误: {ex.Message}");
                        _isInitialized = false; // 即使出错也要重置状态
                    }
                }
            }
            else
            {
                Console.WriteLine("PortAudio 引用计数已为 0，跳过释放操作");
            }
        }
    }

    /// <summary>
    /// 检查 PortAudio 是否已初始化
    /// </summary>
    public bool IsInitialized
    {
        get
        {
            lock (_lock)
            {
                return _isInitialized && !_isDisposed;
            }
        }
    }

    /// <summary>
    /// 获取当前引用计数
    /// </summary>
    public int ReferenceCount
    {
        get
        {
            lock (_lock)
            {
                return _referenceCount;
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed) return;

            try
            {
                if (_isInitialized)
                {
                    PortAudio.Terminate();
                    _isInitialized = false;
                    Console.WriteLine("PortAudio 在 Dispose 中已终止");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Dispose PortAudio 时出现警告: {ex.Message}");
            }
            finally
            {
                _isDisposed = true;
                _referenceCount = 0;
                // 抑制终结器调用
                GC.SuppressFinalize(this);
            }
        }
    }
}
