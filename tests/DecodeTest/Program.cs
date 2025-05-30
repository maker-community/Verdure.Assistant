using System;
using Concentus;
using Concentus.Enums;
using Concentus.Structs;

// Test Decode method signatures
var encoder = (OpusEncoder)OpusCodecFactory.CreateEncoder(24000, 1, OpusApplication.OPUS_APPLICATION_AUDIO);
var decoder = (OpusDecoder)OpusCodecFactory.CreateDecoder(24000, 1);

Console.WriteLine("Testing Opus Decode API signatures...");

// Create test data
short[] pcmData = new short[1440]; // 60ms at 24kHz = 24000 * 0.06 = 1440 samples
for (int i = 0; i < pcmData.Length; i++)
{
    pcmData[i] = (short)(1000 * Math.Sin(2 * Math.PI * 440 * i / 24000)); // 440Hz tone
}

// Encode first
byte[] outputBuffer = new byte[4000];
ReadOnlySpan<short> pcmSpan = new ReadOnlySpan<short>(pcmData);
Span<byte> outputSpan = new Span<byte>(outputBuffer);
int encodedLength = encoder.Encode(pcmSpan, 1440, outputSpan, outputBuffer.Length);

Console.WriteLine($"Encoded {encodedLength} bytes");

// Now test decode methods
var decodeMethods = typeof(IOpusDecoder).GetMethods();
Console.WriteLine("\nAvailable Decode methods:");
foreach (var method in decodeMethods)
{
    if (method.Name == "Decode")
    {
        Console.WriteLine($"{method.Name}: ");
        var parameters = method.GetParameters();
        foreach (var param in parameters)
        {
            Console.WriteLine($"  - {param.ParameterType.Name} {param.Name}");
        }
        Console.WriteLine();
    }
}

// Test actual decode
try
{
    byte[] encodedData = new byte[encodedLength];
    Array.Copy(outputBuffer, encodedData, encodedLength);
    
    short[] decodedData = new short[1440];
    ReadOnlySpan<byte> encodedSpan = new ReadOnlySpan<byte>(encodedData);
    Span<short> decodedSpan = new Span<short>(decodedData);
      // Try different decode method signatures - use the correct 3-parameter Span method
    int decodedSamples = decoder.Decode(encodedSpan, decodedSpan, 1440);
    Console.WriteLine($"Decoded {decodedSamples} samples successfully!");
}
catch (Exception ex)
{
    Console.WriteLine($"Decode failed: {ex.Message}");
}

encoder.Dispose();
decoder.Dispose();
