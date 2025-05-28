using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using PortAudioSharp;
using XiaoZhi.Core.Services;
using XiaoZhi.Core.Interfaces;
using XiaoZhiSharp.Utils;
using System.Collections.Concurrent;
using Newtonsoft.Json;

namespace XiaoZhiSharp.Services
{
    public class AudioService : IDisposable
    {
        // 音频编解码器
        private readonly IAudioCodec audioCodec;
        
        // 音频输出相关组件
        private readonly PortAudioSharp.Stream? _waveOut;
        private readonly ConcurrentQueue<float[]> _waveOutStream = new ConcurrentQueue<float[]>();

        // 音频输入相关组件
        private readonly PortAudioSharp.Stream? _waveIn;        // 音频参数 - 匹配Python配置
        private const int InputSampleRate = 16000;  // 输入采样率16kHz
        private const int OutputSampleRate = 24000; // 输出采样率24kHz
        private const int Channels = 1;
        private const int FrameDuration = 60;  // 60ms帧
        private const int InputFrameSize = InputSampleRate * FrameDuration / 1000; // 960 samples
        private const int OutputFrameSize = OutputSampleRate * FrameDuration / 1000; // 1440 samples

        // Opus 数据包缓存池
        private readonly Queue<byte[]> _opusRecordPackets = new Queue<byte[]>();
        private readonly Queue<byte[]> _opusPlayPackets = new Queue<byte[]>();

        public bool IsRecording { get; private set; }
        public bool IsPlaying { get; private set; }        public AudioService()
        {
            // 初始化音频编解码器 - 暂时使用Concentus进行测试
            audioCodec = new OpusAudioCodec();

            // 初始化音频输出组件
            PortAudio.Initialize();
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
                    LogConsole.WriteLine($"   Max input channels: {deviceInfo.maxInputChannels}");
                    LogConsole.WriteLine($"   Default sample rate: {deviceInfo.defaultSampleRate}");
                }
            }
            var outputInfo = PortAudio.GetDeviceInfo(outputDeviceIndex);
            var outparam = new StreamParameters
            {
                device = outputDeviceIndex,
                channelCount = Channels,
                sampleFormat = SampleFormat.Float32,
                suggestedLatency = outputInfo.defaultLowOutputLatency,
                hostApiSpecificStreamInfo = IntPtr.Zero
            };            _waveOut = new PortAudioSharp.Stream(
                inParams: null, outParams: outparam, sampleRate: OutputSampleRate, framesPerBuffer: (uint)OutputFrameSize,
                streamFlags: StreamFlags.ClipOff, callback: PlayCallback, userData: IntPtr.Zero
            );

            // 初始化音频输入组件
            int inputDeviceIndex = PortAudio.DefaultInputDevice;
            if (inputDeviceIndex == PortAudio.NoDevice)
            {
                Console.WriteLine("No default input device found");
            }
            var inputInfo = PortAudio.GetDeviceInfo(inputDeviceIndex);
            var inparam = new StreamParameters
            {
                device = inputDeviceIndex,
                channelCount = Channels,
                sampleFormat = SampleFormat.Float32,
                suggestedLatency = inputInfo.defaultLowInputLatency,
                hostApiSpecificStreamInfo = IntPtr.Zero
            };            _waveIn = new PortAudioSharp.Stream(
                inParams: inparam, outParams: null, sampleRate: InputSampleRate, framesPerBuffer: (uint)InputFrameSize,
                streamFlags: StreamFlags.ClipOff, callback: InCallback, userData: IntPtr.Zero
            );

