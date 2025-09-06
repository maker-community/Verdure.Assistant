using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Services.Interrupt;
using Verdure.Assistant.Core.Services.Interrupt.Sources;

namespace Verdure.Assistant.Core.Tests;

/// <summary>
/// 简单的打断架构集成测试
/// Simple integration test for the interrupt architecture
/// </summary>
public class InterruptArchitectureIntegrationTest
{
    private readonly ILogger? _logger;

    public InterruptArchitectureIntegrationTest(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 测试基本的打断服务功能
    /// </summary>
    public async Task TestBasicInterruptServiceAsync()
    {
        Console.WriteLine("=== Testing Basic Interrupt Service ===");

        // Create interrupt service
        var interruptService = new InterruptService(_logger as ILogger<InterruptService>);
        var interruptReceived = false;
        string? receivedDescription = null;

        // Subscribe to interrupt events
        interruptService.InterruptOccurred += (sender, e) =>
        {
            interruptReceived = true;
            receivedDescription = e.Description;
            Console.WriteLine($"Interrupt received: {e.InterruptType} from {e.SourceName} - {e.Description}");
        };

        // Create and register a manual interrupt source
        var manualSource = new ManualInterruptSource(_logger as ILogger<ManualInterruptSource>);
        interruptService.RegisterInterruptSource(manualSource);

        // Start the interrupt service
        await interruptService.StartAllAsync();

        // Trigger a manual interrupt
        Console.WriteLine("Triggering manual interrupt...");
        await interruptService.TriggerManualInterruptAsync("Test manual interrupt");

        // Wait a bit for the async processing
        await Task.Delay(100);

        // Check results
        if (interruptReceived && receivedDescription == "Test manual interrupt")
        {
            Console.WriteLine("✅ Basic interrupt service test PASSED");
        }
        else
        {
            Console.WriteLine("❌ Basic interrupt service test FAILED");
        }

        // Cleanup
        await interruptService.StopAllAsync();
        interruptService.Dispose();
    }

    /// <summary>
    /// 测试API打断源
    /// </summary>
    public async Task TestApiInterruptSourceAsync()
    {
        Console.WriteLine("\n=== Testing API Interrupt Source ===");

        var interruptService = new InterruptService(_logger as ILogger<InterruptService>);
        var apiInterruptReceived = false;

        interruptService.InterruptOccurred += (sender, e) =>
        {
            if (e.InterruptType == InterruptTypes.Api)
            {
                apiInterruptReceived = true;
                Console.WriteLine($"API Interrupt received: {e.Description}");
            }
        };

        // Create and register API interrupt source
        var apiSource = new ApiInterruptSource(_logger as ILogger<ApiInterruptSource>);
        interruptService.RegisterInterruptSource(apiSource);

        await interruptService.StartAllAsync();

        // Trigger API interrupt
        Console.WriteLine("Triggering API interrupt...");
        apiSource.TriggerApiInterrupt("/api/test/interrupt", new { TestData = "sample" });

        await Task.Delay(100);

        if (apiInterruptReceived)
        {
            Console.WriteLine("✅ API interrupt source test PASSED");
        }
        else
        {
            Console.WriteLine("❌ API interrupt source test FAILED");
        }

        await interruptService.StopAllAsync();
        interruptService.Dispose();
    }

    /// <summary>
    /// 测试多个打断源
    /// </summary>
    public async Task TestMultipleInterruptSourcesAsync()
    {
        Console.WriteLine("\n=== Testing Multiple Interrupt Sources ===");

        var interruptService = new InterruptService(_logger as ILogger<InterruptService>);
        var interruptCount = 0;

        interruptService.InterruptOccurred += (sender, e) =>
        {
            interruptCount++;
            Console.WriteLine($"Interrupt #{interruptCount}: {e.InterruptType} from {e.SourceName}");
        };

        // Register multiple sources
        var manualSource = new ManualInterruptSource(_logger as ILogger<ManualInterruptSource>);
        var apiSource = new ApiInterruptSource(_logger as ILogger<ApiInterruptSource>);

        interruptService.RegisterInterruptSource(manualSource);
        interruptService.RegisterInterruptSource(apiSource);

        await interruptService.StartAllAsync();

        // Trigger multiple interrupts
        Console.WriteLine("Triggering multiple interrupts...");
        manualSource.TriggerImmediateInterrupt("Manual interrupt 1");
        apiSource.TriggerApiInterrupt("/api/test", null);
        await interruptService.TriggerManualInterruptAsync("Manual interrupt 2");

        await Task.Delay(200);

        if (interruptCount >= 3)
        {
            Console.WriteLine($"✅ Multiple interrupt sources test PASSED (received {interruptCount} interrupts)");
        }
        else
        {
            Console.WriteLine($"❌ Multiple interrupt sources test FAILED (received {interruptCount} interrupts, expected 3)");
        }

        await interruptService.StopAllAsync();
        interruptService.Dispose();
    }

    /// <summary>
    /// 运行所有测试
    /// </summary>
    public async Task RunAllTestsAsync()
    {
        Console.WriteLine("🚀 Starting Interrupt Architecture Integration Tests\n");

        try
        {
            await TestBasicInterruptServiceAsync();
            await TestApiInterruptSourceAsync();
            await TestMultipleInterruptSourcesAsync();

            Console.WriteLine("\n🎉 All tests completed!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"\n💥 Test execution failed: {ex.Message}");
            throw;
        }
    }
}