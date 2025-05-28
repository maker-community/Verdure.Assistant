using System;
using Concentus.Structs;
using Concentus.Enums;
using Concentus;

class Program
{
    static void Main()
    {
        // Simple test for Opus encode/decode functionality
        var encoder = (OpusEncoder)OpusCodecFactory.CreateEncoder(24000, 1, OpusApplication.OPUS_APPLICATION_AUDIO);
        var decoder = (OpusDecoder)OpusCodecFactory.CreateDecoder(24000, 1);

        Console.WriteLine("Testing Opus encode/decode for 24kHz mono audio...");

        // Create test data - 60ms at 24kHz = 1440 samples
        short[] pcmData = new short[1440];
        for (int i = 0; i < pcmData.Length; i++)
        {
            pcmData[i] = (short)(1000 * Math.Sin(2 * Math.PI * 440 * i / 24000)); // 440Hz tone
        }

        // Test encoding
        byte[] outputBuffer = new byte[4000];
        ReadOnlySpan<short> pcmSpan = new ReadOnlySpan<short>(pcmData);
        Span<byte> outputSpan = new Span<byte>(outputBuffer);

        try
        {
            int encodedLength = encoder.Encode(pcmSpan, 1440, outputSpan, outputBuffer.Length);
            Console.WriteLine($"✓ Encoded {encodedLength} bytes successfully");

            // Test decoding
            byte[] encodedData = new byte[encodedLength];
            Array.Copy(outputBuffer, encodedData, encodedLength);
            
            short[] decodedData = new short[1440];
            ReadOnlySpan<byte> encodedSpan = new ReadOnlySpan<byte>(encodedData);
            Span<short> decodedSpan = new Span<short>(decodedData);
            
            int decodedSamples = decoder.Decode(encodedSpan, decodedSpan, 1440);
            Console.WriteLine($"✓ Decoded {decodedSamples} samples successfully");
            
            // Verify the data is reasonable
            bool hasSignal = false;
            for (int i = 0; i < Math.Min(100, decodedData.Length); i++)
            {
                if (Math.Abs(decodedData[i]) > 100)
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
        }
        catch (Exception ex)
        {
            Console.WriteLine($"✗ Test failed: {ex.Message}");
        }

        Console.WriteLine("Test completed.");
    }
}
