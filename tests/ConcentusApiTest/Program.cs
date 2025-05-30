using System;
using System.Reflection;
using Concentus;
using Concentus.Enums;
using Concentus.Structs;

// Test the actual encoding that matches our implementation
var encoder = (OpusEncoder)OpusCodecFactory.CreateEncoder(16000, 1, OpusApplication.OPUS_APPLICATION_AUDIO);

Console.WriteLine("Testing Opus encoding with Span-based API...");

// Test actual encoding
short[] pcmData = new short[960]; // 60ms at 16kHz = 16000 * 0.06 = 960 samples
for (int i = 0; i < pcmData.Length; i++)
{
    pcmData[i] = (short)(1000 * Math.Sin(2 * Math.PI * 440 * i / 16000)); // 440Hz tone
}

try 
{
    byte[] outputBuffer = new byte[4000];
    
    // Use the Span-based API like in our implementation
    ReadOnlySpan<short> pcmSpan = new ReadOnlySpan<short>(pcmData);
    Span<byte> outputSpan = new Span<byte>(outputBuffer);
    int result = encoder.Encode(pcmSpan, 960, outputSpan, outputBuffer.Length);
    
    Console.WriteLine($"Encode successful! Result: {result} bytes");
    
    if (result > 0)
    {
        // Print first few bytes of encoded data
        Console.Write("Encoded data (first 20 bytes): ");
        for (int i = 0; i < Math.Min(20, result); i++)
        {
            Console.Write($"{outputBuffer[i]:X2} ");
        }
        Console.WriteLine();
        Console.WriteLine("Audio encoding is working correctly!");
    }
    else
    {
        Console.WriteLine("Warning: Encode returned 0 bytes");
    }
    
}
catch (Exception ex)
{
    Console.WriteLine($"Error: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
}

encoder.Dispose();
