using System.Text.Json.Serialization;

namespace Verdure.Assistant.Core.Models
{
    /// <summary>
    /// OTA请求模型
    /// </summary>
    public class OtaRequest
    {
        [JsonPropertyName("application")]
        public ApplicationInfo Application { get; set; } = new ApplicationInfo();

        [JsonPropertyName("mac_address")]
        public string? MacAddress { get; set; }

        [JsonPropertyName("uuid")]
        public string? Uuid { get; set; }

        [JsonPropertyName("chip_model_name")]
        public string? ChipModelName { get; set; }

        [JsonPropertyName("flash_size")]
        public long? FlashSize { get; set; }

        [JsonPropertyName("psram_size")]
        public long? PsramSize { get; set; }

        [JsonPropertyName("partition_table")]
        public List<PartitionInfo>? PartitionTable { get; set; }

        [JsonPropertyName("board")]
        public BoardInfo Board { get; set; } = new BoardInfo();

        [JsonPropertyName("version")]
        public int? Version { get; set; }

        [JsonPropertyName("language")]
        public string? Language { get; set; }

        [JsonPropertyName("minimum_free_heap_size")]
        public long? MinimumFreeHeapSize { get; set; }

        [JsonPropertyName("ota")]
        public OtaInfo? Ota { get; set; }

        [JsonPropertyName("chip_info")]
        public ChipInfo? ChipInfo { get; set; }
    }

    /// <summary>
    /// 应用程序信息
    /// </summary>
    public class ApplicationInfo
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; } = "verdure-assistant";

        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.2.0";

        [JsonPropertyName("elf_sha256")]
        public string ElfSha256 { get; set; } = "";

        [JsonPropertyName("compile_time")]
        public string? CompileTime { get; set; }

        [JsonPropertyName("idf_version")]
        public string? IdfVersion { get; set; } = "net8.0";
    }

    /// <summary>
    /// 分区信息
    /// </summary>
    public class PartitionInfo
    {
        [JsonPropertyName("label")]
        public string Label { get; set; } = "";

        [JsonPropertyName("type")]
        public int Type { get; set; }

        [JsonPropertyName("subtype")]
        public int Subtype { get; set; }

        [JsonPropertyName("address")]
        public long Address { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    /// <summary>
    /// 开发板信息
    /// </summary>
    public class BoardInfo
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "verdure-assistant-client";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "verdure-assistant";

        [JsonPropertyName("ssid")]
        public string? Ssid { get; set; }

        [JsonPropertyName("rssi")]
        public int? Rssi { get; set; }

        [JsonPropertyName("channel")]
        public int? Channel { get; set; }

        [JsonPropertyName("ip")]
        public string? Ip { get; set; }

        [JsonPropertyName("mac")]
        public string? Mac { get; set; }
    }

    /// <summary>
    /// 芯片信息
    /// </summary>
    public class ChipInfo
    {
        [JsonPropertyName("model")]
        public int Model { get; set; } = 9; // ESP32-S3

        [JsonPropertyName("cores")]
        public int Cores { get; set; } = 2;

        [JsonPropertyName("revision")]
        public int Revision { get; set; } = 2;

        [JsonPropertyName("features")]
        public int Features { get; set; } = 18;
    }

    /// <summary>
    /// OTA信息
    /// </summary>
    public class OtaInfo
    {
        [JsonPropertyName("label")]
        public string Label { get; set; } = "factory";
    }

    /// <summary>
    /// OTA响应模型
    /// </summary>
    public class OtaResponse
    {
        [JsonPropertyName("activation")]
        public ActivationInfo? Activation { get; set; }

        [JsonPropertyName("mqtt")]
        public MqttInfo? Mqtt { get; set; }

        [JsonPropertyName("websocket")]
        public WebSocketInfo? WebSocket { get; set; }

        [JsonPropertyName("server_time")]
        public ServerTimeInfo? ServerTime { get; set; }

        [JsonPropertyName("firmware")]
        public FirmwareInfo? Firmware { get; set; }
    }

    /// <summary>
    /// 激活信息
    /// </summary>
    public class ActivationInfo
    {
        [JsonPropertyName("code")]
        public string Code { get; set; } = "";

        [JsonPropertyName("message")]
        public string Message { get; set; } = "";
    }

    /// <summary>
    /// MQTT配置信息
    /// </summary>
    public class MqttInfo
    {
        [JsonPropertyName("endpoint")]
        public string Endpoint { get; set; } = "";

        [JsonPropertyName("client_id")]
        public string ClientId { get; set; } = "";

        [JsonPropertyName("username")]
        public string Username { get; set; } = "";

        [JsonPropertyName("password")]
        public string Password { get; set; } = "";

        [JsonPropertyName("publish_topic")]
        public string PublishTopic { get; set; } = "";

        [JsonPropertyName("subscribe_topic")]
        public string SubscribeTopic { get; set; } = "";
    }

    /// <summary>
    /// WebSocket配置信息
    /// </summary>
    public class WebSocketInfo
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = "";

        [JsonPropertyName("token")]
        public string Token { get; set; } = "";
    }

    /// <summary>
    /// 服务器时间信息
    /// </summary>
    public class ServerTimeInfo
    {
        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }

        [JsonPropertyName("timezone")]
        public string Timezone { get; set; } = "";

        [JsonPropertyName("timezone_offset")]
        public int TimezoneOffset { get; set; }
    }

    /// <summary>
    /// 固件信息
    /// </summary>
    public class FirmwareInfo
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [JsonPropertyName("url")]
        public string Url { get; set; } = "";
    }

    /// <summary>
    /// OTA错误响应
    /// </summary>
    public class OtaErrorResponse
    {
        [JsonPropertyName("error")]
        public string Error { get; set; } = "";
    }

    /// <summary>
    /// 网络信息
    /// </summary>
    public class NetworkInfo
    {
        public string? Ssid { get; set; }
        public int? SignalStrength { get; set; }
        public int? Channel { get; set; }
        public string? IpAddress { get; set; }
        public string? MacAddress { get; set; }
    }

    /// <summary>
    /// 设备信息
    /// </summary>
    public class DeviceInfo
    {
        public string DeviceId { get; set; } = "";
        public string ClientId { get; set; } = "";
        public string UserAgent { get; set; } = "";
        public string Version { get; set; } = "";
        public string OsVersion { get; set; } = "";
        public string Platform { get; set; } = "";
        public NetworkInfo? NetworkInfo { get; set; }
    }
}
