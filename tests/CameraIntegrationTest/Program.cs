using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Services;
using Verdure.Assistant.Core.Services.MCP;

namespace Verdure.Assistant.CameraTest
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== 相机服务集成测试 ===");
            Console.WriteLine();

            // 设置依赖注入和日志
            var services = new ServiceCollection();
            services.AddLogging(builder => 
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Information);
            });

            // 注册相机服务
            services.AddCameraService();

            var serviceProvider = services.BuildServiceProvider();
            var logger = serviceProvider.GetRequiredService<ILogger<Program>>();
            var cameraService = serviceProvider.GetRequiredService<ICameraService>();

            try
            {
                // 测试 1: 检查平台信息
                Console.WriteLine("📋 平台信息:");
                Console.WriteLine($"   {CameraServiceFactory.GetPlatformInfo()}");
                Console.WriteLine($"   推荐服务类型: {CameraServiceFactory.DetectPlatformServiceType()}");
                Console.WriteLine();

                // 测试 2: 检查相机可用性
                Console.WriteLine("🔍 检查相机可用性...");
                var isAvailable = await cameraService.IsAvailableAsync();
                Console.WriteLine($"   相机可用: {(isAvailable ? "✅ 是" : "❌ 否")}");
                Console.WriteLine();

                // 测试 3: 获取相机信息
                Console.WriteLine("📷 相机信息:");
                var cameraInfo = await cameraService.GetCameraInfoAsync();
                Console.WriteLine($"   名称: {cameraInfo.Name}");
                Console.WriteLine($"   设备: {cameraInfo.Device}");
                Console.WriteLine($"   平台: {cameraInfo.Platform}");
                Console.WriteLine($"   状态: {(cameraInfo.IsAvailable ? "可用" : "不可用")}");
                Console.WriteLine($"   支持格式: {string.Join(", ", cameraInfo.SupportedFormats)}");
                Console.WriteLine();

                // 测试 4: 获取支持的分辨率
                Console.WriteLine("📐 支持的分辨率:");
                var resolutions = await cameraService.GetSupportedResolutionsAsync();
                foreach (var resolution in resolutions)
                {
                    Console.WriteLine($"   - {resolution}");
                }
                Console.WriteLine();

                // 测试 5: 拍照测试
                if (isAvailable)
                {
                    Console.WriteLine("📸 拍照测试...");
                    var settings = new CameraSettings
                    {
                        Width = 640,
                        Height = 480,
                        JpegQuality = 85,
                        AddTimestamp = true
                    };

                    try
                    {
                        var imageBytes = await cameraService.CapturePhotoAsync(settings);
                        Console.WriteLine($"   ✅ 拍照成功! 图片大小: {imageBytes.Length} 字节");
                        
                        // 保存图片到临时文件
                        var tempFile = Path.Combine(Path.GetTempPath(), $"camera_test_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");
                        await File.WriteAllBytesAsync(tempFile, imageBytes);
                        Console.WriteLine($"   📁 图片已保存到: {tempFile}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"   ❌ 拍照失败: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine("⏩ 跳过拍照测试（相机不可用）");
                }
                Console.WriteLine();

                // 测试 6: MCP 设备集成测试
                Console.WriteLine("🔗 MCP 设备集成测试...");
                try
                {
                    // 创建一个模拟的 MCP 服务器用于测试
                    var mcpLogger = serviceProvider.GetRequiredService<ILogger<EnhancedMcpCameraDevice>>();
                    var mockMcpServer = new MockMcpServer();
                    
                    var mcpCameraDevice = new EnhancedMcpCameraDevice(mockMcpServer, cameraService, mcpLogger);
                    
                    Console.WriteLine("   ✅ MCP 相机设备创建成功!");
                    Console.WriteLine($"   设备ID: {mcpCameraDevice.DeviceId}");
                    Console.WriteLine($"   设备名称: {mcpCameraDevice.Name}");
                    Console.WriteLine($"   设备描述: {mcpCameraDevice.Description}");
                    
                    // 测试 MCP 工具注册
                    Console.WriteLine("   📋 注册 MCP 工具...");
                    // RegisterTools() 是 protected，我们通过创建设备时自动调用
                    
                    Console.WriteLine("   ✅ MCP 工具注册完成！");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"   ❌ MCP 设备创建失败: {ex.Message}");
                }
                Console.WriteLine();

                Console.WriteLine("✨ 测试完成!");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "测试过程中发生错误");
                Console.WriteLine($"❌ 测试失败: {ex.Message}");
            }

            Console.WriteLine();
            Console.WriteLine("按任意键退出...");
            Console.ReadKey();
        }
    }

    // 简单的 Mock MCP 服务器用于测试
    public class MockMcpServer : McpServer
    {
        public MockMcpServer() : base(null)
        {
        }

        public new void AddTool(string name, string description, McpPropertyList properties, Func<McpPropertyList, Task<McpReturnValue>> handler)
        {
            Console.WriteLine($"   📋 注册 MCP 工具: {name} - {description}");
            base.AddTool(name, description, properties, handler);
        }
    }
}
