using System;
using System.Linq;
using System.Reflection;
using OpusSharp.Core;

class ApiCheck 
{
    static void Main()
    {
        // Use OpusSharp to create decoder
        var decoder = new OpusDecoder(48000, 1);
        
        // Test the array-based method signatures
        byte[] encoded = new byte[100];
        short[] output = new short[1000];
        
        Console.WriteLine("Testing OpusSharp OpusDecoder API...");
        
        // Use the OpusSharp array-based method
        try 
        {
            int result = decoder.Decode(encoded, encoded.Length, output, 1440, false);
            Console.WriteLine($"OpusSharp array-based Decode works, result: {result}");
            Console.WriteLine("Signature: Decode(byte[], int, short[], int, bool)");
        }
        catch (Exception e)
        {
            Console.WriteLine($"Array-based method failed: {e.Message}");
        }
        
        // Now let's use reflection to see what methods are available
        var methods = typeof(OpusDecoder).GetMethods()
            .Where(m => m.Name == "Decode")
            .ToArray();
        
        Console.WriteLine($"\nFound {methods.Length} Decode methods:");
        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            var paramTypes = string.Join(", ", parameters.Select(p => p.ParameterType.Name));
            Console.WriteLine($"  Decode({paramTypes})");
        }
        
        decoder?.Dispose();
    }
}
