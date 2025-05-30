using System;
using System.Linq;
using System.Reflection;
using Concentus;
using Concentus.Enums;
using Concentus.Structs;

class ApiCheck 
{
    static void Main()
    {
        // Use OpusCodecFactory to create decoder (recommended approach)
        var decoder = (OpusDecoder)OpusCodecFactory.CreateDecoder(48000, 1);
        
        // Test the Span-based method signatures
        byte[] encoded = new byte[100];
        short[] output = new short[1000];
        
        ReadOnlySpan<byte> encodedSpan = new ReadOnlySpan<byte>(encoded);
        Span<short> outputSpan = new Span<short>(output);
        
        Console.WriteLine("Testing Concentus OpusDecoder API...");
        
        // Use the modern Span-based method
        try 
        {
            int result = decoder.Decode(encodedSpan, outputSpan, 1440);
            Console.WriteLine($"Modern 3-parameter Span-based Decode works, result: {result}");
            Console.WriteLine("Signature: Decode(ReadOnlySpan<byte>, Span<short>, int)");
        }
        catch (Exception e)
        {
            Console.WriteLine($"Span-based method failed: {e.Message}");
        }
        
        // Now let's use reflection to see what Span methods are available
        var methods = typeof(OpusDecoder).GetMethods()
            .Where(m => m.Name == "Decode" && 
                       m.GetParameters().Any(p => p.ParameterType.Name.Contains("Span")))
            .ToArray();
        
        Console.WriteLine($"\nFound {methods.Length} Span-based Decode methods:");
        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            var paramTypes = string.Join(", ", parameters.Select(p => p.ParameterType.Name));
            Console.WriteLine($"  Decode({paramTypes})");
        }
    }
}
