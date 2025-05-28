using System;
using OpusSharp;
using System.Reflection;

// Test file to understand OpusSharp API
class TestOpusSharp
{
    static void Main()
    {
        try
        {
            // Check what types are available in OpusSharp namespace
            Console.WriteLine("Testing OpusSharp API...");
            
            var assembly = Assembly.GetAssembly(typeof(OpusSharp.Application));
            if (assembly != null)
            {
                var types = assembly.GetTypes();
                foreach (var type in types)
                {
                    if (type.Namespace == "OpusSharp")
                    {
                        Console.WriteLine($"Type: {type.Name}");
                        if (type.Name.Contains("Encode") || type.Name.Contains("Decode") || type.Name.Contains("Opus"))
                        {
                            Console.WriteLine($"  -> {type.FullName}");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}
