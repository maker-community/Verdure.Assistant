using OpusSharp.Core;
using Verdure.Assistant.Core.Interfaces;

namespace Verdure.Assistant.Core.Services;

/// <summary>
/// OpusSharp音频编解码器实现
/// </summary>
public class OpusSharpAudioCodec : IAudioCodec
{
    private OpusEncoder? _encoder;
    private OpusDecoder? _decoder;
    private readonly object _lock = new();
    private int _currentSampleRate;
    private int _currentChannels;    
    public byte[] Encode(byte[] pcmData, int sampleRate, int channels)
    {
        lock (_lock)
        {
            // 验证输入参数是否符合官方规格
            if (sampleRate != 16000)
            {
                System.Console.WriteLine($"警告: 编码采样率 {sampleRate} 不符合官方规格 16000Hz");
            }
            if (channels != 1)
            {
                System.Console.WriteLine($"警告: 编码声道数 {channels} 不符合官方规格 1（单声道）");
            }

            if (_encoder == null || _currentSampleRate != sampleRate || _currentChannels != channels)
            {
                _encoder?.Dispose();
                _encoder = new OpusEncoder(sampleRate, channels, OpusPredefinedValues.OPUS_APPLICATION_AUDIO);
                _currentSampleRate = sampleRate;
                _currentChannels = channels;
                System.Console.WriteLine($"Opus编码器已初始化: {sampleRate}Hz, {channels}声道");
            }

            try
            {
                // 计算帧大小 (采样数，不是字节数) - 严格按照官方60ms规格
                int frameSize = sampleRate * 60 / 1000; // 对于16kHz = 960样本
                
                // 确保输入数据长度正确 (16位音频 = 2字节/样本)
                int expectedBytes = frameSize * channels * 2;
                
                //System.Console.WriteLine($"编码PCM数据: 输入长度={pcmData.Length}字节, 期望长度={expectedBytes}字节, 帧大小={frameSize}样本");
                
                if (pcmData.Length != expectedBytes)
                {
                    //System.Console.WriteLine($"调整PCM数据长度: 从{pcmData.Length}字节到{expectedBytes}字节");
                    // 调整数据长度或填充零
                    byte[] adjustedData = new byte[expectedBytes];
                    if (pcmData.Length < expectedBytes)
                    {
                        // 数据不足，复制现有数据并填充零
                        Array.Copy(pcmData, adjustedData, pcmData.Length);
                        //System.Console.WriteLine($"PCM数据不足，已填充{expectedBytes - pcmData.Length}字节的零");
                    }
                    else
                    {
                        // 数据过多，截断
                        Array.Copy(pcmData, adjustedData, expectedBytes);
                        //System.Console.WriteLine($"PCM数据过多，已截断{pcmData.Length - expectedBytes}字节");
                    }
                    pcmData = adjustedData;
                }

                // 转换为16位短整型数组
                short[] pcmShorts = new short[frameSize * channels];
                for (int i = 0; i < pcmShorts.Length && i * 2 + 1 < pcmData.Length; i++)
                {
                    pcmShorts[i] = BitConverter.ToInt16(pcmData, i * 2);
                }

                // 可选：添加输入音频质量检查
                //CheckAudioQuality(pcmData, $"编码输入PCM，长度={pcmData.Length}字节");

                // OpusSharp编码 - 使用正确的API
                byte[] outputBuffer = new byte[4000]; // Opus最大包大小
                int encodedLength = _encoder.Encode(pcmShorts, frameSize, outputBuffer, outputBuffer.Length);

                //System.Console.WriteLine($"编码结果: 输出长度={encodedLength}字节");

                if (encodedLength > 0)
                {
                    // 返回实际编码的数据
                    byte[] result = new byte[encodedLength];
                    Array.Copy(outputBuffer, result, encodedLength);
                    return result;
                }
                else
                {
                    //System.Console.WriteLine($"编码失败: 返回长度为 {encodedLength}");
                }

                return Array.Empty<byte>();
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"OpusSharp编码失败: {ex.Message}");
                System.Console.WriteLine($"堆栈跟踪: {ex.StackTrace}");
                return Array.Empty<byte>();
            }
        }
    }public byte[] Decode(byte[] encodedData, int sampleRate, int channels)
    {
        lock (_lock)
        {
            // 验证输入参数是否符合官方规格
            if (sampleRate != 16000)
            {
                System.Console.WriteLine($"警告: 采样率 {sampleRate} 不符合官方规格 16000Hz");
            }
            if (channels != 1)
            {
                System.Console.WriteLine($"警告: 声道数 {channels} 不符合官方规格 1（单声道）");
            }

            if (_decoder == null || _currentSampleRate != sampleRate || _currentChannels != channels)
            {
                _decoder?.Dispose();
                _decoder = new OpusDecoder(sampleRate, channels);
                _currentSampleRate = sampleRate;
                _currentChannels = channels;
                System.Console.WriteLine($"Opus解码器已初始化: {sampleRate}Hz, {channels}声道");
            }

            // 检查输入数据有效性
            if (encodedData == null || encodedData.Length == 0)
            {
                System.Console.WriteLine("警告: 接收到空的Opus数据包");
                int frameSize = sampleRate * 60 / 1000; // 60ms帧，符合官方规格
                byte[] silenceData = new byte[frameSize * channels * 2];
                return silenceData;
            }

            try
            {
                // 计算帧大小 (采样数，不是字节数) - 严格按照官方60ms规格
                int frameSize = sampleRate * 60 / 1000; // 对于16kHz = 960样本
                
                // 为解码输出分配缓冲区，确保有足够空间
                // Opus可能解码出不同长度的帧，所以使用最大可能的帧大小
                int maxFrameSize = sampleRate * 120 / 1000; // 最大120ms帧作为安全缓冲
                short[] outputBuffer = new short[maxFrameSize * channels];
                
                //System.Console.WriteLine($"解码Opus数据: 输入长度={encodedData.Length}字节, 期望帧大小={frameSize}样本");
                
                // OpusSharp解码 - 使用正确的API，让解码器自动确定帧大小
                int decodedSamples = _decoder.Decode(encodedData, encodedData.Length, outputBuffer, maxFrameSize, false);
                
                //System.Console.WriteLine($"解码结果: 解码了{decodedSamples}样本");
                
                if (decodedSamples > 0)
                {
                    // 验证解码出的样本数是否合理
                    if (decodedSamples > maxFrameSize)
                    {
                        //System.Console.WriteLine($"警告: 解码样本数({decodedSamples})超出最大帧大小({maxFrameSize})");
                        decodedSamples = maxFrameSize;
                    }
                    
                    // 转换为字节数组
                    byte[] pcmBytes = new byte[decodedSamples * channels * 2];
                    Buffer.BlockCopy(outputBuffer, 0, pcmBytes, 0, decodedSamples * channels * 2);
                    
                    // 可选：添加简单的音频质量检查
                    CheckAudioQuality(pcmBytes, $"解码输出PCM，长度={pcmBytes.Length}字节");
                    
                    return pcmBytes;
                }
                else
                {
                    //System.Console.WriteLine($"解码失败: 返回的样本数为 {decodedSamples}");
                }
                
                // 返回静音数据而不是空数组，保持音频流连续性
                int silenceFrameSize = frameSize * channels * 2;
                byte[] silenceData = new byte[silenceFrameSize];
                //System.Console.WriteLine($"返回静音数据: {silenceFrameSize}字节");
                return silenceData;
            }
            catch (Exception ex)
            {
                System.Console.WriteLine($"OpusSharp解码失败: {ex.Message}");
                System.Console.WriteLine($"堆栈跟踪: {ex.StackTrace}");
                
                // 返回静音数据而不是空数组，保持音频流连续性
                int frameSize = sampleRate * 60 / 1000; // 60ms帧
                byte[] silenceData = new byte[frameSize * channels * 2];
                return silenceData;
            }
        }
    }

    /// <summary>
    /// 简单的音频质量检查，帮助诊断音频问题
    /// </summary>
    private void CheckAudioQuality(byte[] pcmData, string context)
    {
        if (pcmData.Length < 4) return;

        // 转换为16位样本进行分析
        var samples = new short[pcmData.Length / 2];
        Buffer.BlockCopy(pcmData, 0, samples, 0, pcmData.Length);

        // 计算音频统计信息
        double sum = 0;
        double sumSquares = 0;
        short min = short.MaxValue;
        short max = short.MinValue;
        int zeroCount = 0;

        foreach (short sample in samples)
        {
            sum += sample;
            sumSquares += sample * sample;
            min = Math.Min(min, sample);
            max = Math.Max(max, sample);
            if (sample == 0) zeroCount++;
        }

        double mean = sum / samples.Length;
        double rms = Math.Sqrt(sumSquares / samples.Length);
        double zeroPercent = (double)zeroCount / samples.Length * 100;

        // 检测潜在问题
        bool hasIssues = false;
        var issues = new List<string>();

        // 检查是否全为零（静音）
        if (zeroPercent > 95)
        {
            issues.Add("几乎全为静音");
            hasIssues = true;
        }

        // 检查是否有削波（饱和）
        if (max >= 32760 || min <= -32760)
        {
            issues.Add("可能存在音频削波");
            hasIssues = true;
        }

        // 检查是否有异常的DC偏移
        if (Math.Abs(mean) > 1000)
        {
            issues.Add($"异常的DC偏移: {mean:F1}");
            hasIssues = true;
        }

        // 检查RMS是否异常低（可能的损坏信号）
        if (rms < 10 && zeroPercent < 50)
        {
            issues.Add($"异常低的RMS: {rms:F1}");
            hasIssues = true;
        }        if (hasIssues)
        {
            //System.Console.WriteLine($"音频质量警告 ({context}): {string.Join(", ", issues)}");
            //System.Console.WriteLine($"  统计: 样本数={samples.Length}, RMS={rms:F1}, 范围=[{min}, {max}], 零值比例={zeroPercent:F1}%");
        }
        else
        {
            //System.Console.WriteLine($"音频质量正常 ({context}): RMS={rms:F1}, 范围=[{min}, {max}]");
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _encoder?.Dispose();
            _decoder?.Dispose();
        }
    }
}
