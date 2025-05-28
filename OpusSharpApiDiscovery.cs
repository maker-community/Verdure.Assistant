using System;
using System.Reflection;

// Discover what's actually in the OpusSharp packages
class Program
{
    static void Main()
    {
        try
        {
            Console.WriteLine("Looking for OpusSharp assemblies...");
            
            // Try to load both packages
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            
            foreach (var assembly in assemblies)
            {
                if (assembly.FullName.Contains("OpusSharp"))
                {
                    Console.WriteLine($"\nFound assembly: {assembly.FullName}");
                    
                    var types = assembly.GetTypes();
                    foreach (var type in types)
                    {
                        Console.WriteLine($"  Type: {type.FullName}");
                        
                        if (type.Name.Contains("Encode") || type.Name.Contains("Decode") || 
                            type.Name.Contains("Opus") || type.Name == "Encoder" || type.Name == "Decoder")
                        {
                            Console.WriteLine($"    *** POTENTIAL CODEC TYPE: {type.Name} ***");
                            
                            // Show public methods
                            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                            foreach (var method in methods)
                            {
                                if (method.Name == "Encode" || method.Name == "Decode")
                                {
                                    var parameters = string.Join(", ", method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}"));
                                    Console.WriteLine($"      Method: {method.Name}({parameters}) -> {method.ReturnType.Name}");
                                }
                            }
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
