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

                        // 配置音频流参数 - 匹配Core项目的配置
                        var outputParameters = new StreamParameters
                        {
                            device = defaultOutputDevice,
                            channelCount = _channels,
                            sampleFormat = SampleFormat.Int16, // 改为Int16匹配Core项目
                            suggestedLatency = PortAudio.GetDeviceInfo(defaultOutputDevice).defaultLowOutputLatency
                        };

                        // 计算正确的帧大小 - 匹配Core项目的60ms帧
                        int frameSize = sampleRate * 60 / 1000; // 60ms帧，匹配Core项目

                        // 创建输出流
                        _stream = new PortAudioSharp.Stream(
                            null,
                            outputParameters,
                            sampleRate,
                            (uint)frameSize, // 使用计算出的帧大小
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
                        _logger.LogDebug("正在停止 PortAudio 流...");
                        
                        // 使用超时机制避免在某些平台上卡死
                        var stopTask = Task.Run(() =>
                        {
                            _stream.Stop();
                            _stream.Close();
                            _stream.Dispose();
                        });
                        
                        var completed = stopTask.Wait(5000); // 5秒超时
                        
                        if (!completed)
                        {
                            _logger.LogWarning("停止 PortAudio 流超时");
                        }
                        
                        _stream = null;
                        _isPlaying = false;

                        // 释放 PortAudio 引用
                        PortAudioManager.Instance.ReleaseReference();

                        _logger.LogDebug("PortAudio 流已停止");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "停止 PortAudio 流时出现警告");
                        // 确保即使出错也要重置状态
                        _stream = null;
                        _isPlaying = false;
                        try
                        {
                            PortAudioManager.Instance.ReleaseReference();
                        }
                        catch (Exception releaseEx)
                        {
                            _logger.LogWarning(releaseEx, "释放 PortAudio 引用时出错");
                        }
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
                    // 填充静音 - 使用Int16格式
                    var silenceBytes = new byte[frameCount * _channels * sizeof(short)];
                    System.Runtime.InteropServices.Marshal.Copy(silenceBytes, 0, output, silenceBytes.Length);
                    return StreamCallbackResult.Continue;
                }

                var samplesNeeded = (int)frameCount * _channels;
                var outputBuffer = new short[samplesNeeded]; // 改为short数组
                var currentSample = 0;

                // 尝试从缓冲区获取足够的音频数据
                var attempts = 0;
                const int maxAttempts = 10; // 增加尝试次数

                while (currentSample < samplesNeeded && attempts < maxAttempts)
                {
                    var audioData = _audioBuffer.TryDequeue(50); // 增加超时时间
                    if (audioData == null)
                    {
                        attempts++;
                        continue;
                    }

                    // 转换float到short并复制样本
                    var samplesToCopy = Math.Min(audioData.Length, samplesNeeded - currentSample);
                    for (int i = 0; i < samplesToCopy; i++)
                    {
                        // 转换float [-1.0, 1.0] 到 short [-32768, 32767]
                        var floatSample = Math.Max(-1.0f, Math.Min(1.0f, audioData[i]));
                        outputBuffer[currentSample + i] = (short)(floatSample * 32767);
                    }
                    currentSample += samplesToCopy;
                    
                    // 如果这个音频块还有剩余数据，重新计算剩余数据的起始位置
                    // 不要放回缓冲区，直接丢弃以避免重复播放导致的回音
                    if (samplesToCopy < audioData.Length)
                    {
                        _logger?.LogDebug("丢弃了 {Count} 个多余的音频样本", audioData.Length - samplesToCopy);
                    }
                }

                // 如果数据不足，填充剩余部分为静音
                for (int i = currentSample; i < samplesNeeded; i++)
                {
                    outputBuffer[i] = 0;
                }

                // 将数据复制到输出缓冲区
                System.Runtime.InteropServices.Marshal.Copy(outputBuffer, 0, output, outputBuffer.Length);

                return StreamCallbackResult.Continue;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "PortAudio 回调错误");
                
                // 发生错误时填充静音以避免杂音
                try
                {
                    var silenceBytes = new byte[frameCount * _channels * sizeof(short)];
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
