using Microsoft.Extensions.Logging;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Models;

namespace Verdure.Assistant.Core.Services;

/// <summary>
/// 配置管理服务，整合了OTA配置获取、设备验证和网络信息管理
/// </summary>
public interface IConfigurationService
{
    Task<bool> InitializeMqttInfoAsync();
    Task<OtaResponse?> CheckOtaUpdateAsync();
    Task<DeviceInfo> GetDeviceInfoAsync();
    Task<NetworkInfo?> GetNetworkInfoAsync();
    Task<string?> ExtractVerificationCodeAsync(string responseText);
    Task CopyToClipboardAsync(string text);
    Task OpenBrowserAsync(string url);
    
    string ClientId { get; }
    string DeviceId { get; }
    string UserAgent { get; }
    string CurrentVersion { get; }
    MqttConfiguration? MqttInfo { get; }
    string OtaVersionUrl { get; }
    string WebSocketUrl { get; }

    /// <summary>
    /// WebSocket 鉴权令牌：优先使用设置页填写的 Token，其次是 OTA 下发的 Token
    /// </summary>
    string AccessToken { get; }

    /// <summary>
    /// 从用户设置刷新 Device ID / Token / OTA 与 WebSocket 地址（设置页保存后无需重启即可生效）
    /// </summary>
    Task ReloadSettingsAsync();
    OtaResponse? LatestOtaResponse { get; }
    DateTime? LastOtaCheckTime { get; }
    bool IsActivated { get; }
    string? ActivationCode { get; }
    string? ActivationMessage { get; }
    
    event EventHandler<string>? VerificationCodeReceived;
    event EventHandler<OtaResponse?>? OtaCheckCompleted;
}

public class MqttConfiguration
{
    public string Endpoint { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string PublishTopic { get; set; } = string.Empty;
    public string SubscribeTopic { get; set; } = string.Empty;
}

public class ConfigurationService : IConfigurationService
{
    private readonly ILogger<ConfigurationService>? _logger;
    private readonly HttpClient _httpClient;

    // 基本属性
    public string ClientId { get; private set; }
    public string DeviceId { get; private set; }
    public string UserAgent { get; private set; }
    public string CurrentVersion { get; private set; } = "1.2.0";
    public MqttConfiguration? MqttInfo { get; private set; }
    public string OtaVersionUrl { get; private set; } = "https://api.tenclass.net/xiaozhi/ota/";
    public string WebSocketUrl { get; private set; } = "wss://api.tenclass.net/xiaozhi/v1/";

    // OTA相关属性
    public OtaResponse? LatestOtaResponse { get; private set; }
    public DateTime? LastOtaCheckTime { get; private set; }
    public string? ActivationCode { get; private set; }
    public string? ActivationMessage { get; private set; }
    public bool IsActivated => string.IsNullOrEmpty(ActivationCode);

    // 事件
    public event EventHandler<string>? VerificationCodeReceived;
    public event EventHandler<OtaResponse?>? OtaCheckCompleted;

    private const string LegacyDefaultWsAddress = "ws://localhost:8765";
    private const string FallbackAccessToken = "test-token";

    private readonly ISettingsService<AppSettings>? _settingsService;
    private readonly string _autoDeviceId;
    private string _settingsToken = string.Empty;
    private string _otaToken = string.Empty;

    public string AccessToken =>
        !string.IsNullOrWhiteSpace(_settingsToken) ? _settingsToken
        : !string.IsNullOrWhiteSpace(_otaToken) ? _otaToken
        : FallbackAccessToken;

    public ConfigurationService(ILogger<ConfigurationService>? logger = null,
        ISettingsService<AppSettings>? settingsService = null)
    {
        _logger = logger;
        _settingsService = settingsService;
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromSeconds(30);

        // 初始化设备信息
        ClientId = GenerateClientId();
        _autoDeviceId = GetMacAddress();
        DeviceId = _autoDeviceId;
        UserAgent = $"verdure-assistant/{CurrentVersion} ({Environment.OSVersion.Platform})";

        try
        {
            // 在线程池上同步等待，避免在 UI 线程上死锁
            Task.Run(ReloadSettingsAsync).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "读取用户设置失败，使用自动生成的设备信息");
        }

