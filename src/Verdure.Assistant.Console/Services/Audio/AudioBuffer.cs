using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Channels;

namespace Verdure.Assistant.Console.Services.Audio
{
    /// <summary>
    /// 音频缓冲区管理器 - 使用 System.Threading.Channels 优化
    /// 用于在解码线程和播放线程之间传递音频数据
    /// 提供更好的性能、流控制和内存效率
    /// </summary>
    public class AudioBuffer : IDisposable
    {
        private readonly Channel<float[]> _channel;
        private readonly ChannelWriter<float[]> _writer;
        private readonly ChannelReader<float[]> _reader;
        private readonly int _maxBufferCount;
        private bool _disposed;
        private bool _isEndOfStream;

        public AudioBuffer(int maxBufferCount = 30) // 减少默认缓冲区大小降低延迟
        {
            _maxBufferCount = maxBufferCount;
            
            // 创建有界通道，支持流控制和背压处理
            var options = new BoundedChannelOptions(maxBufferCount)
            {
                FullMode = BoundedChannelFullMode.DropOldest, // 满时丢弃最旧数据
                SingleReader = true,   // 优化：只有一个消费者
                SingleWriter = false,  // 可能有多个生产者
                AllowSynchronousContinuations = false // 提高性能，避免死锁
            };
            
            _channel = Channel.CreateBounded<float[]>(options);
            _writer = _channel.Writer;
            _reader = _channel.Reader;
        }

        public bool IsEmpty => !_reader.TryPeek(out _);
        public int Count => _reader.CanCount ? _reader.Count : 0;
        public bool IsEndOfStream => _isEndOfStream && IsEmpty;

    /// <summary>
    /// 添加音频数据到缓冲区 - 使用 Channel 的异步写入
    /// </summary>
    public bool TryEnqueue(float[] audioData)
    {
        if (_disposed || _isEndOfStream) return false;

        // 使用 Channel 的 TryWrite，内置流控制和背压处理
        return _writer.TryWrite(audioData);
    }

    /// <summary>
    /// 异步添加音频数据到缓冲区
    /// </summary>
    public async ValueTask<bool> EnqueueAsync(float[] audioData, CancellationToken cancellationToken = default)
    {
        if (_disposed || _isEndOfStream) return false;

        try
        {
            return await _writer.WaitToWriteAsync(cancellationToken) && _writer.TryWrite(audioData);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }        /// <summary>
        /// 从缓冲区获取音频数据
        /// </summary>
        public float[]? TryDequeue(int timeoutMs = 100)
        {
            if (_disposed) return null;

            // 直接使用 Channel 的 TryRead，性能更好
            return _reader.TryRead(out var audioData) ? audioData : null;
        }

        /// <summary>
        /// 异步从缓冲区获取音频数据
        /// </summary>
        public async ValueTask<float[]?> DequeueAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed) return null;

            try
            {
                return await _reader.ReadAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        /// <summary>
        /// 等待数据可用，带超时
        /// </summary>
        public async ValueTask<float[]?> TryDequeueAsync(int timeoutMs = 100)
        {
            if (_disposed) return null;

            using var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                return await _reader.ReadAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
        }

        /// <summary>
        /// 标记流结束
        /// </summary>
        public void SetEndOfStream()
        {
            _isEndOfStream = true;
            // 完成写入器，通知消费者没有更多数据
            _writer.TryComplete();
        }

        /// <summary>
        /// 清空缓冲区
        /// </summary>
        public void Clear()
        {
            // Channel 无法直接清空，需要消费所有现有数据
            while (_reader.TryRead(out _)) { }
            
            _isEndOfStream = false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            _disposed = true;
            _writer.TryComplete();
            // Channel 会在完成时自动清理资源
        }
    }
}
