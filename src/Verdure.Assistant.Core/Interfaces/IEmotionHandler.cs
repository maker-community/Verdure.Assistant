namespace Verdure.Assistant.Core.Interfaces;

/// <summary>
/// 情感处理服务接口 - 用于处理LLM情感事件
/// </summary>
public interface IEmotionHandler
{
    /// <summary>
    /// 处理情感事件
    /// </summary>
    Task HandleEmotionAsync(string emotion, string? context = null);
    
    /// <summary>
    /// 检查是否可用
    /// </summary>
    bool IsAvailable { get; }
}