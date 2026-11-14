using System.Text.Json.Serialization;

namespace Verdure.Assistant.Core.Services.DeepSeek;

/// <summary>
/// 要发送给模型的图片。DeepSeek 支持 JPEG / PNG / GIF / WebP，格式以文件内容为准。
/// </summary>
public sealed record DeepSeekImage(byte[] Data, string MimeType, string? Name = null)
{
    /// <summary>单张图片（内联 base64）上限 32 MiB，请求体总上限 48 MiB</summary>
    public const int MaxInlineBytes = 32 * 1024 * 1024;

    /// <summary>从字节内容识别图片格式；无法识别时返回 null</summary>
    public static string? DetectMimeType(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
        {
            return "image/png";
        }

        if (data.Length >= 6 && data[0] == 'G' && data[1] == 'I' && data[2] == 'F' && data[3] == '8')
        {
            return "image/gif";
        }

        if (data.Length >= 12 && data[0] == 'R' && data[1] == 'I' && data[2] == 'F' && data[3] == 'F'
            && data[8] == 'W' && data[9] == 'E' && data[10] == 'B' && data[11] == 'P')
        {
            return "image/webp";
        }

        return null;
    }

    /// <summary>用内容识别格式创建图片；格式不受支持时抛出 <see cref="ArgumentException"/></summary>
    public static DeepSeekImage FromBytes(byte[] data, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var mime = DetectMimeType(data)
            ?? throw new ArgumentException("不支持的图片格式，仅支持 JPEG、PNG、GIF、WebP", nameof(data));
        if (data.Length > MaxInlineBytes)
        {
            throw new ArgumentException("图片超过 32 MiB 上限", nameof(data));
        }

        return new DeepSeekImage(data, mime, name);
    }

    public string ToDataUrl() => $"data:{MimeType};base64,{Convert.ToBase64String(Data)}";
}

/// <summary>多模态消息的内容块：文本或图片</summary>
public sealed class DeepSeekContentPart
{
    public const string TextType = "text";
    public const string ImageUrlType = "image_url";

    [JsonPropertyName("type")]
    public string Type { get; set; } = TextType;

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    [JsonPropertyName("image_url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DeepSeekImageUrl? ImageUrl { get; set; }

    public static DeepSeekContentPart FromText(string text) => new() { Type = TextType, Text = text };

    /// <param name="detail">low 会把图片缩到 512×512（更快更省 token）；省略即默认原图</param>
    public static DeepSeekContentPart FromImage(DeepSeekImage image, string? detail = null) =>
        new() { Type = ImageUrlType, ImageUrl = new DeepSeekImageUrl { Url = image.ToDataUrl(), Detail = detail } };
}

public sealed class DeepSeekImageUrl
{
    [JsonPropertyName("url")]
    public string Url { get; set; } = string.Empty;

    [JsonPropertyName("detail")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Detail { get; set; }
}
