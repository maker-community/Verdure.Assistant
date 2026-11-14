using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Services.DeepSeek;
using Verdure.Assistant.Core.Services.MCP;
using Xunit;
using static DeepSeek.Tests.TestData;

namespace DeepSeek.Tests;

public class VisionTests
{
    private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01 };
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00 };

    #region 图片与消息模型

    [Fact]
    public void DetectMimeType_Recognizes_Supported_Formats_By_Content()
    {
        Assert.Equal("image/jpeg", DeepSeekImage.DetectMimeType(Jpeg));
        Assert.Equal("image/png", DeepSeekImage.DetectMimeType(Png));
        Assert.Equal("image/gif", DeepSeekImage.DetectMimeType("GIF89a"u8));
        Assert.Equal("image/webp", DeepSeekImage.DetectMimeType("RIFF\0\0\0\0WEBPVP8 "u8));
        Assert.Null(DeepSeekImage.DetectMimeType("not an image"u8));
        Assert.Null(DeepSeekImage.DetectMimeType(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void FromBytes_Rejects_Unknown_Format()
    {
        Assert.Throws<ArgumentException>(() => DeepSeekImage.FromBytes(new byte[] { 1, 2, 3, 4 }));
        Assert.Equal("image/png", DeepSeekImage.FromBytes(Png, "a.png").MimeType);
    }

    [Fact]
    public void DataUrl_Contains_Mime_And_Base64()
    {
        var url = DeepSeekImage.FromBytes(Jpeg).ToDataUrl();

        Assert.StartsWith("data:image/jpeg;base64,", url);
        Assert.Equal(Jpeg, Convert.FromBase64String(url["data:image/jpeg;base64,".Length..]));
    }

    [Fact]
    public void PlainMessage_Still_Serializes_Content_As_String()
    {
        var json = JsonSerializer.Serialize(DeepSeekMessage.User("你好"), DeepSeekJson.Options);

        var node = JsonNode.Parse(json)!;
        Assert.Equal("你好", (string?)node["content"]);
    }

    [Fact]
    public void ImageMessage_Serializes_Content_As_Block_Array_With_Text_First()
    {
        var message = DeepSeekMessage.User("这是什么？", new[] { DeepSeekImage.FromBytes(Jpeg) }, detail: "low");

        var node = JsonNode.Parse(JsonSerializer.Serialize(message, DeepSeekJson.Options))!;
        var blocks = node["content"]!.AsArray();

        Assert.Equal(2, blocks.Count);
        Assert.Equal("text", (string?)blocks[0]!["type"]);
        Assert.Equal("这是什么？", (string?)blocks[0]!["text"]);
        Assert.Equal("image_url", (string?)blocks[1]!["type"]);
        Assert.StartsWith("data:image/jpeg;base64,", (string?)blocks[1]!["image_url"]!["url"]);
        Assert.Equal("low", (string?)blocks[1]!["image_url"]!["detail"]);
        Assert.True(message.HasImages);
    }

    [Fact]
    public void ImageMessage_Without_Text_Has_Only_Image_Block()
    {
        var message = DeepSeekMessage.User("  ", new[] { DeepSeekImage.FromBytes(Png) });

        var blocks = JsonNode.Parse(JsonSerializer.Serialize(message, DeepSeekJson.Options))!["content"]!.AsArray();
        Assert.Single(blocks);
        Assert.Equal("image_url", (string?)blocks[0]!["type"]);
    }

    [Fact]
    public void Message_With_No_Images_Falls_Back_To_Plain_Text()
    {
        var message = DeepSeekMessage.User("只有文字", Array.Empty<DeepSeekImage>());

        Assert.False(message.HasImages);
        Assert.Equal("只有文字", (string?)JsonNode.Parse(JsonSerializer.Serialize(message, DeepSeekJson.Options))!["content"]);
    }

    [Fact]
    public void Deserialize_Accepts_String_And_Array_Content()
    {
        var text = JsonSerializer.Deserialize<DeepSeekMessage>("""{"role":"assistant","content":"你好"}""", DeepSeekJson.Options)!;
        Assert.Equal("你好", text.Content);
        Assert.Null(text.Parts);

        var blocks = JsonSerializer.Deserialize<DeepSeekMessage>(
            """{"role":"user","content":[{"type":"text","text":"看图"},{"type":"image_url","image_url":{"url":"data:image/png;base64,AAAA"}}]}""",
            DeepSeekJson.Options)!;
        Assert.Equal("看图", blocks.Content);
        Assert.Equal(2, blocks.Parts!.Count);
        Assert.True(blocks.HasImages);

        var empty = JsonSerializer.Deserialize<DeepSeekMessage>("""{"role":"assistant","content":null}""", DeepSeekJson.Options)!;
        Assert.Null(empty.Content);
    }

    #endregion

    #region 会话

    [Fact]
    public async Task Session_Sends_Image_Blocks_And_Keeps_Them_In_History()
    {
        var (client, handler, _) = CreateClient();
        var session = new DeepSeekChatSession(client);
        handler.EnqueueSse(TextSse("是一只猫"));

        var events = await ToListAsync(session.SendAsync("这是什么动物", new[] { DeepSeekImage.FromBytes(Jpeg) }));

        Assert.IsType<DeepSeekChatEvent.Completed>(events[^1]);
        var user = handler.Requests[0].Json["messages"]!.AsArray().Last(m => (string?)m!["role"] == "user")!;
        var blocks = user["content"]!.AsArray();
        Assert.Equal("这是什么动物", (string?)blocks[0]!["text"]);
        Assert.Equal("image_url", (string?)blocks[1]!["type"]);

        Assert.True(session.History[0].HasImages);
        Assert.Equal("这是什么动物", session.History[0].Content);
    }

    [Fact]
    public async Task Session_Uses_Default_Question_When_Only_Image_Is_Sent()
    {
        var (client, handler, _) = CreateClient();
        var session = new DeepSeekChatSession(client);
        handler.EnqueueSse(TextSse("一张照片"));

        await ToListAsync(session.SendAsync("", new[] { DeepSeekImage.FromBytes(Png) }));

        var user = handler.Requests[0].Json["messages"]!.AsArray().Last()!;
        Assert.Equal(DeepSeekChatSession.DefaultImageQuestion, (string?)user["content"]![0]!["text"]);
    }

    [Fact]
    public async Task Session_Follow_Up_Text_Turn_Still_Carries_Earlier_Image()
    {
        var (client, handler, _) = CreateClient();
        var session = new DeepSeekChatSession(client);
        handler.EnqueueSse(TextSse("是猫")).EnqueueSse(TextSse("橘色"));

        await ToListAsync(session.SendAsync("这是什么", new[] { DeepSeekImage.FromBytes(Jpeg) }));
        await ToListAsync(session.SendAsync("它是什么颜色"));

        var messages = handler.Requests[1].Json["messages"]!.AsArray();
        Assert.Contains(messages, m => m!["content"] is JsonArray a && a.Any(b => (string?)b!["type"] == "image_url"));
    }

    [Fact]
    public async Task Session_Rejects_Empty_Text_Without_Images()
    {
        var (client, _, _) = CreateClient();
        var session = new DeepSeekChatSession(client);

        await Assert.ThrowsAnyAsync<ArgumentException>(async () =>
            await ToListAsync(session.SendAsync("", Array.Empty<DeepSeekImage>())));
    }

    #endregion

    #region 视觉服务

    [Fact]
    public async Task VisionService_Sends_Flash_Without_Thinking_And_Returns_Text()
    {
        var (client, handler, _) = CreateClient();
        handler.EnqueueJson(Completion("桌上有一个红色的杯子。"));
        var vision = new DeepSeekVisionService(client, client.Options);

        var text = await vision.ExplainAsync(Jpeg, "image/jpeg", "桌上有什么");

        Assert.Equal("桌上有一个红色的杯子。", text);
        var body = handler.Requests[0].Json;
        Assert.Equal("deepseek-flash", (string?)body["model"]);
        Assert.Equal("disabled", (string?)body["thinking"]!["type"]);
        var user = body["messages"]!.AsArray().Last()!;
        Assert.Equal("桌上有什么", (string?)user["content"]![0]!["text"]);
        Assert.StartsWith("data:image/jpeg;base64,", (string?)user["content"]![1]!["image_url"]!["url"]);
        Assert.Contains("视觉助手", (string?)body["messages"]![0]!["content"]);
    }

    [Fact]
    public async Task VisionService_Uses_Default_Question_When_Blank()
    {
        var (client, handler, _) = CreateClient();
        handler.EnqueueJson(Completion("一张图"));

        await new DeepSeekVisionService(client, client.Options).ExplainAsync(Png, null, "  ");

        var user = handler.Requests[0].Json["messages"]!.AsArray().Last()!;
        Assert.Equal(DeepSeekVisionService.DefaultQuestion, (string?)user["content"]![0]!["text"]);
    }

    [Fact]
    public async Task VisionService_Propagates_Auth_Error()
    {
        var (client, handler, _) = CreateClient(o => o.MaxRetries = 0);
        handler.EnqueueError(HttpStatusCode.Unauthorized, "authentication_error", "bad key");

        var ex = await Assert.ThrowsAsync<DeepSeekApiException>(() =>
            new DeepSeekVisionService(client, client.Options).ExplainAsync(Jpeg, null, "q"));

        Assert.True(ex.IsAuthenticationError);
    }

    [Fact]
    public async Task VisionService_Rejects_Empty_Image()
    {
        var (client, _, _) = CreateClient();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new DeepSeekVisionService(client, client.Options).ExplainAsync(Array.Empty<byte>(), null, "q"));
    }

    [Fact]
    public void VisionService_Is_Available_Only_With_ApiKey()
    {
        var (client, _, _) = CreateClient();
        var vision = new DeepSeekVisionService(client, client.Options);

        Assert.True(vision.IsAvailable);
        client.Options.ApiKey = "";
        Assert.False(vision.IsAvailable);
    }

    #endregion

    #region 相机/屏幕工具

    private sealed class FakeCapture : IImageCaptureService
    {
        public Exception? Failure { get; set; }
        public List<ImageCaptureSource> Calls { get; } = new();
        public bool IsSupported(ImageCaptureSource source) => true;

        public Task<CapturedImage> CaptureAsync(ImageCaptureSource source, CancellationToken cancellationToken = default)
        {
            Calls.Add(source);
            return Failure != null ? Task.FromException<CapturedImage>(Failure) : Task.FromResult(new CapturedImage(Jpeg, "image/jpeg", 640, 480));
        }
    }

    private sealed class FakeVision : IVisionService
    {
        public bool IsAvailable { get; set; } = true;
        public Exception? Failure { get; set; }
        public List<(byte[] Image, string Question)> Calls { get; } = new();

        public Task<string> ExplainAsync(byte[] image, string? mimeType, string question, CancellationToken cancellationToken = default)
        {
            Calls.Add((image, question));
            return Failure != null ? Task.FromException<string>(Failure) : Task.FromResult("看到一只猫");
        }
    }

    private static async Task<(McpDeepSeekToolExecutor Tools, McpCameraDevice Device)> CreateCameraAsync(
        IImageCaptureService? capture, IVisionService? vision)
    {
        var server = new McpServer();
        var device = new McpCameraDevice(server, null, capture, vision);
        await device.InitializeAsync();
        return (new McpDeepSeekToolExecutor(server), device);
    }

    [Fact]
    public async Task TakePhoto_Captures_From_Camera_And_Asks_Vision()
    {
        var capture = new FakeCapture();
        var vision = new FakeVision();
        var (tools, _) = await CreateCameraAsync(capture, vision);

        var result = await tools.ExecuteAsync("self_camera_take_photo", """{"question":"这是什么"}""", CancellationToken.None);

        Assert.Equal(new[] { ImageCaptureSource.Camera }, capture.Calls);
        Assert.Equal("这是什么", Assert.Single(vision.Calls).Question);
        Assert.Equal(Jpeg, vision.Calls[0].Image);
        Assert.Contains("看到一只猫", result);
    }

    [Fact]
    public async Task Screenshot_Tool_Captures_Screen_And_Asks_Vision()
    {
        var capture = new FakeCapture();
        var vision = new FakeVision();
        var (tools, _) = await CreateCameraAsync(capture, vision);

        var result = await tools.ExecuteAsync("self_screen_take_screenshot", """{"question":"屏幕上写了什么"}""", CancellationToken.None);

        Assert.Equal(new[] { ImageCaptureSource.Screen }, capture.Calls);
        Assert.Contains("屏幕截图分析结果", result);
        Assert.Contains("看到一只猫", result);
    }

    [Fact]
    public async Task TakePhoto_Reports_Capture_Failure_Clearly_And_Skips_Vision()
    {
        var capture = new FakeCapture { Failure = new ImageCaptureException("没有找到摄像头") };
        var vision = new FakeVision();
        var (tools, _) = await CreateCameraAsync(capture, vision);

        var result = await tools.ExecuteAsync("self_camera_take_photo", """{"question":"这是什么"}""", CancellationToken.None);

        Assert.Contains("没有找到摄像头", result);
        Assert.Empty(vision.Calls);
    }

    [Fact]
    public async Task TakePhoto_Without_Capture_Service_Says_Camera_Unavailable_Instead_Of_Faking()
    {
        var (tools, _) = await CreateCameraAsync(capture: null, vision: new FakeVision());

        var result = await tools.ExecuteAsync("self_camera_take_photo", """{"question":"这是什么"}""", CancellationToken.None);

        Assert.Contains("没有可用的摄像头", result);
    }

    [Fact]
    public async Task TakePhoto_Turns_Vision_Auth_Error_Into_Readable_Message()
    {
        var vision = new FakeVision { Failure = new DeepSeekApiException("bad", HttpStatusCode.Unauthorized) };
        var (tools, _) = await CreateCameraAsync(new FakeCapture(), vision);

        var result = await tools.ExecuteAsync("self_camera_take_photo", """{"question":"q"}""", CancellationToken.None);

        Assert.Contains("API Key 无效", result);
    }

    [Fact]
    public async Task TakePhoto_Without_Any_Vision_Backend_Explains_How_To_Configure()
    {
        // 回退到小智识图地址会发起真实网络请求，这里把地址清空来验证"未配置"的提示
        var server = new McpServer();
        var device = new McpCameraDevice(server, null, new FakeCapture(), new FakeVision { IsAvailable = false });
        device.SetExplainUrl("", "");
        await device.InitializeAsync();

        var result = await new McpDeepSeekToolExecutor(server).ExecuteAsync("self_camera_take_photo", """{"question":"q"}""", CancellationToken.None);

        Assert.Contains("图像识别未配置", result);
    }

    [Fact]
    public async Task Capture_Tool_Only_Takes_Photo_Without_Calling_Vision()
    {
        var capture = new FakeCapture();
        var vision = new FakeVision();
        var (tools, _) = await CreateCameraAsync(capture, vision);

        var result = await tools.ExecuteAsync("self_camera_capture", "{}", CancellationToken.None);

        Assert.Single(capture.Calls);
        Assert.Empty(vision.Calls);
        Assert.Contains("照片拍摄成功", result);
    }

    #endregion
}
