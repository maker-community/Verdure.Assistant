using System;
using System.Reflection;
using OpusSharp.Core;

// Test OpusSharp Decode method signatures
var encoder = new OpusEncoder(24000, 1, OpusPredefinedValues.OPUS_APPLICATION_AUDIO);
var decoder = new OpusDecoder(24000, 1);

Console.WriteLine("Testing OpusSharp Decode API signatures...");

// Create test data
short[] pcmData = new short[1440]; // 60ms at 24kHz = 24000 * 0.06 = 1440 samples
for (int i = 0; i < pcmData.Length; i++)
{
    pcmData[i] = (short)(1000 * Math.Sin(2 * Math.PI * 440 * i / 24000)); // 440Hz tone
}

// Encode first
byte[] outputBuffer = new byte[4000];
int encodedLength = encoder.Encode(pcmData, 1440, outputBuffer, outputBuffer.Length);

Console.WriteLine($"Encoded {encodedLength} bytes");

// Now test decode methods
var decodeMethods = typeof(OpusDecoder).GetMethods();
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
    
    int decodedSamples = decoder.Decode(encodedData, encodedLength, decodedData, 1440, false);
    Console.WriteLine($"Decoded {decodedSamples} samples successfully!");
}
catch (Exception ex)
{
    Console.WriteLine($"Decode failed: {ex.Message}");
}
finally
{
    encoder?.Dispose();
    decoder?.Dispose();
}
