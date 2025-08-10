using System;
using System.Collections.Concurrent;
using System.Threading;

namespace Verdure.Assistant.Console.Services.Audio
{
    /// <summary>
    /// 音频缓冲区管理器
    /// 用于在解码线程和播放线程之间传递音频数据
    /// </summary>
    public class AudioBuffer : IDisposable
    {
        private readonly ConcurrentQueue<float[]> _bufferQueue;
        private readonly SemaphoreSlim _bufferSemaphore;
        private readonly int _maxBufferCount;
        private bool _disposed;
        private bool _isEndOfStream;

        public AudioBuffer(int maxBufferCount = 50) // 减少默认缓冲区大小
        {
            _bufferQueue = new ConcurrentQueue<float[]>();
            _bufferSemaphore = new SemaphoreSlim(0, maxBufferCount);
            _maxBufferCount = maxBufferCount;
        }

        public bool IsEmpty => _bufferQueue.IsEmpty;
        public int Count => _bufferQueue.Count;
        public bool IsEndOfStream => _isEndOfStream && _bufferQueue.IsEmpty;

    /// <summary>
    /// 添加音频数据到缓冲区
    /// </summary>
    public bool TryEnqueue(float[] audioData)
    {
        if (_disposed || _isEndOfStream) return false;

        if (_bufferQueue.Count >= _maxBufferCount)
        {
            // 缓冲区满，丢弃最旧的数据并减少信号量
            if (_bufferQueue.TryDequeue(out _))
            {
                // 尝试减少信号量计数，防止累积
                if (_bufferSemaphore.CurrentCount > 0)
                {
                    _bufferSemaphore.Wait(0); // 非阻塞减少
                }
            }
        }

        _bufferQueue.Enqueue(audioData);
        
        // 安全地释放信号量，检查是否会超过最大计数
        try
        {
            _bufferSemaphore.Release();
        }
        catch (SemaphoreFullException)
        {
            // 信号量已满，说明消费者跟不上生产者的速度
            // 丢弃当前数据并移除队列中的数据
            _bufferQueue.TryDequeue(out _);
            return false;
        }
        
        return true;
    }        /// <summary>
        /// 从缓冲区获取音频数据
        /// </summary>
        public float[]? TryDequeue(int timeoutMs = 100)
        {
            if (_disposed) return null;

            if (_bufferSemaphore.Wait(timeoutMs))
            {
                if (_bufferQueue.TryDequeue(out var audioData))
                {
                    return audioData;
                }
            }

            return null;
        }

        /// <summary>
        /// 标记流结束
        /// </summary>
        public void SetEndOfStream()
        {
            _isEndOfStream = true;
        }

        /// <summary>
        /// 清空缓冲区
        /// </summary>
        public void Clear()
        {
            while (_bufferQueue.TryDequeue(out _)) { }
            
            // 重置信号量
            while (_bufferSemaphore.CurrentCount > 0)
            {
                _bufferSemaphore.Wait(0);
            }
            
            _isEndOfStream = false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            _disposed = true;
            Clear();
            _bufferSemaphore?.Dispose();
        }
    }
}
