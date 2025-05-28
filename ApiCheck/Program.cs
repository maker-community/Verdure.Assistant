using System;
using System.Linq;
using System.Reflection;
using Concentus.Structs;

class ApiCheck 
{
    static void Main()
    {
        var decoder = new OpusDecoder(48000, 1);
        
        // Test the Span-based method signatures
        byte[] encoded = new byte[100];
        short[] output = new short[1000];
        
        ReadOnlySpan<byte> encodedSpan = new ReadOnlySpan<byte>(encoded);
        Span<short> outputSpan = new Span<short>(output);        Console.WriteLine("Testing Concentus OpusDecoder API...");
        
        // Use the legacy method that we know works to understand the API
        try 
        {
            int result = decoder.Decode(encoded, 0, encoded.Length, output, 0, output.Length, false);
            Console.WriteLine($"Legacy 7-parameter Decode works, result: {result}");
            Console.WriteLine("Signature: Decode(byte[], int, int, short[], int, int, bool)");
        }
        catch (Exception e)
        {
            Console.WriteLine($"Legacy method failed: {e.Message}");
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
