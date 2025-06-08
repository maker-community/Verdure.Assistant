using System;
using System.Text.Json;
using System.Threading.Tasks;

class ResponseHandlingTest
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== MCP Response Handling Test ===");
        Console.WriteLine();

        try
        {
            // Test 1: Initialization Response Processing
            await TestInitializationResponse();
            
            // Test 2: Tools List Response Processing  
            await TestToolsListResponse();
            
            // Test 3: Tool Call Response Processing
            await TestToolCallResponse();
            
            // Test 4: Error Response Processing
            await TestErrorResponse();
            
            Console.WriteLine("✅ All MCP response handling tests completed successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Test failed: {ex}");
        }
    }

    private static async Task TestInitializationResponse()
    {
        Console.WriteLine("📋 Test 1: MCP Initialization Response Processing");
        
        // Simulate server initialization response
        var initResponse = new
        {
            jsonrpc = "2.0",
            id = 1,
            result = new
            {
                protocolVersion = "2024-11-05",
                capabilities = new
                {
                    tools = new { },
                    logging = new { }
                },
                serverInfo = new
                {
                    name = "xiaozhi-esp32",
                    version = "1.0.0"
                }
            }
        };

        var responseJson = JsonSerializer.Serialize(initResponse);
        Console.WriteLine($"   Server Response: {responseJson}");
        
        // Test response parsing
        var responseElement = JsonSerializer.Deserialize<JsonElement>(responseJson);
        if (responseElement.TryGetProperty("result", out var resultElement))
        {
            Console.WriteLine("   ✅ Initialization response properly parsed");
            
            if (resultElement.TryGetProperty("capabilities", out var capElement))
            {
                Console.WriteLine("   ✅ Server capabilities extracted");
            }
        }
        
        Console.WriteLine();
    }

    private static async Task TestToolsListResponse()
    {
        Console.WriteLine("🔧 Test 2: Tools List Response Processing");
        
        // Simulate tools list response from xiaozhi-esp32
        var toolsResponse = new
        {
            jsonrpc = "2.0",
            id = 2,
            result = new
            {
                tools = new[]
                {
                    new
                    {
                        name = "self.lamp.turn_on",
                        description = "Turn on the smart lamp",
                        inputSchema = new
                        {
                            type = "object",
                            properties = new
                            {
                                brightness = new
                                {
                                    type = "integer",
                                    description = "Brightness level (0-100)",
                                    minimum = 0,
                                    maximum = 100
                                }
                            }
                        }
                    },
                    new
                    {
                        name = "self.lamp.turn_off", 
                        description = "Turn off the smart lamp",
                        inputSchema = new
                        {
                            type = "object",
                            properties = new { }
                        }
                    }
                }
            }
        };

        var responseJson = JsonSerializer.Serialize(toolsResponse);
        Console.WriteLine($"   Server Response: {responseJson}");
        
        // Test tools extraction
        var responseElement = JsonSerializer.Deserialize<JsonElement>(responseJson);
        if (responseElement.TryGetProperty("result", out var resultElement) &&
            resultElement.TryGetProperty("tools", out var toolsElement))
        {
            var toolsArray = toolsElement.EnumerateArray();
            var toolCount = 0;
            
            foreach (var toolElement in toolsArray)
            {
                if (toolElement.TryGetProperty("name", out var nameElement) &&
                    toolElement.TryGetProperty("description", out var descElement))
                {
                    var toolName = nameElement.GetString();
                    var toolDescription = descElement.GetString();
                    Console.WriteLine($"   ✅ Tool parsed: {toolName} - {toolDescription}");
                    toolCount++;
                }
            }
            
            Console.WriteLine($"   ✅ Successfully processed {toolCount} tools");
        }
        
        Console.WriteLine();
    }

    private static async Task TestToolCallResponse()
    {
        Console.WriteLine("⚡ Test 3: Tool Call Response Processing");
        
        // Simulate successful tool call response
        var toolCallResponse = new
        {
            jsonrpc = "2.0",
            id = 3,
            result = new
            {
                content = new[]
                {
                    new
                    {
                        type = "text",
                        text = "Smart lamp turned on with brightness 75"
                    }
                }
            }
        };

        var responseJson = JsonSerializer.Serialize(toolCallResponse);
        Console.WriteLine($"   Server Response: {responseJson}");
        
        // Test tool call result processing
        var responseElement = JsonSerializer.Deserialize<JsonElement>(responseJson);
        if (responseElement.TryGetProperty("result", out var resultElement))
        {
            Console.WriteLine("   ✅ Tool call succeeded");
            
            if (resultElement.TryGetProperty("content", out var contentElement))
            {
                var contentArray = contentElement.EnumerateArray();
                foreach (var contentItem in contentArray)
                {
                    if (contentItem.TryGetProperty("type", out var typeElement) && 
                        typeElement.GetString() == "text" &&
                        contentItem.TryGetProperty("text", out var textElement))
                    {
                        var resultText = textElement.GetString();
                        Console.WriteLine($"   ✅ Result content: {resultText}");
                        
                        // Test device state extraction
                        if (resultText?.Contains("brightness") == true)
                        {
                            var match = System.Text.RegularExpressions.Regex.Match(resultText, @"brightness.*?(\d+)", 
                                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                            
                            if (match.Success && int.TryParse(match.Groups[1].Value, out var brightness))
                            {
                                Console.WriteLine($"   ✅ Extracted brightness value: {brightness}");
                            }
                        }
                    }
                }
            }
        }
        
        Console.WriteLine();
    }

    private static async Task TestErrorResponse()
    {
        Console.WriteLine("❌ Test 4: Error Response Processing");
        
        // Simulate error response
        var errorResponse = new
        {
            jsonrpc = "2.0",
            id = 4,
            error = new
            {
                code = -32602,
                message = "Invalid parameters",
                data = "Brightness value must be between 0 and 100"
            }
        };

        var responseJson = JsonSerializer.Serialize(errorResponse);
        Console.WriteLine($"   Server Response: {responseJson}");
        
        // Test error handling
        var responseElement = JsonSerializer.Deserialize<JsonElement>(responseJson);
        if (responseElement.TryGetProperty("error", out var errorElement))
        {
            Console.WriteLine("   ✅ Error response detected");
            
            if (errorElement.TryGetProperty("message", out var messageElement))
            {
                var errorMessage = messageElement.GetString();
                Console.WriteLine($"   ✅ Error message extracted: {errorMessage}");
            }
            
            if (errorElement.TryGetProperty("code", out var codeElement))
            {
                var errorCode = codeElement.GetInt32();
                Console.WriteLine($"   ✅ Error code extracted: {errorCode}");
            }
        }
        
        Console.WriteLine();
    }
}
