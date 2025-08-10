using PortAudioSharp;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Console.Services.Audio;
using Verdure.Assistant.Core.Services;

namespace Verdure.Assistant.Console.Audio
{
    public class PortAudioPlayer : IDisposable
    {
        private readonly ILogger<PortAudioPlayer> _logger;
        private PortAudioSharp.Stream? _stream;
        private bool _isPlaying;
        private AudioBuffer? _audioBuffer;
        private readonly object _lock = new object();
        private int _sampleRate;
        private int _channels;

        public PortAudioPlayer(ILogger<PortAudioPlayer> logger)
        {
            _logger = logger;
            _logger.LogDebug("PortAudioPlayer 初始化");
        }

        public async Task StartAsync(AudioBuffer audioBuffer, int sampleRate)
        {
            await Task.Run(() =>
            {
                lock (_lock)
                {
                    if (_isPlaying)
                        return;

                    _audioBuffer = audioBuffer;
                    _sampleRate = sampleRate;
                    _channels = 2; // 假设立体声

                    try
                    {
                        // 使用 PortAudioManager 确保正确初始化
                        if (!PortAudioManager.Instance.AcquireReference())
                        {
                            throw new InvalidOperationException("无法初始化 PortAudio");
                        }

                        // 获取默认输出设备
                        var defaultOutputDevice = PortAudio.DefaultOutputDevice;
                        if (defaultOutputDevice == -1)
                        {
                            PortAudioManager.Instance.ReleaseReference();
                            throw new InvalidOperationException("未找到音频输出设备");
                        }

                        // 配置音频流参数
                        var outputParameters = new StreamParameters
                        {
                            device = defaultOutputDevice,
                            channelCount = _channels,
                            sampleFormat = SampleFormat.Float32,
                            suggestedLatency = PortAudio.GetDeviceInfo(defaultOutputDevice).defaultLowOutputLatency
                        };

                        // 创建输出流
                        _stream = new PortAudioSharp.Stream(
                            null,
                            outputParameters,
                            sampleRate,
                            1024, // 帧大小
                            StreamFlags.ClipOff,
                            AudioCallback,
                            IntPtr.Zero);

                        _stream.Start();
                        _isPlaying = true;
                        
                        var deviceInfo = PortAudio.GetDeviceInfo(defaultOutputDevice);
                        _logger.LogInformation($"PortAudio 流启动成功，设备: {deviceInfo.name}, 采样率: {sampleRate}Hz");
                    }
                    catch (Exception ex)
                    {
                        PortAudioManager.Instance.ReleaseReference();
                        throw new InvalidOperationException($"启动 PortAudio 流失败: {ex.Message}", ex);
                    }
                }
            });
        }

        public async Task StopAsync()
        {
            await Task.Run(() =>
            {
                lock (_lock)
                {
                    if (!_isPlaying || _stream == null)
                        return;

                    try
                    {
                        _stream.Stop();
                        _stream.Close();
                        _stream.Dispose();
                        _stream = null;
                        _isPlaying = false;

                        // 释放 PortAudio 引用
                        PortAudioManager.Instance.ReleaseReference();

                        _logger.LogDebug("PortAudio 流已停止");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "停止 PortAudio 流时出现警告");
                    }
                }
            });
        }

        private StreamCallbackResult AudioCallback(
            IntPtr input,
            IntPtr output,
            uint frameCount,
            ref StreamCallbackTimeInfo timeInfo,
            StreamCallbackFlags statusFlags,
            IntPtr userData)
        {
            try
            {
                if (_audioBuffer == null || output == IntPtr.Zero)
                {
                    // 填充静音
                    var silenceBytes = new byte[frameCount * _channels * sizeof(float)];
                    System.Runtime.InteropServices.Marshal.Copy(silenceBytes, 0, output, silenceBytes.Length);
                    return StreamCallbackResult.Continue;
                }

                var samplesNeeded = (int)frameCount * _channels;
                var outputBuffer = new float[samplesNeeded];
                var currentSample = 0;

                // 尝试从缓冲区获取足够的音频数据
                var attempts = 0;
                const int maxAttempts = 5; // 限制尝试次数，避免卡顿

                while (currentSample < samplesNeeded && attempts < maxAttempts)
                {
                    var audioData = _audioBuffer.TryDequeue(1); // 1ms 超时，快速响应
                    if (audioData == null)
                    {
                        attempts++;
                        continue;
                    }

                    // 复制可用的样本
                    var samplesToCopy = Math.Min(audioData.Length, samplesNeeded - currentSample);
                    Array.Copy(audioData, 0, outputBuffer, currentSample, samplesToCopy);
                    currentSample += samplesToCopy;
                    
                    // 如果这个音频块还有剩余数据，放回缓冲区
                    if (samplesToCopy < audioData.Length)
                    {
                        var remaining = new float[audioData.Length - samplesToCopy];
                        Array.Copy(audioData, samplesToCopy, remaining, 0, remaining.Length);
                        _audioBuffer.TryEnqueue(remaining); // 放回剩余数据
                    }
                }

                // 如果数据不足，填充剩余部分为静音
                for (int i = currentSample; i < samplesNeeded; i++)
                {
                    outputBuffer[i] = 0.0f;
                }

                // 将数据复制到输出缓冲区
                System.Runtime.InteropServices.Marshal.Copy(outputBuffer, 0, output, outputBuffer.Length);

                // 如果获取到了足够数据，继续播放
                return currentSample >= samplesNeeded / 2 ? StreamCallbackResult.Continue : StreamCallbackResult.Continue;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PortAudio 回调错误");
                
                // 发生错误时填充静音，避免杂音
                try
                {
                    var silenceBytes = new byte[frameCount * _channels * sizeof(float)];
                    System.Runtime.InteropServices.Marshal.Copy(silenceBytes, 0, output, silenceBytes.Length);
                }
                catch { }
                
                return StreamCallbackResult.Continue;
            }
        }

        public bool IsPlaying
        {
            get
            {
                lock (_lock)
                {
                    return _isPlaying;
                }
            }
        }

        public void Dispose()
        {
            StopAsync().Wait();
        }
    }
}
