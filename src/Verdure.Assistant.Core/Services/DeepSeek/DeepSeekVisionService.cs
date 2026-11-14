using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Interfaces;

namespace Verdure.Assistant.Core.Services.DeepSeek;

/// <summary>
/// 用 DeepSeek 图片理解（deepseek-flash）回答关于一张图片的问题。每次是独立的一次请求，不读写对话历史，也不开思考模式（更快）。
/// </summary>
public sealed class DeepSeekVisionService : IVisionService
{
    public const string DefaultQuestion = "请描述这张图片的内容。";

    internal const string SystemPrompt =
        "你是一个视觉助手。请根据图片回答用户的问题，使用简洁、准确的中文，直接给出结论；" +
        "回答会被朗读，所以不要使用 Markdown、列表或表情符号。看不清或不确定的地方请如实说明。";

    private readonly IDeepSeekClient _client;
    private readonly DeepSeekOptions _options;
    private readonly ILogger<DeepSeekVisionService>? _logger;

    public DeepSeekVisionService(IDeepSeekClient client, DeepSeekOptions options, ILogger<DeepSeekVisionService>? logger = null)
    {
        _client = client;
        _options = options;
        _logger = logger;
    }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_options.ApiKey);

    public async Task<string> ExplainAsync(byte[] image, string? mimeType, string question, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (image.Length == 0)
        {
            throw new ArgumentException("图片为空", nameof(image));
        }

        var picture = mimeType != null && DeepSeekImage.DetectMimeType(image) == null
            ? new DeepSeekImage(image, mimeType)
            : DeepSeekImage.FromBytes(image);

        var request = new ChatCompletionRequest
        {
            // 图片理解由 deepseek-flash 提供；不跟随用户在对话里选的模型
            Model = DeepSeekModelNames.Flash,
            Thinking = DeepSeekThinking.Off,
            MaxTokens = 800,
            Messages =
            {
                DeepSeekMessage.System(SystemPrompt),
                DeepSeekMessage.User(string.IsNullOrWhiteSpace(question) ? DefaultQuestion : question, new[] { picture })
            }
        };

        var started = Environment.TickCount64;
        var response = await _client.CreateChatCompletionAsync(request, cancellationToken).ConfigureAwait(false);
        var text = response.Content?.Trim();
        _logger?.LogInformation("图像理解完成：图片 {Kb}KB，耗时 {Ms}ms，回答 {Chars} 字",
            image.Length / 1024, Environment.TickCount64 - started, text?.Length ?? 0);

        return string.IsNullOrEmpty(text) ? "没有识别出图片内容。" : text;
    }
}
