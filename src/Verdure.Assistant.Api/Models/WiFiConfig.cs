namespace Verdure.Assistant.Api.Models;

/// <summary>
/// WiFi接入点配置
/// </summary>
public class ApConfig
{
    /// <summary>
    /// 热点名称(SSID)
    /// </summary>
    public string Ssid { get; set; } = "VerdureAssistant-WiFiSetup";

    /// <summary>
    /// 热点密码
    /// </summary>
    public string Password { get; set; } = "verdure123";

    /// <summary>
    /// 热点IP地址
    /// </summary>
    public string Ip { get; set; } = "192.168.4.1";

    /// <summary>
    /// DHCP起始地址
    /// </summary>
    public string DhcpStart { get; set; } = "192.168.4.100";

    /// <summary>
    /// DHCP结束地址
    /// </summary>
    public string DhcpEnd { get; set; } = "192.168.4.200";

    /// <summary>
    /// 网络接口名称
    /// </summary>
    public string Interface { get; set; } = "wlan0";
}

/// <summary>
/// 设备配置
/// </summary>
public class DeviceConfig
{
    /// <summary>
    /// AP配置
    /// </summary>
    public ApConfig ApConfig { get; set; } = new ApConfig();

    /// <summary>
    /// 国家代码
    /// </summary>
    public string Country { get; set; } = "CN";
}

/// <summary>
/// WiFi配网配置
/// </summary>
public class WiFiSetupConfig
{
    /// <summary>
    /// 是否启用WiFi配网功能
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Web服务器端口
    /// </summary>
    public int WebServerPort { get; set; } = 5241;

    /// <summary>
    /// 启动延时（秒）
    /// </summary>
    public int StartupDelaySeconds { get; set; } = 10;

    /// <summary>
    /// 设备配置
    /// </summary>
    public DeviceConfig DeviceConfig { get; set; } = new DeviceConfig();
}