using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using PortAudioSharp;
using XiaoZhi.Core.Services;
using XiaoZhi.Core.Interfaces;
using XiaoZhiSharp.Utils;

namespace XiaoZhiSharp.Services
{
    public class AudioService : IDisposable
    {
        // 音频编解码器
        private readonly IAudioCodec audioCodec;
        
        // 音频输出相关组件
        private readonly PortAudioSharp.Stream? _waveOut;
        private readonly ConcurrentQueue<byte[]> _audioPlayQueue = new ConcurrentQueue<byte[]>();

        // 音频输入相关组件
        private readonly PortAudioSharp.Stream? _waveIn;

        // 音频参数 - 匹配Python配置
        private const int SampleRate = 24000;  // 输出采样率24kHz
        private const int InputSampleRate = 16000;  // 输入采样率16kHz
        private const int Channels = 1;
        private const int FrameDuration = 60;  // 60ms帧
        private const int OutputFrameSize = SampleRate * FrameDuration / 1000; // 1440 samples
        private const int InputFrameSize = InputSampleRate * FrameDuration / 1000; // 960 samples

        // Opus 数据包缓存池
        private readonly ConcurrentQueue<byte[]> _opusRecordPackets = new ConcurrentQueue<byte[]>();
        private readonly ConcurrentQueue<byte[]> _opusPlayPackets = new ConcurrentQueue<byte[]>();

        // 同步对象
        private readonly object _playbackLock = new object();
        private volatile bool _isPlaybackActive = false;

        public bool IsRecording { get; private set; }
        public bool IsPlaying { get; private set; }

        public AudioService()
        {
            // 初始化音频编解码器
            audioCodec = new OpusAudioCodec();

            // 初始化 PortAudio
            PortAudio.Initialize();
            
            // 初始化音频输出组件
            int outputDeviceIndex = PortAudio.DefaultOutputDevice;
            if (outputDeviceIndex == PortAudio.NoDevice)
            {
                Console.WriteLine("No default output device found");
                LogConsole.InfoLine(PortAudio.VersionInfo.versionText);
                LogConsole.WriteLine($"Number of devices: {PortAudio.DeviceCount}");
                for (int i = 0; i != PortAudio.DeviceCount; ++i)
                {
                    LogConsole.WriteLine($" Device {i}");
                    DeviceInfo deviceInfo = PortAudio.GetDeviceInfo(i);
                    LogConsole.WriteLine($"   Name: {deviceInfo.name}");
                    LogConsole.WriteLine($"   Max output channels: {deviceInfo.maxOutputChannels}");
                    LogConsole.WriteLine($"   Default sample rate: {deviceInfo.defaultSampleRate}");
                }
                return;
            }

            var outputInfo = PortAudio.GetDeviceInfo(outputDeviceIndex);
            var outparam = new StreamParameters
            {
                device = outputDeviceIndex,
                channelCount = Channels,
                sampleFormat = SampleFormat.Int16,  // 使用Int16格式匹配PCM数据
                suggestedLatency = outputInfo.defaultLowOutputLatency,
                hostApiSpecificStreamInfo = IntPtr.Zero
            };

            _waveOut = new PortAudioSharp.Stream(
                inParams: null, 
                outParams: outparam, 
                sampleRate: SampleRate, 
                framesPerBuffer: OutputFrameSize,  // 使用正确的帧大小
                streamFlags: StreamFlags.ClipOff, 
                callback: PlayCallback, 
                userData: IntPtr.Zero
            );

            // 初始化音频输入组件
            int inputDeviceIndex = PortAudio.DefaultInputDevice;
            if (inputDeviceIndex == PortAudio.NoDevice)
            {
                Console.WriteLine("No default input device found");
                return;
            }
            
            var inputInfo = PortAudio.GetDeviceInfo(inputDeviceIndex);
            var inparam = new StreamParameters
            {
                device = inputDeviceIndex,
                channelCount = Channels,
                sampleFormat = SampleFormat.Int16,  // 输入也使用Int16
                suggestedLatency = inputInfo.defaultLowInputLatency,
                hostApiSpecificStreamInfo = IntPtr.Zero
            };

            _waveIn = new PortAudioSharp.Stream(
                inParams: inparam, 
                outParams: null, 
                sampleRate: InputSampleRate, 
                framesPerBuffer: InputFrameSize,  // 使用输入帧大小
                streamFlags: StreamFlags.ClipOff, 
                callback: InCallback, 
                userData: IntPtr.Zero
            );

            // 启动 Opus 数据解码线程
            Thread threadOpus = new Thread(() =>
            {
                while (true)
                {
                    if (_opusPlayPackets.TryDequeue(out var opusData))
                    {
                        AddOutStreamSamples(opusData);
                    }
                    Thread.Sleep(1);
                }
            });
            threadOpus.Start();

            LogConsole.InfoLine($"当前默认音频输入设备： {inputDeviceIndex} ({inputInfo.name})");
            LogConsole.InfoLine($"当前默认音频输出设备 {outputDeviceIndex} ({outputInfo.name})");
        }

