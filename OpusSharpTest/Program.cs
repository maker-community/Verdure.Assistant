using System;
using OpusSharp;

// Simple OpusSharp test project
class Program
{
    static void Main()
    {
        Console.WriteLine("Hello OpusSharp Test");
        
        try
        {
            // Try to find the correct types
            var encoder = new Encoder(Application.Audio, 16000, 1);
            Console.WriteLine("OpusSharp Encoder created successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error creating encoder: {ex.Message}");
        }
        
        try
        {
            var decoder = new Decoder(16000, 1);
            Console.WriteLine("OpusSharp Decoder created successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error creating decoder: {ex.Message}");
        }
    }
}
