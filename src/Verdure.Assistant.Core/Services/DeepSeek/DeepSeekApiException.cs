using System.Net;

namespace Verdure.Assistant.Core.Services.DeepSeek;

/// <summary>
/// DeepSeek API 调用失败（HTTP 错误、200 响应体内的错误、网络错误或超时）
/// </summary>
public sealed class DeepSeekApiException : Exception
{
    public DeepSeekApiException(
        string message,
        HttpStatusCode? statusCode = null,
        string? errorType = null,
        string? errorCode = null,
        string? param = null,
        bool isRetryable = false,
        TimeSpan? retryAfter = null,
        string? responseBody = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ErrorType = errorType;
        ErrorCode = errorCode;
        Param = param;
        IsRetryable = isRetryable;
        RetryAfter = retryAfter;
        ResponseBody = responseBody;
    }

    /// <summary>HTTP 状态码；网络错误/超时为 null</summary>
    public HttpStatusCode? StatusCode { get; }

    /// <summary>错误 JSON 中的 error.type</summary>
    public string? ErrorType { get; }

    /// <summary>错误 JSON 中的 error.code</summary>
    public string? ErrorCode { get; }

    /// <summary>错误 JSON 中的 error.param</summary>
    public string? Param { get; }

    /// <summary>是否值得重试（429/500/503 等或网络错误）</summary>
    public bool IsRetryable { get; }

    /// <summary>服务端通过 Retry-After 建议的等待时间</summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>原始响应体（不含 API Key）</summary>
    public string? ResponseBody { get; }

    /// <summary>401：API Key 错误</summary>
    public bool IsAuthenticationError => StatusCode == HttpStatusCode.Unauthorized;

    /// <summary>402：余额不足</summary>
    public bool IsInsufficientBalance => StatusCode == HttpStatusCode.PaymentRequired;

    /// <summary>429：触发限速</summary>
    public bool IsRateLimited => StatusCode == HttpStatusCode.TooManyRequests;

    /// <summary>该状态码是否可重试：408/429/500/502/503/504</summary>
    public static bool IsRetryableStatus(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.RequestTimeout or
        HttpStatusCode.TooManyRequests or
        HttpStatusCode.InternalServerError or
        HttpStatusCode.BadGateway or
        HttpStatusCode.ServiceUnavailable or
        HttpStatusCode.GatewayTimeout;

    internal static string Describe(HttpStatusCode status) => (int)status switch
    {
        400 => "请求体格式错误",
        401 => "认证失败，请检查 API Key",
        402 => "账户余额不足",
        408 => "请求超时",
        413 => "请求体过大",
        422 => "请求参数错误",
        429 => "请求速率达到上限",
        500 => "服务器内部故障",
        503 => "服务器繁忙",
        504 => "网关超时",
        _ => "请求失败"
    };
}