        _logger?.LogInformation("配置服务初始化完成 - DeviceId: {DeviceId}（{Source}）, ClientId: {ClientId}, Token: {TokenSource}, OTA: {Ota}, WS: {Ws}",
            DeviceId, DeviceId == _autoDeviceId ? "自动生成" : "来自设置", ClientId, DescribeTokenSource(), OtaVersionUrl, WebSocketUrl);
    }

    public async Task ReloadSettingsAsync()
    {
        if (_settingsService == null) return;

        var settings = await _settingsService.LoadSettingsAsync().ConfigureAwait(false);

        var deviceId = settings.DeviceId?.Trim();
        DeviceId = string.IsNullOrEmpty(deviceId) ? _autoDeviceId : deviceId;
        _settingsToken = settings.WsToken?.Trim() ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(settings.OtaAddress))
        {
            OtaVersionUrl = NormalizeUrl(settings.OtaProtocol, settings.OtaAddress);
        }

        if (!string.IsNullOrWhiteSpace(settings.WsAddress))
        {
            var wsUrl = NormalizeUrl(settings.WsProtocol, settings.WsAddress);
            if (!string.Equals(wsUrl.TrimEnd('/'), LegacyDefaultWsAddress, StringComparison.OrdinalIgnoreCase))
            {
                WebSocketUrl = wsUrl;
            }
        }
    }

    private static string NormalizeUrl(string? protocol, string address)
    {
        address = address.Trim();
        return address.Contains("://") ? address : $"{protocol}{address}";
    }

    private string DescribeTokenSource() =>
        !string.IsNullOrWhiteSpace(_settingsToken) ? "来自设置"
        : !string.IsNullOrWhiteSpace(_otaToken) ? "OTA 下发"
        : "默认占位（未配置）";

    /// <summary>
    /// 执行OTA检查并获取配置信息
    /// </summary>
    public async Task<OtaResponse?> CheckOtaUpdateAsync()
    {
        try
        {
            _logger?.LogInformation("开始OTA检查...");
            LastOtaCheckTime = DateTime.Now;

            await ReloadSettingsAsync().ConfigureAwait(false);

            var deviceInfo = await GetDeviceInfoAsync();
            var networkInfo = await GetNetworkInfoAsync();
            var otaRequest = CreateOtaRequest(deviceInfo, networkInfo);

            var json = JsonSerializer.Serialize(otaRequest, new JsonSerializerOptions 
            { 
                WriteIndented = false,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            });

            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            // 设置请求头
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Device-Id", DeviceId);
            _httpClient.DefaultRequestHeaders.Add("Client-Id", ClientId);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", UserAgent);
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "zh-CN");

            _logger?.LogDebug("OTA请求数据: {JsonData}", json);

            var response = await _httpClient.PostAsync(OtaVersionUrl, content);
            var responseText = await response.Content.ReadAsStringAsync();

            _logger?.LogDebug("OTA响应状态码: {StatusCode}", response.StatusCode);
            _logger?.LogDebug("OTA响应内容: {ResponseText}", responseText);

            if (response.IsSuccessStatusCode)
            {
                LatestOtaResponse = JsonSerializer.Deserialize<OtaResponse>(responseText);
                
                if (LatestOtaResponse != null)
                {
                    await ProcessOtaResponseAsync(LatestOtaResponse, responseText);
                    _logger?.LogInformation("OTA检查成功");
                }
            }
            else
            {
                _logger?.LogError("OTA检查失败: HTTP {StatusCode}, 响应: {Response}", response.StatusCode, responseText);
                
                // 尝试解析错误响应
                try
                {
                    var errorResponse = JsonSerializer.Deserialize<OtaErrorResponse>(responseText);
                    _logger?.LogError("OTA错误信息: {Error}", errorResponse?.Error ?? "未知错误");
                }
                catch
                {
                    // 忽略解析错误
                }
            }

            OtaCheckCompleted?.Invoke(this, LatestOtaResponse);
            return LatestOtaResponse;
        }
        catch (HttpRequestException httpEx)
        {
            _logger?.LogError(httpEx, "OTA网络请求异常");
            return null;
        }
        catch (TaskCanceledException tcEx)
        {
            _logger?.LogError(tcEx, "OTA请求超时");
            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "OTA检查异常");
            return null;
        }
    }

    /// <summary>
    /// 从OTA服务器获取MQTT配置信息（保持兼容性）
    /// </summary>
    public async Task<bool> InitializeMqttInfoAsync()
    {
        var otaResponse = await CheckOtaUpdateAsync();
        return otaResponse?.Mqtt != null && UpdateMqttConfiguration(otaResponse.Mqtt);
    }

    /// <summary>
    /// 获取设备信息
    /// </summary>
    public async Task<DeviceInfo> GetDeviceInfoAsync()
    {
        var networkInfo = await GetNetworkInfoAsync();
        
        return new DeviceInfo
        {
            DeviceId = DeviceId,
            ClientId = ClientId,
            UserAgent = UserAgent,
            Version = CurrentVersion,
            OsVersion = Environment.OSVersion.ToString(),
            Platform = Environment.OSVersion.Platform.ToString(),
            NetworkInfo = networkInfo
        };
    }

    /// <summary>
    /// 获取网络信息
    /// </summary>
    public Task<NetworkInfo?> GetNetworkInfoAsync()
    {
        try
        {
            var networkInfo = new NetworkInfo
            {
                IpAddress = GetLocalIpAddress(),
                MacAddress = DeviceId,
                // WiFi信息在某些平台上可能无法获取，这里设置为null
                Ssid = null,
                SignalStrength = null,
                Channel = null
            };

            return Task.FromResult<NetworkInfo?>(networkInfo);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "获取网络信息失败");
            return Task.FromResult<NetworkInfo?>(null);
        }
    }

    /// <summary>
    /// 从服务器响应中提取验证码
    /// </summary>
    public Task<string?> ExtractVerificationCodeAsync(string responseText)
    {
        try
        {
            var jsonDocument = JsonDocument.Parse(responseText);

            if (jsonDocument.RootElement.TryGetProperty("activation", out var activationProperty) &&
                activationProperty.TryGetProperty("code", out var codeProperty))
            {
                var activationCode = codeProperty.GetString();
                if (!string.IsNullOrEmpty(activationCode))
                {
                    _logger?.LogInformation("提取到验证码: {Code}", activationCode);
                    return Task.FromResult<string?>(activationCode);
                }
            }
            return Task.FromResult<string?>(null);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "提取验证码时发生错误");
            return Task.FromResult<string?>(null);
        }
    }

    /// <summary>
    /// 复制文本到剪贴板
    /// </summary>
    public async Task CopyToClipboardAsync(string text)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                await CopyToClipboardWindowsAsync(text);
            }
            else if (OperatingSystem.IsLinux())
            {
                await CopyToClipboardLinuxAsync(text);
            }
            else if (OperatingSystem.IsMacOS())
            {
                await CopyToClipboardMacOSAsync(text);
            }
            else
            {
                _logger?.LogWarning("不支持的操作系统，无法复制到剪贴板");
            }

            _logger?.LogInformation("已复制到剪贴板: {Text}", text);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "复制到剪贴板失败");
        }
    }

    /// <summary>
    /// 打开浏览器访问指定URL
    /// </summary>
    public async Task OpenBrowserAsync(string url)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
            else if (OperatingSystem.IsLinux())
            {
                Process.Start("xdg-open", url);
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", url);
            }
            else
            {
                _logger?.LogWarning("不支持的操作系统，无法打开浏览器");
                return;
            }

            _logger?.LogInformation("已打开浏览器: {Url}", url);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "打开浏览器失败");
        }
    }

    #region 私有方法

    /// <summary>
    /// 创建OTA请求对象
    /// </summary>
    private OtaRequest CreateOtaRequest(DeviceInfo deviceInfo, NetworkInfo? networkInfo)
    {
        var request = new OtaRequest
        {
            Version = 2,
            FlashSize = 16777216, // 16MB
            PsramSize = 0,
            MinimumFreeHeapSize = 8318916,
            MacAddress = deviceInfo.DeviceId,
            Uuid = deviceInfo.ClientId,
            ChipModelName = "esp32s3",
            Language = "zh-CN",
            Application = new ApplicationInfo
            {
                Name = "verdure-assistant",
                Version = deviceInfo.Version,
                ElfSha256 = GenerateDefaultSha256(),
                CompileTime = DateTime.UtcNow.ToString("MMM dd yyyy HH:mm:ss") + "Z",
                IdfVersion = "net8.0"
            },
            Board = new BoardInfo
            {
                Type = "verdure-assistant-client",
                Name = "verdure-assistant",
                Ip = networkInfo?.IpAddress,
                Mac = deviceInfo.DeviceId,
                Ssid = networkInfo?.Ssid,
                Rssi = networkInfo?.SignalStrength,
                Channel = networkInfo?.Channel
            },
            ChipInfo = new ChipInfo
            {
                Model = 9, // ESP32-S3
                Cores = 2,
                Revision = 2,
                Features = 18
            },
            PartitionTable = new List<PartitionInfo>(),
            Ota = new OtaInfo
            {
                Label = "factory"
            }
        };

        return request;
    }

    /// <summary>
    /// 处理OTA响应
    /// </summary>
    private async Task ProcessOtaResponseAsync(OtaResponse otaResponse, string responseText)
    {
        // 处理激活信息
        if (otaResponse.Activation != null)
        {
            ActivationCode = otaResponse.Activation.Code;
            ActivationMessage = otaResponse.Activation.Message;

            if (!string.IsNullOrEmpty(ActivationCode))
            {
                _logger?.LogInformation("收到验证码: {Code}", ActivationCode);
                
                // 复制到剪贴板
                await CopyToClipboardAsync(ActivationCode);
                
                // 触发验证码接收事件
                VerificationCodeReceived?.Invoke(this, ActivationCode);
                
                _logger?.LogInformation("请先登录xiaozhi.me,绑定Code: {Code}", ActivationCode);
            }
        }

        // 更新MQTT配置
        if (otaResponse.Mqtt != null)
        {
            UpdateMqttConfiguration(otaResponse.Mqtt);
        }

        // 更新WebSocket URL
        if (otaResponse.WebSocket != null && !string.IsNullOrEmpty(otaResponse.WebSocket.Url))
        {
            WebSocketUrl = otaResponse.WebSocket.Url;
            _logger?.LogInformation("更新WebSocket URL: {Url}", WebSocketUrl);
        }

        if (otaResponse.WebSocket != null && !string.IsNullOrEmpty(otaResponse.WebSocket.Token))
        {
            _otaToken = otaResponse.WebSocket.Token;
            _logger?.LogInformation("已获取OTA下发的WebSocket令牌");
        }

        // 检查固件更新
        if (otaResponse.Firmware != null && !string.IsNullOrEmpty(otaResponse.Firmware.Url))
        {
            _logger?.LogInformation("发现固件更新: 版本 {Version}, 下载地址: {Url}", 
                otaResponse.Firmware.Version, otaResponse.Firmware.Url);
        }

        // 显示服务器时间
        if (otaResponse.ServerTime != null)
        {
            var serverTime = DateTimeOffset.FromUnixTimeMilliseconds(otaResponse.ServerTime.Timestamp);
            _logger?.LogInformation("服务器时间: {ServerTime}, 时区: {Timezone}", 
                serverTime, otaResponse.ServerTime.Timezone);
        }
    }

    /// <summary>
    /// 更新MQTT配置
    /// </summary>
    private bool UpdateMqttConfiguration(MqttInfo mqttInfo)
    {
        try
        {
            MqttInfo = new MqttConfiguration
            {
                Endpoint = mqttInfo.Endpoint,
                ClientId = mqttInfo.ClientId,
                Username = mqttInfo.Username,
                Password = mqttInfo.Password,
                PublishTopic = mqttInfo.PublishTopic,
                SubscribeTopic = mqttInfo.SubscribeTopic
            };

            _logger?.LogInformation("MQTT配置更新成功");
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "更新MQTT配置失败");
            return false;
        }
    }

    private string GenerateClientId()
    {
        return Guid.NewGuid().ToString();
    }

    private string GenerateDefaultSha256()
    {
        // 生成一个默认的SHA256哈希值
        var random = new Random();
        var bytes = new byte[32];
        random.NextBytes(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private string GetMacAddress()
    {
        try
        {
            // 跳过没有物理地址的虚拟网卡（VPN/Hyper-V/WSL 等），优先选择以太网与 Wi-Fi
            var candidates = NetworkInterface.GetAllNetworkInterfaces()
                .Where(nic => nic.OperationalStatus == OperationalStatus.Up &&
                              nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                              nic.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                .Select(nic => (nic, bytes: nic.GetPhysicalAddress().GetAddressBytes()))
                .Where(x => x.bytes.Length == 6 && x.bytes.Any(b => b != 0))
                .OrderBy(x => x.nic.NetworkInterfaceType is NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211 ? 0 : 1)
                .ToList();

            if (candidates.Count > 0)
            {
                return string.Join(":", candidates[0].bytes.Select(b => b.ToString("x2")));
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "无法获取MAC地址，使用基于机器名的稳定标识");
        }

        // 取不到真实 MAC 时，用机器名+用户名生成稳定的本地管理地址，保证每次启动 Device ID 不变（否则每次都会被当作新设备要求重新绑定）
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{Environment.MachineName}|{Environment.UserName}"));
        var macBytes = hash[..6];
        macBytes[0] = (byte)(macBytes[0] & 0xFE | 0x02); // 本地管理的单播地址
        return string.Join(":", macBytes.Select(b => b.ToString("x2")));
    }

    private string GetLocalIpAddress()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            var ipAddress = host.AddressList.FirstOrDefault(ip => 
                ip.AddressFamily == AddressFamily.InterNetwork && 
                !IPAddress.IsLoopback(ip));
            return ipAddress?.ToString() ?? "127.0.0.1";
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "无法获取本地IP地址，使用默认值");
            return "127.0.0.1";
        }
    }

    #endregion

    #region 剪贴板操作方法

    private async Task CopyToClipboardWindowsAsync(string text)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-Command \"Set-Clipboard -Value '{text.Replace("'", "''")}'\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        await process.WaitForExitAsync();
    }

    private async Task CopyToClipboardLinuxAsync(string text)
    {
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "xclip",
                    Arguments = "-selection clipboard",
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.StandardInput.WriteAsync(text);
            process.StandardInput.Close();
            await process.WaitForExitAsync();
        }
        catch
        {
            // 尝试使用 xsel
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "xsel",
                    Arguments = "--clipboard --input",
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.StandardInput.WriteAsync(text);
            process.StandardInput.Close();
            await process.WaitForExitAsync();
        }
    }

    private async Task CopyToClipboardMacOSAsync(string text)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "pbcopy",
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        await process.StandardInput.WriteAsync(text);
        process.StandardInput.Close();
        await process.WaitForExitAsync();
    }

    #endregion

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
