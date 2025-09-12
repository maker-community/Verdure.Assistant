using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Interfaces;

namespace Verdure.Assistant.MAUI.Services;

/// <summary>
/// MAUI平台资源访问服务
/// 提供对MAUI应用包内资源的访问
/// </summary>
public class MauiResourceService : IPlatformResourceService
{
    private readonly ILogger<MauiResourceService>? _logger;

    public MauiResourceService(ILogger<MauiResourceService>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 获取MAUI应用包内的资源文件路径
    /// </summary>
    /// <param name="resourcePath">资源相对路径（如 "keywords/keyword_xiaodian.table"）</param>
    /// <returns>可访问的文件路径，如果资源不存在则返回null</returns>
    public async Task<string?> GetResourceFilePathAsync(string resourcePath)
    {
        try
        {
            // 尝试打开应用包内的文件
            using var stream = await Microsoft.Maui.Storage.FileSystem.Current.OpenAppPackageFileAsync(resourcePath);
            if (stream != null)
            {
                // 如果文件存在于应用包中，我们需要将其复制到应用数据目录
                // 因为Microsoft.CognitiveServices.Speech需要直接的文件路径访问
                var localPath = Path.Combine(Microsoft.Maui.Storage.FileSystem.Current.AppDataDirectory, resourcePath);
                var localDir = Path.GetDirectoryName(localPath);
                
                if (!string.IsNullOrEmpty(localDir) && !Directory.Exists(localDir))
                {
                    Directory.CreateDirectory(localDir);
                }

                // 如果本地文件不存在，则复制
                if (!File.Exists(localPath))
                {
                    using var fileStream = File.Create(localPath);
                    await stream.CopyToAsync(fileStream);
                    _logger?.LogDebug("已复制资源文件到本地: {LocalPath}", localPath);
                }
                else
                {
                    _logger?.LogDebug("本地资源文件已存在: {LocalPath}", localPath);
                }

                return localPath;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("无法访问资源文件 {ResourcePath}: {Error}", resourcePath, ex.Message);
        }

        return null;
    }

    /// <summary>
    /// 获取关键词模型目录路径
    /// </summary>
    /// <returns>包含关键词模型文件的目录路径</returns>
    public async Task<string?> GetKeywordModelsDirectoryAsync()
    {
        try
        {
            // 尝试获取keywords目录下的任一文件来确定目录存在
            var testFiles = new[] { "keyword_xiaodian.table", "keyword_cortana.table" };
            
            foreach (var testFile in testFiles)
            {
                var filePath = await GetResourceFilePathAsync($"keywords/{testFile}");
                if (!string.IsNullOrEmpty(filePath))
                {
                    var directory = Path.GetDirectoryName(filePath);
                    _logger?.LogDebug("找到关键词模型目录: {Directory}", directory);
                    return directory;
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("获取关键词模型目录失败: {Error}", ex.Message);
        }

        return null;
    }

    /// <summary>
    /// 列出keywords目录下的所有.table文件
    /// </summary>
    /// <returns>可用的关键词模型文件列表</returns>
    public async Task<string[]> GetAvailableKeywordModelsAsync()
    {
        var models = new List<string>();
        
        try
        {
            var keywordsDir = await GetKeywordModelsDirectoryAsync();
            if (!string.IsNullOrEmpty(keywordsDir) && Directory.Exists(keywordsDir))
            {
                var tableFiles = Directory.GetFiles(keywordsDir, "*.table");
                models.AddRange(tableFiles.Select(Path.GetFileName).Where(name => !string.IsNullOrEmpty(name))!);
                _logger?.LogDebug("找到 {Count} 个关键词模型文件", models.Count);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning("列出关键词模型文件失败: {Error}", ex.Message);
        }

        return models.ToArray();
    }
}
