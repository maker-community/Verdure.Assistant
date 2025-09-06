using Verdure.Assistant.Core.Tests;

namespace Verdure.Assistant.Core.VadTest;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== VAD Interrupt Source Test ===");
        Console.WriteLine("Testing the enhanced VoiceActivityInterruptSource implementation...");
        Console.WriteLine();

        try
        {
            await VadInterruptSourceTest.TestVadWithSimulatedAudio();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Test execution failed: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }
}
