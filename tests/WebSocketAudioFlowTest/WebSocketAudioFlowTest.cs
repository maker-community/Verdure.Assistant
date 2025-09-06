using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Services;
using Verdure.Assistant.Core.Interfaces;

namespace Verdure.Assistant.Tests
{
    /// <summary>
    /// WebSocket音频流程测试
    /// 模拟完整的WebSocket音频接收→解码→播放流程
    /// </summary>
    public class WebSocketAudioFlowTest
    {
        private readonly ILogger<WebSocketAudioFlowTest> _logger;
        private readonly OpusSharpAudioCodec _audioCodec;
        private readonly SoundFlowAudioPlayer _audioPlayer;

        public WebSocketAudioFlowTest()
        {
            var loggerFactory = LoggerFactory.Create(builder => 
                builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
            
            _logger = loggerFactory.CreateLogger<WebSocketAudioFlowTest>();
            _audioCodec = new OpusSharpAudioCodec();
            _audioPlayer = new SoundFlowAudioPlayer(loggerFactory.CreateLogger<SoundFlowAudioPlayer>());
        }

        /// <summary>
        /// 测试完整的音频流程：生成→编码→解码→播放
        /// </summary>
        public async Task TestCompleteAudioFlow()
        {
            _logger.LogInformation("开始WebSocket音频流程测试");

            try
            {
                // 1. 生成多帧测试音频数据（模拟真实WebSocket流）
                var frameCount = 10; // 10帧，每帧60ms
                var frameDurationMs = 60;
                var sampleRate = 16000;
                var channels = 1;
                
                _logger.LogInformation("生成 {FrameCount} 帧音频数据，每帧 {Duration}ms", frameCount, frameDurationMs);

                // 模拟WebSocket音频流处理
                for (int frame = 0; frame < frameCount; frame++)
                {
                    // 生成一帧音频数据
                    var frameDurationSeconds = frameDurationMs / 1000.0;
                    var testPcmData = GenerateTestAudio(sampleRate, channels, frameDurationSeconds, 440.0 + frame * 50); // 不同频率
                    _logger.LogDebug("帧 {Frame}: 生成PCM数据 {Length}字节", frame + 1, testPcmData.Length);

                    // 编码为Opus格式（模拟WebSocket发送端）
                    var encodedData = _audioCodec.Encode(testPcmData, sampleRate, channels);
                    _logger.LogDebug("帧 {Frame}: Opus编码 {Length}字节", frame + 1, encodedData.Length);

                    if (encodedData.Length == 0)
                    {
                        _logger.LogError("帧 {Frame}: 编码失败", frame + 1);
                        continue;
                    }

                    // 解码Opus数据（模拟WebSocket接收端）
                    var decodedPcmData = _audioCodec.Decode(encodedData, sampleRate, channels);
                    _logger.LogDebug("帧 {Frame}: Opus解码 {Length}字节", frame + 1, decodedPcmData.Length);

                    if (decodedPcmData.Length == 0)
                    {
                        _logger.LogError("帧 {Frame}: 解码失败", frame + 1);
                        continue;
                    }

                    // 播放解码后的数据（模拟VoiceChatService.HandleAudioDataReceived）
                    await _audioPlayer.PlayAsync(decodedPcmData, sampleRate, channels);
                    _logger.LogDebug("帧 {Frame}: 已提交播放", frame + 1);

                    // 模拟网络延迟
                    await Task.Delay(20);
                }

                _logger.LogInformation("所有音频帧已提交播放，等待播放完成...");

                // 等待播放完成
                var playbackTimeout = TimeSpan.FromSeconds(frameCount * frameDurationMs / 1000.0 + 2); // 加2秒缓冲
                var startTime = DateTime.Now;
                
                while (_audioPlayer.IsPlaying && (DateTime.Now - startTime) < playbackTimeout)
                {
                    await Task.Delay(100);
                    _logger.LogDebug("播放中... ({ElapsedSeconds:F1}s)", (DateTime.Now - startTime).TotalSeconds);
                }

                if (_audioPlayer.IsPlaying)
                {
                    _logger.LogWarning("播放超时，强制停止");
                    await _audioPlayer.StopAsync();
                }
                else
                {
                    _logger.LogInformation("播放完成");
                }

                _logger.LogInformation("WebSocket音频流程测试完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "音频流程测试失败");
                throw;
            }
        }

        /// <summary>
        /// 生成测试音频数据（正弦波）
        /// </summary>
        private byte[] GenerateTestAudio(int sampleRate, int channels, double durationSeconds, double frequency = 440.0)
        {
            var sampleCount = (int)(sampleRate * durationSeconds * channels);
            var audioData = new byte[sampleCount * 2]; // 16位 = 2字节/样本

            for (int i = 0; i < sampleCount; i++)
            {
                // 生成正弦波
                var time = (double)i / sampleRate;
                var amplitude = Math.Sin(2 * Math.PI * frequency * time);
                
                // 转换为16位PCM
                var sample = (short)(amplitude * 16000); // 适中的音量
                var bytes = BitConverter.GetBytes(sample);
                
                audioData[i * 2] = bytes[0];
                audioData[i * 2 + 1] = bytes[1];
            }

            return audioData;
        }

        public void Dispose()
        {
            _audioPlayer?.Dispose();
            _audioCodec?.Dispose();
        }
    }
}
