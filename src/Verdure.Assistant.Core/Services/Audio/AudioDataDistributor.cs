using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Verdure.Assistant.Core.Services
{
    /// <summary>
    /// 音频数据分发器 - 使用 System.Threading.Channels 优化
    /// 提供高性能的音频数据广播功能，支持多个订阅者
    /// 避免在音频回调中执行阻塞操作，提高音频处理性能
    /// </summary>
    internal class AudioDataDistributor : IDisposable
    {
        private readonly Channel<(byte[] Data, EventHandler<byte[]>? MainHandler)> _dataChannel;
        private readonly ChannelWriter<(byte[] Data, EventHandler<byte[]>? MainHandler)> _writer;
        private readonly ChannelReader<(byte[] Data, EventHandler<byte[]>? MainHandler)> _reader;
        private readonly ConcurrentBag<EventHandler<byte[]>> _subscribers = new();
        private readonly Task _distributionTask;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly ILogger? _logger;
        private bool _disposed;

        public AudioDataDistributor(ILogger? logger = null)
        {
            _logger = logger;
            _cancellationTokenSource = new CancellationTokenSource();

            // 创建无界通道用于音频数据分发，优化延迟
            var options = new UnboundedChannelOptions
            {
                SingleReader = true,   // 只有分发任务读取
                SingleWriter = false,  // 音频回调和其他来源可能写入
                AllowSynchronousContinuations = false // 避免死锁
            };

            _dataChannel = Channel.CreateUnbounded<(byte[], EventHandler<byte[]>?)>(options);
            _writer = _dataChannel.Writer;
            _reader = _dataChannel.Reader;

            // 启动后台分发任务
            _distributionTask = Task.Run(DistributeAudioDataAsync, _cancellationTokenSource.Token);
        }

        /// <summary>
        /// 订阅音频数据
        /// </summary>
        public void Subscribe(EventHandler<byte[]> handler)
        {
            if (handler != null)
            {
                _subscribers.Add(handler);
                _logger?.LogDebug("音频数据订阅者已添加");
            }
        }

        /// <summary>
        /// 取消订阅音频数据
        /// </summary>
        public void Unsubscribe(EventHandler<byte[]> handler)
        {
            // ConcurrentBag 不支持直接移除，需要重建
            var newSubscribers = new List<EventHandler<byte[]>>();
            foreach (var subscriber in _subscribers)
            {
                if (!ReferenceEquals(subscriber, handler))
                {
                    newSubscribers.Add(subscriber);
                }
            }

            // 清空并重新添加
            while (_subscribers.TryTake(out _)) { }
            foreach (var subscriber in newSubscribers)
            {
                _subscribers.Add(subscriber);
            }

            _logger?.LogDebug("音频数据订阅者已移除");
        }

        /// <summary>
        /// 快速分发音频数据（从音频回调调用）
        /// </summary>
        public bool TryDistributeAudioData(byte[] audioData, EventHandler<byte[]>? mainHandler = null)
        {
            if (_disposed) return false;

            // 非阻塞写入，避免影响音频回调性能
            return _writer.TryWrite((audioData, mainHandler));
        }

        /// <summary>
        /// 后台音频数据分发任务
        /// </summary>
        private async Task DistributeAudioDataAsync()
        {
            try
            {
                await foreach (var (data, mainHandler) in _reader.ReadAllAsync(_cancellationTokenSource.Token))
                {
                    try
                    {
                        // 触发主处理器
                        mainHandler?.Invoke(this, data);

                        // 分发给所有订阅者
                        foreach (var subscriber in _subscribers)
                        {
                            try
                            {
                                subscriber?.Invoke(this, data);
                            }
                            catch (Exception ex)
                            {
                                _logger?.LogWarning(ex, "音频数据订阅者处理时出错");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "分发音频数据时出错");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // 正常取消，忽略
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "音频数据分发任务异常");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _writer.TryComplete();
            _cancellationTokenSource.Cancel();

            try
            {
                _distributionTask.Wait(TimeSpan.FromSeconds(1));
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "等待音频分发任务完成时出错");
            }

            _cancellationTokenSource.Dispose();
        }
    }
}