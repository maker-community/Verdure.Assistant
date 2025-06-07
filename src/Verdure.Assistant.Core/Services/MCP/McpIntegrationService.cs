using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Verdure.Assistant.Core.Services.MCP
{
    /// <summary>
    /// Integration service that bridges MCP IoT devices with the voice chat system
    /// Provides backward compatibility and seamless integration
    /// </summary>
    public class McpIntegrationService : IDisposable
    {
        private readonly ILogger<McpIntegrationService> _logger;
        private readonly McpDeviceManager _deviceManager;
        private readonly McpServer _mcpServer;

        public McpIntegrationService(
            ILogger<McpIntegrationService> logger,
            McpDeviceManager deviceManager,
            McpServer mcpServer)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _deviceManager = deviceManager ?? throw new ArgumentNullException(nameof(deviceManager));
            _mcpServer = mcpServer ?? throw new ArgumentNullException(nameof(mcpServer));
        }

        /// <summary>
        /// Initialize the integration service
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                // Initialize MCP server
                await _mcpServer.InitializeAsync();

                // Initialize device manager
                await _deviceManager.InitializeAsync();

                _logger.LogInformation("MCP Integration Service initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize MCP Integration Service");
                throw;
            }
        }

        public List<SimpleMcpTool> GetAllSimpleMcpTools()
        {
            try
            {
                var tools = new List<SimpleMcpTool>();
                var toolList = _deviceManager.GetAllTools();

                foreach (var tool in toolList)
                {
                    tools.Add(new SimpleMcpTool
                    {
                        Name = tool.Name,
                        Description = tool.Description,
                        InputSchema = tool.InputSchema,
                    });
                }

                _logger.LogDebug($"Converted {tools.Count} MCP tools to {tools.Count} tools");
                return tools;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get available functions");
                return new List<SimpleMcpTool>();
            }
        }

        /// <summary>
        /// Get available IoT functions for voice chat integration
        /// Converts MCP tools to voice chat function descriptions
        /// </summary>
        public List<VoiceChatFunction> GetAvailableFunctions()
        {
            try
            {
                var functions = new List<VoiceChatFunction>();
                var tools = _deviceManager.GetAllTools();

                foreach (var tool in tools)
                {
                    functions.Add(new VoiceChatFunction
                    {
                        Name = tool.Name,
                        Description = tool.Description,
                        Parameters = ConvertMcpParametersToFunctionParameters(tool.Properties.ToDictionary(p => p.Name, p => p))
                    });
                }

                _logger.LogDebug($"Converted {tools.Count} MCP tools to {functions.Count} voice chat functions");
                return functions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get available functions");
                return new List<VoiceChatFunction>();
            }
        }

        /// <summary>
        /// Execute a function call from voice chat system
        /// </summary>
        public async Task<string> ExecuteFunctionAsync(string functionName, Dictionary<string, object>? parameters = null)
        {
            try
            {
                _logger.LogInformation($"Executing function '{functionName}' via MCP");

                var result = await _deviceManager.ExecuteToolAsync(functionName, parameters);

                if (result.IsError)
                {
                    var errorMessage = result.Content?.FirstOrDefault()?.Text ?? "Unknown error occurred";
                    _logger.LogError($"Function execution failed: {errorMessage}");
                    return $"Error: {errorMessage}";
                }

                // Convert MCP result to simple string response for voice chat
                var response = ConvertMcpResultToResponse(result);
                _logger.LogDebug($"Function '{functionName}' executed successfully: {response}");

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to execute function '{functionName}'");
                return $"Error executing {functionName}: {ex.Message}";
            }
        }

        /// <summary>
        /// Get current device states for context
        /// </summary>
        public Dictionary<string, object> GetDeviceStates()
        {
            try
            {
                return _deviceManager.GetDeviceStatuses();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get device states");
                return new Dictionary<string, object>();
            }
        }

        /// <summary>
        /// Handle direct MCP JSON-RPC requests (for advanced scenarios)
        /// </summary>
        public async Task<string> HandleMcpRequestAsync(string jsonRequest)
        {
            try
            {
                return await _mcpServer.HandleRequestAsync(jsonRequest);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to handle MCP request");
                return JsonSerializer.Serialize(new
                {
                    jsonrpc = "2.0",
                    error = new
                    {
                        code = -32603,
                        message = "Internal error",
                        data = ex.Message
                    }
                });
            }
        }

        /// <summary>
        /// Convert MCP properties to voice chat function parameters
        /// </summary>
        private Dictionary<string, object> ConvertMcpParametersToFunctionParameters(Dictionary<string, McpProperty> mcpProperties)
        {
            var parameters = new Dictionary<string, object>
            {
                ["type"] = "object",
                ["properties"] = new Dictionary<string, object>(),
                ["required"] = new List<string>()
            };

            var properties = (Dictionary<string, object>)parameters["properties"];
            var required = (List<string>)parameters["required"];

            foreach (var prop in mcpProperties)
            {
                var propertyDef = new Dictionary<string, object>
                {
                    ["type"] = prop.Value.Type switch
                    {
                        McpPropertyType.Boolean => "boolean",
                        McpPropertyType.Integer => "integer",
                        McpPropertyType.String => "string",
                        _ => "string"
                    },
                    ["description"] = prop.Value.Description ?? ""
                };

                // Add constraints for integers
                if (prop.Value.Type == McpPropertyType.Integer)
                {
                    if (prop.Value.Minimum.HasValue)
                        propertyDef["minimum"] = prop.Value.Minimum.Value;
                    if (prop.Value.Maximum.HasValue)
                        propertyDef["maximum"] = prop.Value.Maximum.Value;
                }

                // Add enum values if present
                if (prop.Value.EnumValues?.Any() == true)
                {
                    propertyDef["enum"] = prop.Value.EnumValues.ToList();
                }

                properties[prop.Key] = propertyDef;

                if (prop.Value.Required)
                {
                    required.Add(prop.Key);
                }
            }

            return parameters;
        }

        /// <summary>
        /// Convert MCP tool result to simple string response
        /// </summary>
        private string ConvertMcpResultToResponse(McpToolCallResult result)
        {
            if (result.Content == null || !result.Content.Any())
            {
                return "Operation completed successfully";
            }

            var textContents = result.Content
                .Where(c => c.Type == "text")
                .Select(c => c.Text)
                .Where(t => !string.IsNullOrEmpty(t));

            return string.Join(". ", textContents);
        }

        /// <summary>
        /// Get device information for voice chat context
        /// </summary>
        public string GetDeviceContextForChat()
        {
            try
            {
                var devices = _deviceManager.Devices.Values.ToList();
                if (!devices.Any())
                {
                    return "No IoT devices are currently available.";
                }

                var deviceInfo = devices.Select(d => $"{d.Name} ({d.DeviceId})").ToList();
                var availableFunctions = GetAvailableFunctions().Select(f => f.Name).ToList();

                return $"Available IoT devices: {string.Join(", ", deviceInfo)}. " +
                       $"Available functions: {string.Join(", ", availableFunctions)}.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get device context");
                return "IoT device information is currently unavailable.";
            }
        }

        /// <summary>
        /// Register a tool from the MCP server response
        /// </summary>
        public async Task RegisterToolAsync(string toolName, string description, string toolDefinition)
        {
            try
            {
                _logger.LogDebug("Registering tool: {ToolName}", toolName);

                // Parse the tool definition and register it with the device manager
                var toolElement = JsonSerializer.Deserialize<JsonElement>(toolDefinition);

                // Create a basic tool registration - this would be expanded based on actual MCP tool schema
                var tool = new McpTool
                {
                    Name = toolName,
                    Description = description,
                    Properties = new McpPropertyList(ExtractToolProperties(toolElement))
                };

                // For now, just log the registration since the actual device manager method signature may differ
                _logger.LogInformation("Tool registered locally: {ToolName} - {Description}", toolName, description);

                await Task.CompletedTask; // Placeholder for actual async registration
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to register tool: {ToolName}", toolName);
                throw;
            }
        }        /// <summary>
                 /// Handle successful tool call completion
                 /// </summary>
        public async Task OnToolCallCompletedAsync(string toolName, string result)
        {
            try
            {
                _logger.LogDebug("Tool call completed: {ToolName}", toolName);

                // Log successful tool execution - device manager integration would be added here
                _logger.LogInformation("Tool execution successful: {ToolName}", toolName);

                await Task.CompletedTask; // Placeholder for actual async processing
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process tool call completion for: {ToolName}", toolName);
            }
        }

        /// <summary>
        /// Handle failed tool call
        /// </summary>
        public async Task OnToolCallFailedAsync(string toolName, string error)
        {
            try
            {
                _logger.LogWarning("Tool call failed: {ToolName}, Error: {Error}", toolName, error);

                // Log failure - device manager integration would be added here  
                _logger.LogWarning("Tool execution failed: {ToolName}", toolName);

                await Task.CompletedTask; // Placeholder for actual async processing
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process tool call failure for: {ToolName}", toolName);
            }
        }

        /// <summary>
        /// Update device state based on tool execution results
        /// </summary>
        public async Task UpdateDeviceStateAsync(string deviceName, string property, object value)
        {
            try
            {
                _logger.LogDebug("Updating device state: {DeviceName}.{Property} = {Value}", deviceName, property, value);

                // Log state update - actual device state management would be implemented here
                _logger.LogInformation("Device state updated: {DeviceName}.{Property} = {Value}", deviceName, property, value);

                await Task.CompletedTask; // Placeholder for actual async state update
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update device state: {DeviceName}.{Property}", deviceName, property);
            }
        }

        /// <summary>
        /// Extract tool properties from JSON element
        /// </summary>
        private List<McpProperty> ExtractToolProperties(JsonElement toolElement)
        {
            var properties = new List<McpProperty>();

            try
            {
                if (toolElement.TryGetProperty("inputSchema", out var schemaElement) &&
                    schemaElement.TryGetProperty("properties", out var propsElement))
                {
                    foreach (var prop in propsElement.EnumerateObject())
                    {
                        var property = new McpProperty
                        {
                            Name = prop.Name,
                            Type = McpPropertyType.String, // Default type
                            Description = ""
                        };

                        if (prop.Value.TryGetProperty("type", out var typeElement))
                        {
                            property.Type = typeElement.GetString() switch
                            {
                                "boolean" => McpPropertyType.Boolean,
                                "integer" => McpPropertyType.Integer,
                                "string" => McpPropertyType.String,
                                _ => McpPropertyType.String
                            };
                        }

                        if (prop.Value.TryGetProperty("description", out var descElement))
                        {
                            property.Description = descElement.GetString() ?? "";
                        }

                        properties.Add(property);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract tool properties, using empty list");
            }

            return properties;
        }

        #region IDisposable Support

        private bool _disposed = false;

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Dispose managed resources if needed
                    _logger?.LogInformation("MCP Integration Service disposed");
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        #endregion
    }

    /// <summary>
    /// Voice chat function definition for backward compatibility
    /// </summary>
    public class VoiceChatFunction
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public Dictionary<string, object> Parameters { get; set; } = new();
    }
}
