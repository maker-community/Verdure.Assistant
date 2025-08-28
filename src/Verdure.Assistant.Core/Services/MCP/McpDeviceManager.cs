using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Models;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Services;

namespace Verdure.Assistant.Core.Services.MCP
{
    /// <summary>
    /// MCP-based IoT device manager that replaces the traditional IoTDeviceManager
    /// with MCP protocol support for device discovery, tool registration, and state management
    /// </summary>
    public class McpDeviceManager : IDisposable
    {
        private readonly ILogger<McpDeviceManager> _logger;
        private readonly McpServer _mcpServer;
        private readonly IMusicPlayerService? _musicPlayerService;
        private readonly Dictionary<string, McpIoTDevice> _devices;
        private readonly Dictionary<string, McpTool> _deviceTools;
        private bool _disposed = false;

        public event EventHandler<McpDeviceStateChangedEventArgs>? DeviceStateChanged;
        public event EventHandler<string>? DeviceAdded;
        public event EventHandler<string>? DeviceRemoved;

        public IReadOnlyDictionary<string, McpIoTDevice> Devices => _devices;
        public IReadOnlyDictionary<string, McpTool> DeviceTools => _deviceTools;

        public McpDeviceManager(ILogger<McpDeviceManager> logger, McpServer mcpServer, IMusicPlayerService? musicPlayerService = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _mcpServer = mcpServer ?? throw new ArgumentNullException(nameof(mcpServer));
            _musicPlayerService = musicPlayerService;
            _devices = new Dictionary<string, McpIoTDevice>();
            _deviceTools = new Dictionary<string, McpTool>();

            _logger.LogInformation("MCP Device Manager initialized with music service: {HasMusicService}", _musicPlayerService != null);
        }

        /// <summary>
        /// Initialize the device manager and discover available devices
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                _logger.LogInformation("Starting MCP device discovery...");

                // Initialize default devices based on xiaozhi-esp32 pattern
                await InitializeDefaultDevicesAsync();

                // Register all device tools with MCP server
                await RegisterDeviceToolsAsync();

