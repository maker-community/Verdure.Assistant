using System.Diagnostics;
using System.Runtime.InteropServices;
using Verdure.Assistant.Api.Models;

namespace Verdure.Assistant.Api.Services.WiFi;

/// <summary>
/// WiFi网络管理器 - 负责热点创建和WiFi连接
/// </summary>
public class WiFiNetworkManager
{
    private readonly ILogger<WiFiNetworkManager> _logger;
    private readonly DeviceConfig _config;
    private readonly string _interface;

    public WiFiNetworkManager(ILogger<WiFiNetworkManager> logger, DeviceConfig config)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _interface = _config.ApConfig.Interface;
    }

    /// <summary>
    /// 异步执行nmcli命令
    /// </summary>
    private async Task<CommandResult> RunNmcliCommandAsync(string arguments, int timeoutSeconds = 30)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            _logger.LogWarning("非Linux系统，跳过nmcli命令执行: {Arguments}", arguments);
            return new CommandResult { Success = false, Output = "非Linux系统", Error = "仅支持Linux系统" };
        }

        try
        {
            var fullCommand = $"sudo nmcli {arguments}";
            
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{fullCommand.Replace("\"", "\\\"")}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            var allTask = Task.WhenAll(outputTask, errorTask);
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));

            var completed = await Task.WhenAny(allTask, timeoutTask);

            if (completed == allTask)
            {
                await process.WaitForExitAsync();
                var output = await outputTask;
                var error = await errorTask;

                var result = new CommandResult
                {
                    Success = process.ExitCode == 0,
                    Output = output,
                    Error = error,
                    ExitCode = process.ExitCode
                };

                if (!result.Success && !string.IsNullOrEmpty(error))
                {
                    _logger.LogError("nmcli命令执行失败: {Error}", error);
                }

                return result;
            }
            else
            {
                // 超时处理
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill();
                    }
                }
                catch { }

                return new CommandResult
                {
                    Success = false,
                    Output = "",
                    Error = $"命令执行超时({timeoutSeconds}秒)",
                    ExitCode = -1
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行nmcli命令时出错");
            return new CommandResult
            {
                Success = false,
                Output = "",
                Error = ex.Message,
                ExitCode = -1
            };
        }
    }

    /// <summary>
    /// 启动WiFi热点
    /// </summary>
    public async Task<bool> StartHotspotAsync(string ssid, string password)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            _logger.LogInformation("非Linux系统，模拟启动WiFi热点: {Ssid}", ssid);
            return true;
        }

        _logger.LogInformation("正在使用nmcli启动WiFi热点: {Ssid}", ssid);
        _logger.LogInformation("使用配置的IP地址: {Ip}", _config.ApConfig.Ip);
        _logger.LogInformation("使用配置的DHCP范围: {DhcpStart} - {DhcpEnd}", _config.ApConfig.DhcpStart, _config.ApConfig.DhcpEnd);

        try
        {
            // 停止任何可能正在运行的热点
            await StopHotspotAsync();

            // 确保设备被NetworkManager管理
            await SetDeviceManagedAsync(true);

            // 删除可能存在的相同名称的连接
            var deleteCmd = $"connection delete {ssid}";
            await RunNmcliCommandAsync(deleteCmd);

            // 创建新的热点连接
            var createHotspotCmd = $"device wifi hotspot ifname {_interface} con-name {ssid} ssid \"{ssid}\" password \"{password}\"";
            var result = await RunNmcliCommandAsync(createHotspotCmd);

            if (!result.Success)
            {
                _logger.LogError("创建WiFi热点失败: {Error}", result.Error);
                return false;
            }

            // 设置IP地址和掩码
            var ipCmd = $"connection modify {ssid} ipv4.addresses {_config.ApConfig.Ip}/24";
            var ipResult = await RunNmcliCommandAsync(ipCmd);

            if (!ipResult.Success)
            {
                _logger.LogWarning("设置IP地址失败: {Error}", ipResult.Error);
            }

            // 设置为手动IP模式
            var methodCmd = $"connection modify {ssid} ipv4.method manual";
            var methodResult = await RunNmcliCommandAsync(methodCmd);

            if (!methodResult.Success)
            {
                _logger.LogWarning("设置IP模式失败: {Error}", methodResult.Error);
            }

            // 启用DHCP服务器
            var dhcpCmd = $"connection modify {ssid} ipv4.dhcp-range \"{_config.ApConfig.DhcpStart},{_config.ApConfig.DhcpEnd}\"";
            var dhcpResult = await RunNmcliCommandAsync(dhcpCmd);

            if (!dhcpResult.Success)
            {
                _logger.LogWarning("设置DHCP范围失败: {Error}", dhcpResult.Error);
            }

            // 重新应用配置
            var upCmd = $"connection up {ssid}";
            var upResult = await RunNmcliCommandAsync(upCmd);

            if (!upResult.Success)
            {
                _logger.LogError("启动WiFi热点失败: {Error}", upResult.Error);
                return false;
            }

            _logger.LogInformation("WiFi热点启动成功: {Ssid}", ssid);
            _logger.LogInformation("热点IP: {Ip}", _config.ApConfig.Ip);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动WiFi热点时出错");
            return false;
        }
    }

    /// <summary>
    /// 停止WiFi热点
    /// </summary>
    public async Task<bool> StopHotspotAsync()
    {
        return await StopHotspotWithNmcliAsync(_config.ApConfig.Ssid);
    }

    /// <summary>
    /// 连接到WiFi网络
    /// </summary>
    public async Task<bool> ConnectToWifiAsync(string ssid, string password)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            _logger.LogInformation("非Linux系统，模拟连接WiFi: {Ssid}", ssid);
            return true;
        }

        _logger.LogInformation("正在连接到WiFi: {Ssid}", ssid);

        var connectCmd = $"device wifi connect \"{ssid}\" password \"{password}\" ifname {_interface}";
        var result = await RunNmcliCommandAsync(connectCmd);

        if (result.Success)
        {
            _logger.LogInformation("WiFi连接成功: {Ssid}", ssid);
            _logger.LogDebug("连接输出: {Output}", result.Output);
        }
        else
        {
            _logger.LogError("WiFi连接失败: {Error}", result.Error);
        }

        return result.Success;
    }

    /// <summary>
    /// 设置设备管理状态
    /// </summary>
    public async Task<bool> SetDeviceManagedAsync(bool managed)
    {
        var managedState = managed ? "yes" : "no";
        _logger.LogInformation("正在设置设备管理状态: {Interface} -> {ManagedState}", _interface, managedState);

        var setCmd = $"device set {_interface} managed {managedState}";
        var result = await RunNmcliCommandAsync(setCmd);

        return result.Success;
    }

    /// <summary>
    /// 连接设备
    /// </summary>
    public async Task<bool> ConnectDeviceAsync()
    {
        _logger.LogInformation("正在连接设备: {Interface}", _interface);

        var connectCmd = $"device connect {_interface}";
        var result = await RunNmcliCommandAsync(connectCmd);

        return result.Success;
    }

    /// <summary>
    /// 使用nmcli关闭WiFi热点
    /// </summary>
    private async Task<bool> StopHotspotWithNmcliAsync(string ssid)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            _logger.LogInformation("非Linux系统，模拟关闭WiFi热点: {Ssid}", ssid);
            return true;
        }

        _logger.LogInformation("正在关闭nmcli WiFi热点: {Ssid}", ssid);

        try
        {
            // 关闭连接
            var downCmd = $"connection down {ssid}";
            await RunNmcliCommandAsync(downCmd);

            // 删除连接
            var deleteCmd = $"connection delete {ssid}";
            var result = await RunNmcliCommandAsync(deleteCmd);

            if (!result.Success)
            {
                _logger.LogError("关闭WiFi热点失败: {Error}", result.Error);
                return false;
            }

            _logger.LogInformation("WiFi热点已关闭: {Ssid}", ssid);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "关闭WiFi热点时出错");
            return false;
        }
    }

    /// <summary>
    /// 检查网络连接状态
    /// </summary>
    public async Task<bool> IsNetworkAvailableAsync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            _logger.LogInformation("非Linux系统，模拟网络检查结果: 未连接");
            return false; // 非Linux系统默认启动配网模式用于测试
        }

        try
        {
            // 检查网络连接状态
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(5);
            var response = await httpClient.GetAsync("http://www.google.com");
            var isConnected = response.IsSuccessStatusCode;

            _logger.LogInformation("网络连接状态: {Status}", isConnected ? "已连接" : "未连接");
            return isConnected;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "网络连接检查失败，假定网络不可用");
            return false;
        }
    }

    /// <summary>
    /// 获取当前WiFi连接的IP地址
    /// </summary>
    public async Task<string?> GetWiFiConnectedIpAddressAsync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            _logger.LogInformation("非Linux系统，模拟IP地址: 192.168.1.100");
            return "192.168.1.100";
        }

        try
        {
            var result = await RunNmcliCommandAsync($"device show {_interface}");
            if (result.Success)
            {
                var lines = result.Output.Split('\n');
                foreach (var line in lines)
                {
                    if (line.Contains("IP4.ADDRESS[1]"))
                    {
                        var parts = line.Split(':');
                        if (parts.Length > 1)
                        {
                            var ipWithMask = parts[1].Trim();
                            var ip = ipWithMask.Split('/')[0];
                            return ip;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取WiFi IP地址失败");
        }

        return null;
    }

    /// <summary>
    /// 重启系统
    /// </summary>
    public async Task RebootAsync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            _logger.LogInformation("非Linux系统，模拟系统重启");
            return;
        }

        _logger.LogInformation("执行系统重启...");
        var result = await RunNmcliCommandAsync("reboot", 5);
        
        if (!result.Success)
        {
            _logger.LogError("重启命令执行失败: {Error}", result.Error);
        }
    }
}

/// <summary>
/// 命令执行结果
/// </summary>
public class CommandResult
{
    public bool Success { get; set; }
    public string Output { get; set; } = "";
    public string Error { get; set; } = "";
    public int ExitCode { get; set; }
}