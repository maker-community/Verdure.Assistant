using System;
using System.Linq;
using System.Reflection;
using OpusSharp.Core;

// Simple OpusSharp test project - test the actual API
class Program
{
    static void Main()
    {
        Console.WriteLine("Hello OpusSharp Test - Testing API");
        
        try
        {
            // Try to create encoder using the correct API
            // OpusEncoder(sampleRate, channels, application)
            var encoder = new OpusEncoder(16000, 1, OpusPredefinedValues.OPUS_APPLICATION_AUDIO);
            Console.WriteLine("✓ OpusSharp Encoder created successfully");
            
            // Try to create decoder
            // OpusDecoder(sampleRate, channels)
            var decoder = new OpusDecoder(16000, 1);
            Console.WriteLine("✓ OpusSharp Decoder created successfully");
            
            // Test basic encode/decode
            short[] pcmData = new short[960]; // 60ms at 16kHz
            for (int i = 0; i < pcmData.Length; i++)
            {
                pcmData[i] = (short)(1000 * Math.Sin(2 * Math.PI * 440 * i / 16000)); // 440Hz tone
            }
            
            Console.WriteLine("✓ Test audio data generated");
            
            // Encode using the correct signature: Encode(Int16[] input, Int32 frame_size, Byte[] output, Int32 max_data_bytes)
            byte[] outputBuffer = new byte[4000];
            int encodedLength = encoder.Encode(pcmData, pcmData.Length, outputBuffer, outputBuffer.Length);
            Console.WriteLine($"✓ Encoded {encodedLength} bytes");
            
            // Decode using the correct signature: Decode(Byte[] input, Int32 length, Int16[] output, Int32 frame_size, Boolean decode_fec)
            short[] decodedBuffer = new short[960];
            int decodedSamples = decoder.Decode(outputBuffer, encodedLength, decodedBuffer, decodedBuffer.Length, false);
            Console.WriteLine($"✓ Decoded {decodedSamples} samples");
            
            // Verify the data
            bool hasSignal = false;
            for (int i = 0; i < Math.Min(100, decodedBuffer.Length); i++)
            {
                if (Math.Abs(decodedBuffer[i]) > 100)
                {
                    hasSignal = true;
                    break;
                }
            }
            
            if (hasSignal)
            {
                Console.WriteLine("✓ Decoded audio contains signal data");
            }
            else
            {
                Console.WriteLine("⚠ Decoded audio appears to be silence");
            }
            
            Console.WriteLine("✓ OpusSharp test completed successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Error testing OpusSharp: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
        }
    }
}
