using System;
using System.Reflection;
using System.Linq;

// Discover what's actually in the OpusSharp packages
class Program
{
    static void Main()
    {
        try
        {
            Console.WriteLine("Testing OpusSharp API...");
            
            // First try to use OpusSharp namespace directly
            try
            {
                // Test different possible type names
                Console.WriteLine("\n=== Testing OpusSharp namespace ===");
                
                var opusSharpAssembly = Assembly.Load("OpusSharp");
                Console.WriteLine($"Loaded OpusSharp assembly: {opusSharpAssembly.FullName}");
                
                var types = opusSharpAssembly.GetTypes();
                Console.WriteLine($"Found {types.Length} types in OpusSharp");
                
                foreach (var type in types)
                {
                    Console.WriteLine($"  Type: {type.FullName}");
                    
                    if (type.Name.Contains("Encode") || type.Name.Contains("Decode") || 
                        type.Name == "Encoder" || type.Name == "Decoder" || 
                        type.Name.Contains("Opus"))
                    {
                        Console.WriteLine($"    *** CODEC TYPE: {type.Name} ***");
                        
                        // Show constructors
                        var constructors = type.GetConstructors();
                        foreach (var ctor in constructors)
                        {
                            var parameters = string.Join(", ", ctor.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                            Console.WriteLine($"      Constructor: {type.Name}({parameters})");
                        }
                        
                        // Show public methods
                        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                        foreach (var method in methods)
                        {
                            var parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                            Console.WriteLine($"      Method: {method.Name}({parameters}) -> {method.ReturnType.Name}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading OpusSharp: {ex.Message}");
            }
            
            // Try OpusSharp.Core
            try
            {
                Console.WriteLine("\n=== Testing OpusSharp.Core namespace ===");
                
                var opusSharpCoreAssembly = Assembly.Load("OpusSharp.Core");
                Console.WriteLine($"Loaded OpusSharp.Core assembly: {opusSharpCoreAssembly.FullName}");
                
                var types = opusSharpCoreAssembly.GetTypes();
                Console.WriteLine($"Found {types.Length} types in OpusSharp.Core");
                
                foreach (var type in types)
                {
                    Console.WriteLine($"  Type: {type.FullName}");
                    
                    if (type.Name.Contains("Encode") || type.Name.Contains("Decode") || 
                        type.Name == "Encoder" || type.Name == "Decoder" || 
                        type.Name.Contains("Opus"))
                    {
                        Console.WriteLine($"    *** CODEC TYPE: {type.Name} ***");
                        
                        // Show constructors
                        var constructors = type.GetConstructors();
                        foreach (var ctor in constructors)
                        {
                            var parameters = string.Join(", ", ctor.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                            Console.WriteLine($"      Constructor: {type.Name}({parameters})");
                        }
                        
                        // Show public methods
                        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                        foreach (var method in methods)
                        {
                            var parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                            Console.WriteLine($"      Method: {method.Name}({parameters}) -> {method.ReturnType.Name}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading OpusSharp.Core: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"General error: {ex.Message}");
        }
    }
}
