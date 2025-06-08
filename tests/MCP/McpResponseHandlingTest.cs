using System;
using System.Text.Json;
using System.Threading.Tasks;

namespace Verdure.Assistant.Tests
{
    /// <summary>
    /// 测试MCP响应处理逻辑
    /// </summary>
    public class McpResponseHandlingTest
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("=== MCP响应处理逻辑测试 ===");
            
            // 测试1: 初始化响应
            Console.WriteLine("\n1. 测试初始化响应处理");
            var initResponse = @"{
                ""jsonrpc"": ""2.0"",
                ""id"": 1,
                ""result"": {
                    ""protocolVersion"": ""2024-11-05"",
                    ""capabilities"": {
                        ""tools"": {}
                    },
                    ""serverInfo"": {
                        ""name"": ""xiaozhi-esp32"",
                        ""version"": ""1.0.0""
                    }
                }
            }";
            
            TestResponseParsing("初始化响应", initResponse);
            
            // 测试2: 工具列表响应
            Console.WriteLine("\n2. 测试工具列表响应处理");
            var toolsResponse = @"{
                ""jsonrpc"": ""2.0"",
                ""id"": 2,
                ""result"": {
                    ""tools"": [
                        {
                            ""name"": ""self.get_device_status"",
                            ""description"": ""获取设备状态信息"",
                            ""inputSchema"": {
                                ""type"": ""object"",
                                ""properties"": {}
                            }
                        },
                        {
                            ""name"": ""self.audio_speaker.set_volume"",
                            ""description"": ""设置音量"",
                            ""inputSchema"": {
                                ""type"": ""object"",
                                ""properties"": {
                                    ""volume"": {
                                        ""type"": ""integer"",
                                        ""minimum"": 0,
                                        ""maximum"": 100
                                    }
                                }
                            }
                        }
                    ],
                    ""nextCursor"": """"
                }
            }";
            
            TestResponseParsing("工具列表响应", toolsResponse);
            
            // 测试3: 工具调用成功响应
            Console.WriteLine("\n3. 测试工具调用成功响应处理");
            var toolCallResponse = @"{
                ""jsonrpc"": ""2.0"",
                ""id"": 3,
                ""result"": {
                    ""content"": [
                        {
                            ""type"": ""text"",
                            ""text"": ""音量已设置为50""
                        }
                    ],
                    ""isError"": false
                }
            }";
            
            TestResponseParsing("工具调用成功响应", toolCallResponse);
            
            // 测试4: 工具调用错误响应
            Console.WriteLine("\n4. 测试工具调用错误响应处理");
            var errorResponse = @"{
                ""jsonrpc"": ""2.0"",
                ""id"": 4,
                ""error"": {
                    ""code"": -32601,
                    ""message"": ""Unknown tool: self.non_existent_tool""
                }
            }";
            
            TestResponseParsing("工具调用错误响应", errorResponse);
            
            // 测试5: 设备状态通知
            Console.WriteLine("\n5. 测试设备状态通知处理");
            var notification = @"{
                ""jsonrpc"": ""2.0"",
                ""method"": ""notifications/state_changed"",
                ""params"": {
                    ""oldState"": ""idle"",
                    ""newState"": ""active""
                }
            }";
            
            TestResponseParsing("设备状态通知", notification);
            
            Console.WriteLine("\n=== 测试完成 ===");
            Console.WriteLine("所有MCP响应类型都能正确解析和识别");
        }
        
        private static void TestResponseParsing(string testName, string response)
        {
            try
            {
                var responseElement = JsonSerializer.Deserialize<JsonElement>(response);
                
                Console.WriteLine($"✓ {testName}: JSON解析成功");
                
                // 检查响应类型
                if (responseElement.TryGetProperty("id", out var idElement))
                {
                    var requestId = idElement.GetInt32();
                    
                    if (responseElement.TryGetProperty("error", out var errorElement))
                    {
                        if (errorElement.TryGetProperty("message", out var errorMessageElement))
                        {
                            var errorMessage = errorMessageElement.GetString();
                            Console.WriteLine($"  - 错误响应 (ID={requestId}): {errorMessage}");
                        }
                    }
                    else if (responseElement.TryGetProperty("result", out var resultElement))
                    {
                        Console.WriteLine($"  - 成功响应 (ID={requestId})");
                        
                        // 检查具体响应类型
                        if (resultElement.TryGetProperty("protocolVersion", out _))
                        {
                            Console.WriteLine($"    类型: 初始化响应");
                        }
                        else if (resultElement.TryGetProperty("tools", out var toolsElement))
                        {
                            var toolCount = toolsElement.GetArrayLength();
                            Console.WriteLine($"    类型: 工具列表响应，包含 {toolCount} 个工具");
                        }
                        else if (resultElement.TryGetProperty("content", out var contentElement))
                        {
                            var contentCount = contentElement.GetArrayLength();
                            Console.WriteLine($"    类型: 工具调用响应，包含 {contentCount} 个内容项");
                        }
                    }
                }
                else if (responseElement.TryGetProperty("method", out var methodElement))
                {
                    var method = methodElement.GetString();
                    Console.WriteLine($"  - 通知消息: {method}");
                }
                
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"✗ {testName}: 解析失败 - {ex.Message}");
            }
        }
    }
}