            // 启动音频播放
            StartPlaying();

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
            if (_waveOutStream.Count <= 0)
            {
                //return StreamCallbackResult.Complete;
            }
            try
            {
                while (_waveOutStream.Count > 0)
                {
                    float[]? buffer;
                    lock (_waveOutStream)
                    {
                        if (_waveOutStream.TryDequeue(out buffer))
                        {
                            if (buffer.Length < frameCount)
                            {
                                float[] paddedBuffer = new float[frameCount];
                                Array.Copy(buffer, paddedBuffer, buffer.Length);
                                Marshal.Copy(paddedBuffer, 0, output, (int)frameCount);
                            }
                            else
                            {
                                Marshal.Copy(buffer, 0, output, (int)frameCount);
                            }
                        }
                        return StreamCallbackResult.Continue;
                    }
                }
                return StreamCallbackResult.Continue;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                return StreamCallbackResult.Complete;
            }
        }

        private StreamCallbackResult InCallback(
            IntPtr input, IntPtr output, uint frameCount, ref StreamCallbackTimeInfo timeInfo,
            StreamCallbackFlags statusFlags, IntPtr userData)
        {
            try
            {
                if (!IsRecording)
                {
                    return StreamCallbackResult.Complete;
                }

                // 创建一个数组来存储输入的音频数据
                float[] samples = new float[frameCount];
                // 将输入的音频数据从非托管内存复制到托管数组
                Marshal.Copy(input, samples, 0, (int)frameCount);

                // 将音频数据转换为字节数组
                byte[] buffer = FloatArrayToByteArray(samples);

                // 处理音频数据
                AddRecordSamples(buffer, buffer.Length);

                return StreamCallbackResult.Continue;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return StreamCallbackResult.Complete;
            }
        }        private void AddRecordSamples(byte[] buffer, int bytesRecorded)
        {
            int frameCount = bytesRecorded / (InputFrameSize * 2); // 每个样本 2 字节

            for (int i = 0; i < frameCount; i++)
            {
                byte[] frame = new byte[InputFrameSize * 2];
                Array.Copy(buffer, i * InputFrameSize * 2, frame, 0, InputFrameSize * 2);

                // 编码音频帧 - 使用输入采样率
                byte[] opusPacket = audioCodec.Encode(frame, InputSampleRate, Channels);
                if (opusPacket.Length > 0)
                {
                    _opusRecordPackets.Enqueue(opusPacket);
                }
            }
        }        public void AddOutStreamSamples(byte[] opusData)
        {
            if (opusData == null || opusData.Length == 0)
                return;

            try
            {
                // 使用修复后的解码器解码 Opus 数据 - 使用输出采样率
                byte[] pcmData = audioCodec.Decode(opusData, OutputSampleRate, Channels);

                if (pcmData.Length > 0)
                {
                    // 将解码后的 PCM 数据转换为 float 数组
                    float[] floatData = ByteArrayToFloatArray(pcmData);

                    // 将 PCM 数据添加到缓冲区
                    lock (_waveOutStream)
                    {
                        _waveOutStream.Enqueue(floatData);
                    }

                    if (_waveOutStream.Count > 5)
                    {
                        if (!IsPlaying)
                        {
                            StartPlaying();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogConsole.ErrorLine($"Error decoding Opus data: {ex.Message}");
            }
        }

        public void StartRecording()
        {
            if (!IsRecording)
            {
                _waveIn?.Start();
                IsRecording = true;
            }
        }

        public void StopRecording()
        {
            if (IsRecording)
            {
                _waveIn?.Stop();
                IsRecording = false;
            }
        }

        public void StartPlaying()
        {
            if (!IsPlaying)
            {
                _waveOut?.Start();
                IsPlaying = true;
            }
        }

        public void StopPlaying()
        {
            if (IsPlaying)
            {
                _waveOut?.Stop();
                IsPlaying = false;
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

        public static byte[] FloatArrayToByteArray(float[] floatArray)
        {
            // 初始化一个与 float 数组长度两倍的 byte 数组，因为每个 short 占 2 个字节
            byte[] byteArray = new byte[floatArray.Length * 2];

            for (int i = 0; i < floatArray.Length; i++)
            {
                // 将 float 类型的值映射到 short 类型的范围
                short sample = (short)(floatArray[i] * short.MaxValue);

                // 将 short 类型的值拆分为两个字节
                byteArray[i * 2] = (byte)(sample & 0xFF);
                byteArray[i * 2 + 1] = (byte)(sample >> 8);
            }

            return byteArray;
        }

        public static float[] ByteArrayToFloatArray(byte[] byteArray)
        {
            int floatArrayLength = byteArray.Length / 2;
            float[] floatArray = new float[floatArrayLength];

            for (int i = 0; i < floatArrayLength; i++)
            {
                short sample = BitConverter.ToInt16(byteArray, i * 2);
                floatArray[i] = sample / (float)short.MaxValue;
            }

            return floatArray;
        }

        public void Dispose()
        {
            IsPlaying = false;
            IsRecording = false;
            audioCodec?.Dispose();
            _waveIn?.Dispose();
            _waveOut?.Dispose();
            PortAudio.Terminate();
        }
    }
}
