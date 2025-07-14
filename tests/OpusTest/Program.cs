using System;
using OpusSharp.Core;

class Program
{
    static void Main()
    {
        // Simple test for OpusSharp encode/decode functionality
        var encoder = new OpusEncoder(24000, 1, OpusPredefinedValues.OPUS_APPLICATION_AUDIO);
        var decoder = new OpusDecoder(24000, 1);

        Console.WriteLine("Testing OpusSharp encode/decode for 24kHz mono audio...");

        // Create test data - 60ms at 24kHz = 1440 samples
        short[] pcmData = new short[1440];
        for (int i = 0; i < pcmData.Length; i++)
        {
            pcmData[i] = (short)(1000 * Math.Sin(2 * Math.PI * 440 * i / 24000)); // 440Hz tone
        }

        // Test encoding
        byte[] outputBuffer = new byte[4000];

        try
        {
            int encodedLength = encoder.Encode(pcmData, 1440, outputBuffer, outputBuffer.Length);
            Console.WriteLine($"✓ Encoded {encodedLength} bytes successfully");

            // Test decoding
            byte[] encodedData = new byte[encodedLength];
            Array.Copy(outputBuffer, encodedData, encodedLength);
            
            short[] decodedData = new short[1440];
            
            int decodedSamples = decoder.Decode(encodedData, encodedLength, decodedData, 1440, false);
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
        finally
        {
            encoder?.Dispose();
            decoder?.Dispose();
        }

        Console.WriteLine("Test completed.");
    }
}
