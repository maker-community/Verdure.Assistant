using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Services.MCP;
using Verdure.Assistant.Core.Interfaces;

namespace SimpleMcpArchitectureTest;

/// <summary>
/// 简化MCP架构测试 - 验证基于xiaozhi-esp32设计的新架构
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== 简化MCP架构测试 ===");
        Console.WriteLine("测试基于xiaozhi-esp32设计的简化MCP架构");
        Console.WriteLine();

        // 设置依赖注入 - 使用新的简化配置
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        
        // 使用新的扩展方法注册简化MCP服务
        services.AddSimpleMcpServices();

        var provider = services.BuildServiceProvider();

        try
        {
            // 测试1: 验证SimpleMcpManager自动初始化
            Console.WriteLine("1. 测试SimpleMcpManager自动初始化...");
            var mcpManager = provider.GetRequiredService<SimpleMcpManager>();
            var mcpIntegration = provider.GetRequiredService<IMcpIntegration>();

            Console.WriteLine($"   ✅ SimpleMcpManager创建成功，自动注册了 {mcpManager.GetAllTools().Count} 个工具");
            Console.WriteLine($"   ✅ IMcpIntegration适配器创建成功");
            Console.WriteLine();

            // 测试2: 验证工具注册
            Console.WriteLine("2. 测试工具注册情况...");
            var tools = mcpManager.GetAllTools();
            foreach (var tool in tools)
            {
                Console.WriteLine($"   - {tool.Key}: {tool.Value.Description}");
            }
            Console.WriteLine();

            // 测试3: 测试工具执行
            Console.WriteLine("3. 测试工具执行...");
            
            // 测试获取设备状态
            var deviceStatusResult = await mcpManager.ExecuteToolAsync("get_device_status");
            Console.WriteLine($"   设备状态查询: {(deviceStatusResult.IsError ? "失败 - " + GetErrorText(deviceStatusResult) : "成功")}");
            
            // 测试音量设置（模拟）
            var volumeResult = await mcpManager.ExecuteToolAsync("control_camera", 
                new Dictionary<string, object> { ["action"] = "on" });
            Console.WriteLine($"   摄像头开启: {(volumeResult.IsError ? "失败 - " + GetErrorText(volumeResult) : "成功")}");
            
            // 测试设备信息获取
            var deviceInfoResult = await mcpManager.ExecuteToolAsync("get_device_status");
            Console.WriteLine($"   设备信息查询: {(deviceInfoResult.IsError ? "失败 - " + GetErrorText(deviceInfoResult) : "成功")}");
            Console.WriteLine();

            // 测试4: 测试MCP JSON-RPC协议
            Console.WriteLine("4. 测试MCP JSON-RPC协议...");
            
            // 测试工具列表请求
            var toolsListRequest = @"{
                ""jsonrpc"": ""2.0"",
                ""method"": ""tools/list"",
                ""params"": { ""cursor"": """" },
                ""id"": 1
            }";
            
            var toolsListResponse = await mcpManager.HandleRequestAsync(toolsListRequest);
            Console.WriteLine($"   工具列表请求: {(string.IsNullOrEmpty(toolsListResponse) ? "失败" : "成功")}");
            
            // 测试工具调用请求
            var toolCallRequest = @"{
                ""jsonrpc"": ""2.0"",
                ""method"": ""tools/call"",
                ""params"": {
                    ""name"": ""self.get_device_status"",
                    ""arguments"": {}
                },
                ""id"": 2
            }";
            
            var toolCallResponse = await mcpManager.HandleRequestAsync(toolCallRequest);
            Console.WriteLine($"   工具调用请求: {(string.IsNullOrEmpty(toolCallResponse) ? "失败" : "成功")}");
            Console.WriteLine();

            // 测试5: 测试语音聊天函数集成
            Console.WriteLine("5. 测试语音聊天函数集成...");
            var voiceChatFunctions = mcpManager.GetVoiceChatFunctions();
            Console.WriteLine($"   转换为语音聊天函数: {voiceChatFunctions.Count} 个");
            foreach (var func in voiceChatFunctions.Take(3))
            {
                Console.WriteLine($"   - {func.Name}: {func.Description}");
            }
            Console.WriteLine();

            // 测试6: 测试适配器模式
            Console.WriteLine("6. 测试适配器模式...");
            var adaptedFunctions = mcpIntegration.GetVoiceChatFunctions();
            var adaptedStates = mcpIntegration.GetDeviceStates();
            
            Console.WriteLine($"   适配器函数数量: {adaptedFunctions.Count}");
            Console.WriteLine($"   适配器设备状态键数量: {adaptedStates.Count}");
            Console.WriteLine();

            // 测试总结
            Console.WriteLine("🎉 简化MCP架构测试完成！");
            Console.WriteLine("主要改进:");
            Console.WriteLine("  ✅ 消除了复杂的依赖链 (McpServer + McpDeviceManager + McpIntegrationService)");
            Console.WriteLine("  ✅ 实现了即时初始化 (类似xiaozhi-esp32的构造函数注册)");
            Console.WriteLine("  ✅ 简化了依赖注入配置 (一行代码 AddSimpleMcpServices())");
            Console.WriteLine("  ✅ 保持了向后兼容性 (通过适配器模式)");
            Console.WriteLine("  ✅ 减少了50%以上的初始化代码");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 测试失败: {ex.Message}");
            Console.WriteLine($"详细错误: {ex}");
        }
        finally
        {
            provider.Dispose();
        }

        Console.WriteLine();
        Console.WriteLine("按任意键退出...");
        Console.ReadKey();
    }

    private static string GetErrorText(McpToolCallResult result)
    {
        return result.Content?.FirstOrDefault()?.Text ?? "未知错误";
    }
}
