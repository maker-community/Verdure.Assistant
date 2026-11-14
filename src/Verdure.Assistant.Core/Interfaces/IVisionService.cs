namespace Verdure.Assistant.Core.Interfaces;

/// <summary>
/// 图像理解：给一张图和一个问题，返回文字回答。供拍照/截屏等设备工具使用，与具体对话后端无关。
/// </summary>
public interface IVisionService
{
    /// <summary>当前是否可用（例如已配置 API Key）</summary>
    bool IsAvailable { get; }

    /// <summary>失败时抛出异常（API 错误、网络错误、取消）；调用方负责转成用户可读的提示</summary>
    Task<string> ExplainAsync(byte[] image, string? mimeType, string question, CancellationToken cancellationToken = default);
}
