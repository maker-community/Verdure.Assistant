using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Verdure.Assistant.Console.Services.Audio
{
    /// <summary>
    /// 音频解码线程管理器
    /// 负责从 MP3 解码器读取音频数据并缓冲
    /// </summary>
    public class AudioDecodeThread : IDisposable
    {
        private readonly ILogger<AudioDecodeThread> _logger;
        private readonly Mp3Decoder _decoder;
        private readonly AudioBuffer _buffer;
        private Thread? _decodeThread;
        private CancellationTokenSource? _cancellationTokenSource;
        private bool _disposed;
        private bool _isRunning;

        public event EventHandler<DecodeProgressEventArgs>? ProgressUpdated;

        public bool IsRunning => _isRunning;

        public AudioDecodeThread(ILogger<AudioDecodeThread> logger, Mp3Decoder decoder, AudioBuffer buffer)
        {
            _logger = logger;
            _decoder = decoder;
            _buffer = buffer;
        }

        /// <summary>
        /// 开始解码线程
        /// </summary>
        public void Start()
        {
            if (_isRunning || !_decoder.IsLoaded)
            {
                return;
            }

            _cancellationTokenSource = new CancellationTokenSource();
            _decodeThread = new Thread(DecodeLoop)
            {
                Name = "AudioDecodeThread",
                IsBackground = true
            };

            _isRunning = true;
            _decodeThread.Start();
            _logger.LogInformation("音频解码线程已启动");
        }

        /// <summary>
        /// 停止解码线程
        /// </summary>
        public void Stop()
        {
            if (!_isRunning)
            {
                return;
            }

            _cancellationTokenSource?.Cancel();
            _decodeThread?.Join(5000); // 等待 5 秒
            _isRunning = false;
            _logger.LogInformation("音频解码线程已停止");
        }

        /// <summary>
        /// 解码循环
        /// </summary>
        private void DecodeLoop()
        {
            try
            {
                var bufferSize = 2304; // 匹配NLayer的最大输出大小：up to 2,304 elements
                var audioData = new float[bufferSize];
                var totalSamples = 0;

                while (!_cancellationTokenSource!.Token.IsCancellationRequested)
                {
                    // 检查缓冲区是否过满，如果是则等待消费
                    if (_buffer.Count > 30) // 降低缓冲区深度，减少延迟
                    {
                        Thread.Sleep(5); // 等待消费者消费数据
                        continue;
                    }

                    // 从 MP3 解码器读取数据
                    var samplesRead = _decoder.ReadSamples(audioData, 0, bufferSize);
                    
                    if (samplesRead > 0)
                    {
                        // 如果读取到数据，添加到缓冲区
                        var audioChunk = new float[samplesRead];
                        Array.Copy(audioData, 0, audioChunk, 0, samplesRead);
                        
                        if (_buffer.TryEnqueue(audioChunk))
                        {
                            totalSamples += samplesRead;
                            
                            // 触发进度事件
                            var currentPosition = _decoder.GetCurrentPosition();
                            var progress = new DecodeProgressEventArgs(currentPosition, _decoder.Duration, totalSamples);
                            ProgressUpdated?.Invoke(this, progress);
                        }
                        else
                        {
                            // 缓冲区满，稍微等待
                            Thread.Sleep(2);
                        }
                    }
                    else
                    {
                        // 没有更多数据，标记流结束
                        _buffer.SetEndOfStream();
                        break;
                    }

                    // 动态休眠：缓冲区少时快速解码，多时慢速解码
                    var sleepTime = _buffer.Count > 15 ? 3 : 1;
                    Thread.Sleep(sleepTime);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "音频解码线程异常");
            }
            finally
            {
                _buffer.SetEndOfStream();
                _isRunning = false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            _disposed = true;
            Stop();
            _cancellationTokenSource?.Dispose();
        }
    }

    /// <summary>
    /// 解码进度事件参数
    /// </summary>
    public class DecodeProgressEventArgs : EventArgs
    {
        public TimeSpan Position { get; }
        public TimeSpan Duration { get; }
        public int TotalSamples { get; }

        public DecodeProgressEventArgs(TimeSpan position, TimeSpan duration, int totalSamples)
        {
            Position = position;
            Duration = duration;
            TotalSamples = totalSamples;
        }
    }
}
