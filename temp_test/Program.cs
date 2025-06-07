using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Services.MCP;

// Setup DI container
var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole());
services.AddSingleton<McpServer>();
services.AddSingleton<McpDeviceManager>();

var serviceProvider = services.BuildServiceProvider();

try
{
    // Get MCP services
    var mcpServer = serviceProvider.GetRequiredService<McpServer>();
    var deviceManager = serviceProvider.GetRequiredService<McpDeviceManager>();
    
    // Initialize MCP Server
    Console.WriteLine("Initializing MCP Server...");
    await mcpServer.InitializeAsync();
    
    // Initialize Device Manager
    Console.WriteLine("Initializing MCP Device Manager...");
    await deviceManager.InitializeAsync();
    
    // Test device discovery
    Console.WriteLine($"Discovered {deviceManager.Devices.Count} devices:");
    foreach (var device in deviceManager.Devices.Values)
    {
        Console.WriteLine($"  - {device.Name} ({device.DeviceId}): {device.Description}");
    }
    
    // Test tool registration
    var tools = deviceManager.GetAllTools();
    Console.WriteLine($"Registered {tools.Count} tools:");
    foreach (var tool in tools.Take(5)) // Show first 5 tools
    {
        Console.WriteLine($"  - {tool.Name}: {tool.Description}");
    }
    
    // Test a simple tool execution
    Console.WriteLine("\nTesting lamp turn_on tool...");
    var result = await deviceManager.ExecuteToolAsync("self.lamp.turn_on");
    Console.WriteLine($"Result: {result.Content.FirstOrDefault()?.Text ?? "No content"}");
    
    Console.WriteLine("\nMCP implementation test completed successfully!");
}
catch (Exception ex)
{
    Console.WriteLine($"Test failed: {ex.Message}");
    Console.WriteLine($"Stack trace: {ex.StackTrace}");
    return 1;
}
finally
{
    serviceProvider.Dispose();
}

return 0;
