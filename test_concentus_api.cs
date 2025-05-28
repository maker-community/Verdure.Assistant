using System;
using Concentus;
using Concentus.Enums;
using Concentus.Structs;

// Test file to understand Concentus API
public class ConcentusTest 
{
    public static void TestAPI()
    {
        var encoder = OpusCodecFactory.CreateEncoder(16000, 1, OpusApplication.OPUS_APPLICATION_VOIP);
        
        // Test different method signatures
        short[] pcmData = new short[960]; // 60ms at 16kHz
        byte[] outputBuffer = new byte[4000];
        
        // Try to find the correct Encode method signature
        // var result1 = encoder.Encode(pcmData, 0, 960, outputBuffer, 0, 4000);
        // var result2 = encoder.Encode(pcmData, 960, outputBuffer, 0, 4000);
        // var result3 = encoder.Encode(pcmData, outputBuffer, 4000);
        
        Console.WriteLine("Available methods:");
        var methods = typeof(IOpusEncoder).GetMethods();
        foreach (var method in methods)
        {
            if (method.Name == "Encode")
            {
                Console.WriteLine($"Encode: {method}");
                var parameters = method.GetParameters();
                foreach (var param in parameters)
                {
                    Console.WriteLine($"  - {param.ParameterType.Name} {param.Name}");
                }
            }
        }
    }
}
