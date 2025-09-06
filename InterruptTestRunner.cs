using System;
using System.Threading.Tasks;
using Verdure.Assistant.Core.Tests;

namespace InterruptArchitectureTestRunner;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("Interrupt Architecture Integration Test Runner");
        Console.WriteLine("=============================================\n");

        try
        {
            var test = new InterruptArchitectureIntegrationTest();
            await test.RunAllTestsAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Test execution failed: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            return;
        }

        Console.WriteLine("\nPress any key to exit...");
        Console.ReadKey();
    }
}