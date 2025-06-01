using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Text.Json;
using System.Net.NetworkInformation;

namespace Verdure.Assistant.Core.Services
{
    /// <summary>
    /// 配置管理服务，对应Python中的ConfigManager
    /// </summary>
    public interface IConfigurationService
    {
        Task<bool> InitializeMqttInfoAsync();
        string ClientId { get; }
        string DeviceId { get; }
        MqttConfiguration? MqttInfo { get; }
        string OtaVersionUrl { get; }
        string WebSocketUrl { get; }
        event EventHandler<string>? VerificationCodeReceived;
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
        private readonly IVerificationService _verificationService;

        public string ClientId { get; private set; }
        public string DeviceId { get; private set; }
        public MqttConfiguration? MqttInfo { get; private set; }
        public string OtaVersionUrl { get; private set; } = "https://api.tenclass.net/xiaozhi/ota/";
        public string WebSocketUrl { get; private set; } = "wss://api.tenclass.net/xiaozhi/v1/";

        public event EventHandler<string>? VerificationCodeReceived;

        public ConfigurationService(IVerificationService verificationService, ILogger<ConfigurationService>? logger = null)
        {
            _verificationService = verificationService;
            _logger = logger;
            _httpClient = new HttpClient();
            
            // 初始化客户端ID和设备ID
            ClientId = GenerateClientId();
            DeviceId = GetMacAddress();
        }

        /// <summary>
        /// 从OTA服务器获取MQTT配置信息
        /// </summary>
        public async Task<bool> InitializeMqttInfoAsync()
        {
            try
            {
                _logger?.LogInformation("正在从OTA服务器获取MQTT配置...");

                var payload = new
                {
                    version = 2,
                    flash_size = 16777216,
                    psram_size = 0,
                    minimum_free_heap_size = 8318916,
                    mac_address = DeviceId,
                    uuid = ClientId,
                    chip_model_name = "esp32s3",
                    chip_info = new
                    {
                        model = 9,
                        cores = 2,
                        revision = 2,
                        features = 18
                    },
                    application = new
                    {
                        name = "xiaozhi",
                        version = "1.1.2",
                        idf_version = "v5.3.2-dirty"
                    },
                    partition_table = new object[0],
                    ota = new
                    {
                        label = "factory"
                    },
                    board = new
                    {
                        type = "bread-compact-wifi",
                        ip = GetLocalIpAddress(),
                        mac = DeviceId
                    }
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
                
                // 设置请求头
                content.Headers.Clear();
                content.Headers.Add("Content-Type", "application/json");
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("Device-Id", DeviceId);

                var response = await _httpClient.PostAsync(OtaVersionUrl, content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseText = await response.Content.ReadAsStringAsync();
                    var responseData = JsonSerializer.Deserialize<JsonElement>(responseText);
                    
                    // Handle verification code if present
                    await HandleVerificationCodeAsync(responseText);
                    
                    if (responseData.TryGetProperty("mqtt", out var mqttElement))
                    {
                        MqttInfo = new MqttConfiguration
                        {
                            Endpoint = mqttElement.GetProperty("endpoint").GetString() ?? "",
                            ClientId = mqttElement.GetProperty("client_id").GetString() ?? "",
                            Username = mqttElement.GetProperty("username").GetString() ?? "",
                            Password = mqttElement.GetProperty("password").GetString() ?? "",
                            PublishTopic = mqttElement.GetProperty("publish_topic").GetString() ?? "",
                            SubscribeTopic = mqttElement.GetProperty("subscribe_topic").GetString() ?? ""
                        };

                        _logger?.LogInformation("MQTT配置获取成功");
                        return true;
                    }
                    else
                    {
                        _logger?.LogError("OTA服务器返回的数据无效: MQTT信息缺失");
                        return false;
                    }
                }
                else
                {
                    _logger?.LogError($"OTA服务器错误: HTTP {response.StatusCode}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "从OTA服务器获取MQTT配置失败");
                return false;
            }
        }

        /// <summary>
        /// 处理验证码响应（对应Python中的_handle_verification_code）
        /// </summary>
        private async Task HandleVerificationCodeAsync(string responseText)
        {
            try
            {
                var verificationCode = await _verificationService.ExtractVerificationCodeAsync(responseText);
                
                if (!string.IsNullOrEmpty(verificationCode))
                {
                    _logger?.LogInformation("检测到验证码: {Code}", verificationCode);
                    
                    // 复制到剪贴板
                    await _verificationService.CopyToClipboardAsync(verificationCode);
                    
                    // 构建登录URL并打开浏览器
                    var loginUrl = $"https://api.tenclass.net/xiaozhi/login?code={verificationCode}";
                    await _verificationService.OpenBrowserAsync(loginUrl);
                    
                    // 触发验证码接收事件
                    VerificationCodeReceived?.Invoke(this, verificationCode);
                    
                    _logger?.LogInformation("验证码已复制到剪贴板并打开浏览器登录页面");
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "处理验证码时发生错误");
            }
        }

        private string GenerateClientId()
        {
            return Guid.NewGuid().ToString();
        }

        private string GetMacAddress()
        {
            try
            {
                var networkInterface = NetworkInterface.GetAllNetworkInterfaces()
                    .FirstOrDefault(nic => nic.OperationalStatus == OperationalStatus.Up &&
                                          nic.NetworkInterfaceType != NetworkInterfaceType.Loopback);

                if (networkInterface != null)
                {
                    var macBytes = networkInterface.GetPhysicalAddress().GetAddressBytes();
                    return string.Join(":", macBytes.Select(b => b.ToString("x2")));
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "无法获取MAC地址，使用默认值");
            }

            // 如果无法获取MAC地址，返回一个默认值
            return "00:00:00:00:00:00";
        }

        private string GetLocalIpAddress()
        {
            try
            {
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                var ipAddress = host.AddressList.FirstOrDefault(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                return ipAddress?.ToString() ?? "127.0.0.1";
            }
            catch
            {
                return "127.0.0.1";
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