                _logger.LogInformation($"MCP device manager initialized with {_devices.Count} devices and {_deviceTools.Count} tools");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize MCP device manager");
                throw;
            }
        }        /// <summary>
        /// Initialize default devices similar to xiaozhi-esp32's thing_manager
        /// </summary>
        private async Task InitializeDefaultDevicesAsync()
        {
            // Create basic IoT devices with real services
            var lamp = new McpLampDevice(_mcpServer, null);
            var speaker = new McpSpeakerDevice(_mcpServer, null);
            var musicPlayer = new McpMusicPlayerDevice(_mcpServer, _musicPlayerService, null);
            
            // 使用新的增强相机设备替代旧的 McpCameraDevice
            // var camera = new McpCameraDevice(_mcpServer, null);
            
            // 创建增强相机设备（需要相机服务）
            // 注意：这里需要从DI容器获取ICameraService，但为了保持兼容性，我们将在外部处理

            // Add devices
            //await AddDeviceAsync(lamp);
            //await AddDeviceAsync(speaker);
            await AddDeviceAsync(musicPlayer);
            // await AddDeviceAsync(camera); // 旧相机设备已禁用
            
            _logger.LogInformation("Default MCP devices initialized - Music service available: {HasMusicService}, Enhanced camera will be added separately", 
                _musicPlayerService != null);
        }

        /// <summary>
        /// 添加增强相机设备（需要外部传入ICameraService）
        /// </summary>
        public async Task AddEnhancedCameraDeviceAsync(ICameraService cameraService)
        {
            try
            {
                // 传递null作为logger，EnhancedMcpCameraDevice会处理null logger
                var enhancedCamera = new EnhancedMcpCameraDevice(_mcpServer, cameraService, null);
                await AddDeviceAsync(enhancedCamera);
                _logger.LogInformation("Enhanced camera device added successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to add enhanced camera device");
                throw;
            }
        }

        /// <summary>
        /// Add a new MCP IoT device to the manager
        /// </summary>
        public async Task AddDeviceAsync(McpIoTDevice device)
        {
            if (device == null)
                throw new ArgumentNullException(nameof(device));

            if (_devices.ContainsKey(device.DeviceId))
            {
                _logger.LogWarning($"Device {device.DeviceId} already exists, skipping");
                return;
            }

            try
            {
                // Subscribe to device state changes
                device.StateChanged += OnDeviceStateChanged;

                // Initialize device
                await device.InitializeAsync();

                // Add device to collection
                _devices[device.DeviceId] = device;

                // Register device tools
                foreach (var tool in device.GetTools())
                {
                    var toolKey = $"{device.DeviceId}.{tool.Name}";
                    _deviceTools[toolKey] = tool;
                    _logger.LogDebug($"Registered tool: {toolKey}");
                }

                DeviceAdded?.Invoke(this, device.DeviceId);
                _logger.LogInformation($"Added MCP device: {device.DeviceId} ({device.Name})");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to add device {device.DeviceId}");
                throw;
            }
        }        /// <summary>
        /// Remove a device from the manager
        /// </summary>
        public async Task RemoveDeviceAsync(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId))
                throw new ArgumentNullException(nameof(deviceId));

            if (!_devices.TryGetValue(deviceId, out var device))
            {
                _logger.LogWarning($"Device {deviceId} not found for removal");
                return;
            }

            try
            {
                // Unsubscribe from events
                device.StateChanged -= OnDeviceStateChanged;

                // Remove device tools
                var toolsToRemove = _deviceTools.Keys
                    .Where(k => k.StartsWith(deviceId + "."))
                    .ToList();

                foreach (var toolKey in toolsToRemove)
                {
                    _deviceTools.Remove(toolKey);
                    _logger.LogDebug($"Unregistered tool: {toolKey}");
                }

                // Dispose device
                if (device is IDisposable disposable)
                {
                    disposable.Dispose();
                }                _devices.Remove(deviceId);
                DeviceRemoved?.Invoke(this, deviceId);
                _logger.LogInformation($"Removed MCP device: {deviceId}");
                
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to remove device {deviceId}");
                throw;
            }
        }

        /// <summary>
        /// Get a device by ID
        /// </summary>
        public McpIoTDevice? GetDevice(string deviceId)
        {
            return _devices.TryGetValue(deviceId, out var device) ? device : null;
        }

        /// <summary>
        /// Get all available MCP tools from all devices
        /// </summary>
        public List<McpTool> GetAllTools()
        {
            return _deviceTools.Values.ToList();
        }

        /// <summary>
        /// Execute a tool by name with parameters
        /// </summary>
        public async Task<McpToolCallResult> ExecuteToolAsync(string toolName, Dictionary<string, object>? parameters = null)
        {
            try
            {
                // Find the tool
                var toolEntry = _deviceTools.FirstOrDefault(kvp => 
                    kvp.Key.EndsWith("." + toolName) || kvp.Value.Name == toolName);

                if (toolEntry.Key == null)
                {
                    return new McpToolCallResult
                    {
                        IsError = true,
                        Content = new List<McpContent>
                        {
                            new McpContent { Type = "text", Text = $"Tool '{toolName}' not found" }
                        }
                    };
                }

                var tool = toolEntry.Value;
                var deviceId = toolEntry.Key.Split('.')[0];
                var device = _devices[deviceId];

                _logger.LogInformation($"Executing tool '{toolName}' on device '{deviceId}'");

                // Execute the tool
                var result = await tool.ExecuteAsync(parameters ?? new Dictionary<string, object>());

                _logger.LogDebug($"Tool '{toolName}' executed successfully");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to execute tool '{toolName}'");
                return new McpToolCallResult
                {
                    IsError = true,
                    Content = new List<McpContent>
                    {
                        new McpContent { Type = "text", Text = $"Tool execution failed: {ex.Message}" }
                    }
                };
            }
        }

        /// <summary>
        /// Register all device tools with the MCP server
        /// </summary>
        private async Task RegisterDeviceToolsAsync()
        {
            var allTools = GetAllTools();
            await _mcpServer.RegisterToolsAsync();
            _logger.LogInformation($"Registered {allTools.Count} tools with MCP server");
        }

        /// <summary>
        /// Handle device state changes and forward to MCP server
        /// </summary>
        private void OnDeviceStateChanged(object? sender, McpDeviceStateChangedEventArgs e)
        {
            try
            {
                _logger.LogDebug($"Device state changed: {e.DeviceName} - {e.PropertyName} = {e.NewValue}");
                
                // Forward state change to subscribers
                DeviceStateChanged?.Invoke(this, e);

                // Optionally send MCP notification
                // This could be implemented as part of MCP server notifications
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error handling state change for device {e.DeviceName}");
            }
        }

        /// <summary>
        /// Get device status for all devices
        /// </summary>
        public Dictionary<string, object> GetDeviceStatuses()
        {
            var statuses = new Dictionary<string, object>();

            foreach (var device in _devices.Values)
            {
                try
                {
                    statuses[device.DeviceId] = new
                    {
                        Name = device.Name,
                        Type = device.GetType().Name,
                        Properties = device.GetPropertyValues(),
                        IsOnline = true // Could be enhanced with actual connectivity status
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to get status for device {device.DeviceId}");
                    statuses[device.DeviceId] = new
                    {
                        Name = device.Name,
                        Type = device.GetType().Name,
                        Error = ex.Message,
                        IsOnline = false
                    };
                }
            }

            return statuses;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            try
            {
                // Dispose all devices
                foreach (var device in _devices.Values)
                {
                    try
                    {
                        device.StateChanged -= OnDeviceStateChanged;
                        if (device is IDisposable disposable)
                        {
                            disposable.Dispose();
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error disposing device {device.DeviceId}");
                    }
                }

                _devices.Clear();
                _deviceTools.Clear();

                _logger.LogInformation("MCP Device Manager disposed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during MCP Device Manager disposal");
            }

            _disposed = true;
        }
    }}
