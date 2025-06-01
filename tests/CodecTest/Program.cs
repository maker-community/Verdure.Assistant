using System;
using System.IO;
using Verdure.Assistant.Core.Services;

// Test program to compare Concentus vs OpusSharp codec implementations
class Program
{
    static void Main()
    {
        Console.WriteLine("=== XiaoZhi Audio Codec Comparison Test ===\n");
        
        try
        {
            // Create test audio data - 60ms of 16kHz mono (960 samples)
            const int sampleRate = 16000;
            const int channels = 1;
            const int frameSize = 960; // 60ms at 16kHz
            
            // Generate test tone (440Hz sine wave)
            byte[] testPcmData = GenerateTestTone(frameSize, sampleRate);
            
            Console.WriteLine($"Generated test audio: {testPcmData.Length} bytes");
            Console.WriteLine($"Sample rate: {sampleRate}Hz, Channels: {channels}, Frame size: {frameSize} samples");
            
            // Test both codecs
            TestCodec("Concentus (OpusAudioCodec)", new OpusAudioCodec(), testPcmData, sampleRate, channels);
            TestCodec("OpusSharp (OpusSharpAudioCodec)", new OpusSharpAudioCodec(), testPcmData, sampleRate, channels);
            
            Console.WriteLine("\n=== Test completed successfully! ===");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Test failed: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }
    
    static void TestCodec(string codecName, Verdure.Assistant.Core.Interfaces.IAudioCodec codec, byte[] testData, int sampleRate, int channels)
    {
        Console.WriteLine($"\n--- Testing {codecName} ---");
        
        try
        {
            // Test encoding
            var encodedData = codec.Encode(testData, sampleRate, channels);
            Console.WriteLine($"✓ Encoding successful: {encodedData.Length} bytes");
            
            if (encodedData.Length == 0)
            {
                Console.WriteLine("⚠ Warning: Encoded data is empty");
                return;
            }
            
            // Test decoding
            var decodedData = codec.Decode(encodedData, sampleRate, channels);
            Console.WriteLine($"✓ Decoding successful: {decodedData.Length} bytes");
            
            if (decodedData.Length == 0)
            {
                Console.WriteLine("⚠ Warning: Decoded data is empty");
                return;
            }
            
            // Basic validation
            bool hasNonZeroData = false;
            for (int i = 0; i < Math.Min(100, decodedData.Length); i++)
            {
                if (decodedData[i] != 0)
                {
                    hasNonZeroData = true;
                    break;
                }
            }
            
            if (hasNonZeroData)
            {
                Console.WriteLine("✓ Decoded audio contains signal data");
            }
            else
            {
                Console.WriteLine("⚠ Decoded audio appears to be silence");
            }
              // Calculate compression ratio
            double compressionRatio = (double)testData.Length / encodedData.Length;
            Console.WriteLine($"✓ Compression ratio: {compressionRatio:F2}:1");
            
            // Dispose if the codec supports it
            if (codec is IDisposable disposableCodec)
            {
                disposableCodec.Dispose();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ {codecName} failed: {ex.Message}");
        }
    }
    
    static byte[] GenerateTestTone(int frameSize, int sampleRate)
    {
        // Generate 440Hz sine wave
        short[] samples = new short[frameSize];
        const double frequency = 440.0; // A4 note
        
        for (int i = 0; i < frameSize; i++)
        {
            double time = (double)i / sampleRate;
            double amplitude = 1000; // Moderate amplitude
            samples[i] = (short)(amplitude * Math.Sin(2 * Math.PI * frequency * time));
        }
        
        // Convert to byte array
        byte[] pcmBytes = new byte[frameSize * 2];
        for (int i = 0; i < frameSize; i++)
        {
            byte[] sampleBytes = BitConverter.GetBytes(samples[i]);
            pcmBytes[i * 2] = sampleBytes[0];
            pcmBytes[i * 2 + 1] = sampleBytes[1];
        }
        
        return pcmBytes;
    }
}
