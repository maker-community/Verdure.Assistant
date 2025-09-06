using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Verdure.Assistant.Core.Services
{
    /// <summary>
    /// WebSocket 音频数据处理器 - 使用 System.Threading.Channels 优化
    /// 提供高性能的 WebSocket 音频数据发送和接收缓冲
    /// 解决 WebSocket 音频传输中的性能瓶颈和背压问题
    /// </summary>
    public class WebSocketAudioHandler : IDisposable
    {
        private readonly Channel<byte[]> _outgoingAudioChannel;
        private readonly ChannelWriter<byte[]> _outgoingWriter;
        private readonly ChannelReader<byte[]> _outgoingReader;
        
        private readonly Channel<byte[]> _incomingAudioChannel;
        private readonly ChannelWriter<byte[]> _incomingWriter;
        private readonly ChannelReader<byte[]> _incomingReader;
        
        private readonly Task _processingTask;
        private readonly CancellationTokenSource _cancellationTokenSource;
        private readonly ILogger? _logger;
        private bool _disposed;

        // 回调函数用于实际的 WebSocket 发送
        private readonly Func<byte[], CancellationToken, Task>? _sendAudioCallback;

        // 音频数据接收事件
        public event EventHandler<byte[]>? AudioDataReceived;

        public WebSocketAudioHandler(
            Func<byte[], CancellationToken, Task>? sendAudioCallback = null,
            ILogger? logger = null,
            int outgoingBufferSize = 50,
            int incomingBufferSize = 100)
        {
            _logger = logger;
            _sendAudioCallback = sendAudioCallback;
            _cancellationTokenSource = new CancellationTokenSource();

            // 创建发送音频数据的有界通道
            var outgoingOptions = new BoundedChannelOptions(outgoingBufferSize)
            {
                FullMode = BoundedChannelFullMode.DropOldest, // 满时丢弃最旧数据，避免延迟
                SingleReader = true,   // 只有处理任务读取
                SingleWriter = false,  // 多个音频源可能写入
                AllowSynchronousContinuations = false
            };
            
            _outgoingAudioChannel = Channel.CreateBounded<byte[]>(outgoingOptions);
            _outgoingWriter = _outgoingAudioChannel.Writer;
            _outgoingReader = _outgoingAudioChannel.Reader;

            // 创建接收音频数据的有界通道
            var incomingOptions = new BoundedChannelOptions(incomingBufferSize)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = false,  // 可能有多个消费者
                SingleWriter = true,   // 只有 WebSocket 接收线程写入
                AllowSynchronousContinuations = false
            };
            
            _incomingAudioChannel = Channel.CreateBounded<byte[]>(incomingOptions);
            _incomingWriter = _incomingAudioChannel.Writer;
            _incomingReader = _incomingAudioChannel.Reader;

            // 启动后台处理任务
            _processingTask = Task.Run(ProcessAudioDataAsync, _cancellationTokenSource.Token);
        }

        /// <summary>
        /// 队列音频数据发送
        /// </summary>
        public bool TryQueueAudioForSending(byte[] audioData)
        {
            if (_disposed) return false;

            return _outgoingWriter.TryWrite(audioData);
        }

        /// <summary>
        /// 异步队列音频数据发送
        /// </summary>
        public async ValueTask<bool> QueueAudioForSendingAsync(byte[] audioData, CancellationToken cancellationToken = default)
        {
            if (_disposed) return false;

            try
            {
                return await _outgoingWriter.WaitToWriteAsync(cancellationToken) && _outgoingWriter.TryWrite(audioData);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        /// <summary>
        /// 处理接收到的音频数据
        /// </summary>
        public bool TryProcessReceivedAudio(byte[] audioData)
        {
            if (_disposed) return false;

            return _incomingWriter.TryWrite(audioData);
        }

        /// <summary>
        /// 异步获取接收到的音频数据
        /// </summary>
        public async ValueTask<byte[]?> GetReceivedAudioAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed) return null;

            try
            {
                return await _incomingReader.ReadAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        /// <summary>
        /// 尝试获取接收到的音频数据（非阻塞）
        /// </summary>
        public byte[]? TryGetReceivedAudio()
        {
            if (_disposed) return null;

            return _incomingReader.TryRead(out var audioData) ? audioData : null;
        }

        /// <summary>
        /// 后台音频数据处理任务
        /// </summary>
        private async Task ProcessAudioDataAsync()
        {
            try
            {
                // 同时处理发送和接收的任务
                var sendingTask = ProcessOutgoingAudioAsync();
                var receivingTask = ProcessIncomingAudioAsync();

                await Task.WhenAll(sendingTask, receivingTask);
            }
            catch (OperationCanceledException)
            {
                // 正常取消，忽略
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "WebSocket音频处理任务异常");
            }
        }

        /// <summary>
        /// 处理发送音频数据
        /// </summary>
        private async Task ProcessOutgoingAudioAsync()
        {
            await foreach (var audioData in _outgoingReader.ReadAllAsync(_cancellationTokenSource.Token))
            {
                try
                {
                    if (_sendAudioCallback != null)
                    {
                        await _sendAudioCallback(audioData, _cancellationTokenSource.Token);
                        _logger?.LogDebug("已发送WebSocket音频数据，长度: {Length}", audioData.Length);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "发送WebSocket音频数据时出错");
                }
            }
        }

        /// <summary>
        /// 处理接收音频数据
        /// </summary>
        private async Task ProcessIncomingAudioAsync()
        {
            await foreach (var audioData in _incomingReader.ReadAllAsync(_cancellationTokenSource.Token))
            {
                try
                {
                    AudioDataReceived?.Invoke(this, audioData);
                    _logger?.LogDebug("处理接收到的WebSocket音频数据，长度: {Length}", audioData.Length);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "处理接收到的WebSocket音频数据时出错");
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // 完成写入器
            _outgoingWriter.TryComplete();
            _incomingWriter.TryComplete();

            // 取消处理任务
            _cancellationTokenSource.Cancel();

            try
            {
                _processingTask.Wait(TimeSpan.FromSeconds(1));
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "等待WebSocket音频处理任务完成时出错");
            }

            _cancellationTokenSource.Dispose();
        }
    }
}