using System.Text.Json.Serialization;

namespace Verdure.Assistant.Core.Models;

/// <summary>
/// 聊天消息模型
/// </summary>
public class ChatMessage
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; } = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [JsonPropertyName("role")]
    public string Role { get; set; } = "user"; // user, assistant, system

    [JsonPropertyName("audio_data")]
    public byte[]? AudioData { get; set; }

    [JsonPropertyName("session_id")]
    public string SessionId { get; set; } = string.Empty;
}
