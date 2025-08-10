using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NLayer;

namespace Verdure.Assistant.Console.Services.Audio
{
    /// <summary>
    /// MP3 解码器，使用 NLayer 解码 MP3 文件到 PCM 数据
    /// </summary>
    public class Mp3Decoder : IDisposable
    {
        private readonly ILogger<Mp3Decoder> _logger;
        private readonly HttpClient _httpClient;
        private MpegFile? _mpegFile;
        private Stream? _audioStream;
        private bool _disposed;

        public int SampleRate { get; private set; }
        public int Channels { get; private set; }
        public TimeSpan Duration { get; private set; }
        public bool IsLoaded { get; private set; }

        public Mp3Decoder(ILogger<Mp3Decoder> logger)
        {
            _logger = logger;
            _httpClient = new HttpClient();
        }

        /// <summary>
        /// 从文件加载 MP3
        /// </summary>
        public async Task LoadFromFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("加载 MP3 文件: {FilePath}", filePath);
                
                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException($"音频文件不存在: {filePath}");
                }

                var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                await LoadFromStreamAsync(fileStream, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载 MP3 文件失败: {FilePath}", filePath);
                throw;
            }
        }

        /// <summary>
        /// 从 URL 加载 MP3 流
        /// </summary>
        public async Task LoadFromUrlAsync(string url, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("加载 MP3 流: {Url}", url);
                
                var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                
                var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await LoadFromStreamAsync(stream, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "加载 MP3 流失败: {Url}", url);
                throw;
            }
        }

        /// <summary>
        /// 从流加载 MP3
        /// </summary>
        private async Task LoadFromStreamAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            try
            {
                Reset();
                _audioStream = stream;
                _mpegFile = new MpegFile(stream);

                // 获取音频属性
                SampleRate = _mpegFile.SampleRate;
                Channels = _mpegFile.Channels;
                Duration = _mpegFile.Duration;
                IsLoaded = true;

                _logger.LogInformation("MP3 解码器初始化完成 - 采样率: {SampleRate}Hz, 声道: {Channels}, 时长: {Duration}", 
                    SampleRate, Channels, Duration);

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "初始化 MP3 解码器失败");
                Reset();
                throw;
            }
        }

        /// <summary>
        /// 读取音频数据到 float 数组
        /// </summary>
        public int ReadSamples(float[] buffer, int offset, int count)
        {
            if (!IsLoaded || _mpegFile is null)
            {
                return 0;
            }

            try
            {
                return _mpegFile.ReadSamples(buffer, offset, count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "读取音频采样失败");
                return 0;
            }
        }

        /// <summary>
        /// 跳转到指定时间位置
        /// </summary>
        public void SeekTo(TimeSpan position)
        {
            if (!IsLoaded || _mpegFile is null)
            {
                return;
            }

            try
            {
                _mpegFile.Time = position;
                _logger.LogDebug("跳转到位置: {Position}", position);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "跳转失败: {Position}", position);
            }
        }

        /// <summary>
        /// 获取当前播放位置
        /// </summary>
        public TimeSpan GetCurrentPosition()
        {
            return _mpegFile?.Time ?? TimeSpan.Zero;
        }

        /// <summary>
        /// 重置解码器状态
        /// </summary>
        private void Reset()
        {
            _mpegFile?.Dispose();
            _mpegFile = null;
            _audioStream?.Dispose();
            _audioStream = null;
            IsLoaded = false;
            SampleRate = 0;
            Channels = 0;
            Duration = TimeSpan.Zero;
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            _disposed = true;
            Reset();
            _httpClient?.Dispose();
        }
    }
}
