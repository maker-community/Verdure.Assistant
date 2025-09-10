namespace Verdure.Assistant.Core.Interfaces;

/// <summary>
/// 平台资源访问服务接口
/// 为不同平台提供统一的资源访问方式
/// </summary>
public interface IPlatformResourceService
{
    /// <summary>
    /// 获取关键词模型目录路径
    /// </summary>
    /// <returns>包含关键词模型文件的目录路径，如果获取失败返回null</returns>
    Task<string?> GetKeywordModelsDirectoryAsync();

    /// <summary>
    /// 获取指定资源文件的本地路径
    /// </summary>
    /// <param name="resourcePath">资源相对路径</param>
    /// <returns>可访问的本地文件路径，如果资源不存在返回null</returns>
    Task<string?> GetResourceFilePathAsync(string resourcePath);

    /// <summary>
    /// 列出可用的关键词模型文件
    /// </summary>
    /// <returns>可用的关键词模型文件名数组</returns>
    Task<string[]> GetAvailableKeywordModelsAsync();
}
