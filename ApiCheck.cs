using System;
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
        Span<short> outputSpan = new Span<short>(output);
        
        // Test various Decode method signatures
        try 
        {
            // Try 3 parameter version
            int result1 = decoder.Decode(encodedSpan, outputSpan, false);
            Console.WriteLine("3-parameter Decode works: Decode(ReadOnlySpan<byte>, Span<short>, bool)");
        }
        catch (Exception e)
        {
            Console.WriteLine($"3-parameter failed: {e.Message}");
        }
        
        try 
        {
            // Try 2 parameter version
            int result2 = decoder.Decode(encodedSpan, outputSpan);
            Console.WriteLine("2-parameter Decode works: Decode(ReadOnlySpan<byte>, Span<short>)");
        }
        catch (Exception e)
        {
            Console.WriteLine($"2-parameter failed: {e.Message}");
        }
    }
}
