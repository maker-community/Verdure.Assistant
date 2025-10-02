using System.Diagnostics;
using System.Net.NetworkInformation;
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
        if (!OperatingSystem.IsLinux())
        {
            _logger.LogWarning("非Linux系统，跳过nmcli命令执行");
            return new CommandResult { Success = false, Output = "非Linux系统" };
        }

        try
        {
            // 构建完整的命令，包含sudo
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

            // 修复：将任务存储在变量中，避免重复创建Task对象
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
                }; if (!result.Success && !string.IsNullOrEmpty(error))
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
    /// 异步重启系统
    /// </summary>
    public async Task RebootAsync()
    {
        if (!OperatingSystem.IsLinux())
        {
            Console.WriteLine("非Linux系统，跳过重启");
            return;
        }

        Console.WriteLine("执行系统重启...");
        await RunCommandAsync("sudo reboot");
    }

    /// <summary>
    /// 异步检查网络连接
    /// </summary>
    public async Task<bool> IsNetworkAvailableAsync()
    {
        try
        {
            var result = await RunCommandAsync("sudo ping -c 1 -W 1 8.8.8.8");
            if (!result.Success)
            {
                Console.WriteLine("网络连接检查失败: " + result.Error);
                return false;
            }
            Console.WriteLine("网络连接检查成功");
            return result.Success;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"检查网络连接时出错: {ex.Message}");
            return false;
        }
    }




    /// <summary>
    /// 检查WiFi接口是否已连接到网络
    /// </summary>
    public async Task<bool> IsWiFiConnectedAsync()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return false;
        }

        try
        {
            var result = await RunNmcliCommandAsync($"device status");
            if (result.Success)
            {
                var lines = result.Output.Split('\n');
                foreach (var line in lines)
                {
                    if (line.Contains(_interface) && line.Contains("connected") && !line.Contains("disconnected"))
                    {
                        return true;
                    }
                }
            }
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "检查WiFi连接状态失败");
            return false;
        }
    }

    /// <summary>
    /// 获取当前WiFi连接的IP地址
    /// </summary>
    public Task<string?> GetWiFiConnectedIpAddressAsync()
    {
        try
        {
            NetworkInterface[] networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();
            foreach (NetworkInterface netInterface in networkInterfaces)
            {
                if (netInterface.OperationalStatus != OperationalStatus.Up)
                    continue;
                if (netInterface.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                // 查找无线网络接口
                bool isWireless = netInterface.Description.ToLower().Contains("wireless") ||
                                  netInterface.Name.ToLower().Contains("wlan") ||
                                  netInterface.Name.ToLower().Contains("wi-fi");

                if (isWireless)
                {
                    IPInterfaceProperties ipProps = netInterface.GetIPProperties();
                    foreach (UnicastIPAddressInformation addr in ipProps.UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        {
                            var ip = addr.Address.ToString();
                            // 排除热点IP段，只返回正常WiFi连接的IP
                            if (!ip.StartsWith("192.168.4.") &&
                                !ip.StartsWith("10.42.0.") &&
                                !ip.StartsWith("169.254.")) // 排除APIPA地址
                            {
                                return Task.FromResult<string?>(ip);
                            }
                        }
                    }
                }
            }
            return Task.FromResult<string?>(null);
        }
        catch
        {
            return Task.FromResult<string?>(null);
        }
    }

    /// <summary>
    /// 异步执行shell命令
    /// </summary>
    public static async Task<CommandResult> RunCommandAsync(string command, int timeoutSeconds = 30)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{command.Replace("\"", "\\\"")}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            // 修复：将任务存储在变量中，避免重复创建Task对象
            var allTask = Task.WhenAll(outputTask, errorTask);
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds));

            var completed = await Task.WhenAny(allTask, timeoutTask);

            if (completed == allTask)
            {
                await process.WaitForExitAsync();
                var output = await outputTask;
                var error = await errorTask;

                Console.WriteLine($"命令 '{command}' 执行完成，输出: {output}, 错误: {error}");
                var result = new CommandResult
                {
                    Success = process.ExitCode == 0,
                    Output = output,
                    Error = error,
                    ExitCode = process.ExitCode
                };

                if (!result.Success && !string.IsNullOrEmpty(error))
                {
                    Console.WriteLine($"命令 '{command}' 执行错误: {error}");
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
            Console.WriteLine($"执行命令 '{command}' 时出错: {ex.Message}");
            return new CommandResult
            {
                Success = false,
                Output = "",
                Error = ex.Message,
                ExitCode = -1
            };
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