        private StreamCallbackResult PlayCallback(
            IntPtr input, IntPtr output, uint frameCount, ref StreamCallbackTimeInfo timeInfo,
            StreamCallbackFlags statusFlags, IntPtr userData)
        {
            try
            {
                // 检查是否有音频数据
                if (_audioPlayQueue.TryDequeue(out byte[]? audioData))
                {
                    // 确保数据长度正确
                    int expectedBytes = (int)(frameCount * Channels * 2); // Int16 = 2 bytes per sample
                    
                    if (audioData.Length >= expectedBytes)
                    {
                        // 直接复制PCM数据到输出缓冲区
                        Marshal.Copy(audioData, 0, output, expectedBytes);
                    }
                    else
                    {
                        // 数据不够，复制现有数据并填充静音
                        Marshal.Copy(audioData, 0, output, audioData.Length);
                        
                        // 填充剩余部分为静音
                        int remainingBytes = expectedBytes - audioData.Length;
                        byte[] silence = new byte[remainingBytes];
                        Marshal.Copy(silence, 0, IntPtr.Add(output, audioData.Length), remainingBytes);
                    }
                    
                    _isPlaybackActive = true;
                    return StreamCallbackResult.Continue;
                }
                else
                {
                    // 没有数据，输出静音
                    int silenceBytes = (int)(frameCount * Channels * 2);
                    byte[] silence = new byte[silenceBytes];
                    Marshal.Copy(silence, 0, output, silenceBytes);
                    
                    _isPlaybackActive = false;
                    return StreamCallbackResult.Continue;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"播放回调错误: {ex.Message}");
                return StreamCallbackResult.Continue;
            }
        }

        private StreamCallbackResult InCallback(
            IntPtr input, IntPtr output, uint frameCount, ref StreamCallbackTimeInfo timeInfo,
            StreamCallbackFlags statusFlags, IntPtr userData)
        {
            try
            {
                // 计算数据大小
                int dataSize = (int)(frameCount * Channels * 2); // Int16 = 2 bytes per sample
                byte[] buffer = new byte[dataSize];

                // 从输入缓冲区复制数据
                Marshal.Copy(input, buffer, 0, dataSize);

                // 处理录制的音频数据
                AddRecordSamples(buffer, dataSize);

                return StreamCallbackResult.Continue;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"录制回调错误: {ex.Message}");
                return StreamCallbackResult.Continue;
            }
        }

        private void AddRecordSamples(byte[] buffer, int bytesRecorded)
        {
            try
            {
                // 编码 PCM 数据为 Opus
                byte[] opusData = audioCodec.Encode(buffer, InputSampleRate, Channels);
                
                if (opusData.Length > 0)
                {
                    _opusRecordPackets.Enqueue(opusData);
                }
            }
            catch (Exception ex)
            {
                LogConsole.ErrorLine($"录制编码错误: {ex.Message}");
            }
        }

        public void AddOutStreamSamples(byte[] opusData)
        {
            if (opusData == null || opusData.Length == 0)
                return;

            try
            {
                // 解码 Opus 数据为 PCM
                byte[] pcmData = audioCodec.Decode(opusData, SampleRate, Channels);

                if (pcmData.Length > 0)
                {
                    // 将PCM数据添加到播放队列
                    _audioPlayQueue.Enqueue(pcmData);

                    // 如果队列中有足够的数据且未开始播放，则启动播放
                    if (_audioPlayQueue.Count > 2 && !IsPlaying)
                    {
                        StartPlaying();
                    }
                }
            }
            catch (Exception ex)
            {
                LogConsole.ErrorLine($"解码Opus数据错误: {ex.Message}");
            }
        }

        public void StartRecording()
        {
            if (!IsRecording && _waveIn != null)
            {
                try
                {
                    _waveIn.Start();
                    IsRecording = true;
                    LogConsole.InfoLine("开始录制音频");
                }
                catch (Exception ex)
                {
                    LogConsole.ErrorLine($"启动录制失败: {ex.Message}");
                }
            }
        }

        public void StopRecording()
        {
            if (IsRecording && _waveIn != null)
            {
                try
                {
                    _waveIn.Stop();
                    IsRecording = false;
                    LogConsole.InfoLine("停止录制音频");
                }
                catch (Exception ex)
                {
                    LogConsole.ErrorLine($"停止录制失败: {ex.Message}");
                }
            }
        }

        public void StartPlaying()
        {
            if (!IsPlaying && _waveOut != null)
            {
                try
                {
                    _waveOut.Start();
                    IsPlaying = true;
                    LogConsole.InfoLine("开始播放音频");
                }
                catch (Exception ex)
                {
                    LogConsole.ErrorLine($"启动播放失败: {ex.Message}");
                }
            }
        }

        public void StopPlaying()
        {
            if (IsPlaying && _waveOut != null)
            {
                try
                {
                    _waveOut.Stop();
                    IsPlaying = false;
                    LogConsole.InfoLine("停止播放音频");
                }
                catch (Exception ex)
                {
                    LogConsole.ErrorLine($"停止播放失败: {ex.Message}");
                }
            }
        }

        public void OpusPlayEnqueue(byte[] opusData)
        {
            _opusPlayPackets.Enqueue(opusData);
        }

        public bool OpusRecordEnqueue(out byte[]? opusData)
        {
            return _opusRecordPackets.TryDequeue(out opusData);
        }

        public void Dispose()
        {
            IsPlaying = false;
            IsRecording = false;
            
            try
            {
                audioCodec?.Dispose();
                _waveIn?.Dispose();
                _waveOut?.Dispose();
                PortAudio.Terminate();
            }
            catch (Exception ex)
            {
                LogConsole.ErrorLine($"释放音频资源时出错: {ex.Message}");
            }
        }
    }
}
