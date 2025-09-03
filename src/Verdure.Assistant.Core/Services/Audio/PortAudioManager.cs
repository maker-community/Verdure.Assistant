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
    /// 树莓派优化版本：更强的异常处理和超时控制
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
                        
                        // 在树莓派上，使用更谨慎的终止策略
                        var terminateTask = Task.Run(() =>
                        {
                            try
                            {
                                PortAudio.Terminate();
                                return true;
                            }
                            catch (PortAudioException paEx)
                            {
                                Console.WriteLine($"PortAudio 终止时的预期异常: {paEx.Message}");
                                return true; // 对于 PortAudio 异常，认为是成功的
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"PortAudio 终止时的意外异常: {ex.Message}");
                                return false;
                            }
                        });

                        var completed = terminateTask.Wait(2000); // 缩短到2秒超时
                        
                        if (completed && terminateTask.Result)
                        {
                            _isInitialized = false;
                            Console.WriteLine("PortAudio 已终止");
                        }
                        else
                        {
                            Console.WriteLine("PortAudio 终止超时或失败，强制重置状态");
                            _isInitialized = false; // 强制重置状态
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"PortAudio 终止过程中出现严重错误: {ex.Message}");
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
    /// 强制清理 PortAudio（用于紧急情况下的资源恢复）
    /// </summary>
    public void ForceCleanup()
    {
        lock (_lock)
        {
            try
            {
                Console.WriteLine("执行 PortAudio 强制清理...");
                
                // 强制重置引用计数
                _referenceCount = 0;
                
                if (_isInitialized)
                {
                    try
                    {
                        // 尝试快速终止
                        var terminateTask = Task.Run(() => PortAudio.Terminate());
                        var completed = terminateTask.Wait(1000); // 1秒快速超时
                        
                        if (!completed)
                        {
                            Console.WriteLine("强制清理：PortAudio 终止超时");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"强制清理：PortAudio 终止异常: {ex.Message}");
                    }
                    finally
                    {
                        _isInitialized = false;
                    }
                }
                
                Console.WriteLine("PortAudio 强制清理完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"PortAudio 强制清理时出错: {ex.Message}");
                // 确保状态重置
                _isInitialized = false;
                _referenceCount = 0;
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
                Console.WriteLine("开始 Dispose PortAudioManager...");
                
                if (_isInitialized)
                {
                    // 在 Dispose 中使用更快的超时
                    var disposeTask = Task.Run(() =>
                    {
                        try
                        {
                            PortAudio.Terminate();
                            return true;
                        }
                        catch (PortAudioException paEx)
                        {
                            Console.WriteLine($"Dispose 时的 PortAudio 异常: {paEx.Message}");
                            return true; // 认为是成功的
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Dispose 时的意外异常: {ex.Message}");
                            return false;
                        }
                    });

                    var completed = disposeTask.Wait(1500); // 1.5秒超时
                    
                    if (completed)
                    {
                        Console.WriteLine("PortAudio 在 Dispose 中已终止");
                    }
                    else
                    {
                        Console.WriteLine("Dispose PortAudio 超时，强制完成");
                    }
                    
                    _isInitialized = false;
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
                // 抑制终结器调用，防止二次清理
                GC.SuppressFinalize(this);
                Console.WriteLine("PortAudioManager Dispose 完成");
            }
        }
    }
}
