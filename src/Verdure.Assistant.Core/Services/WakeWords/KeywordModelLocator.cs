using Microsoft.Extensions.Logging;
using System.Reflection;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Models;

namespace Verdure.Assistant.Core.Services;

/// <summary>
/// 关键词模型文件定位器：统一处理平台资源服务（MAUI 等）、配置路径以及开发/发布目录布局下的模型查找。
/// 全部为异步实现，避免在 UI 线程上对异步资源服务调用 .Result 造成死锁。
/// </summary>
internal sealed class KeywordModelLocator
{
    private const string DefaultModel = "keyword_xiaodian.table";

    private readonly ILogger? _logger;
    private readonly IPlatformResourceService? _platformResourceService;

    public KeywordModelLocator(IPlatformResourceService? platformResourceService, ILogger? logger)
    {
        _platformResourceService = platformResourceService;
        _logger = logger;
    }

    /// <summary>
    /// 解析当前（或指定）模型的完整路径；找不到时返回 null
    /// </summary>
    public async Task<string?> ResolveModelPathAsync(VerdureConfig? config, string? modelFileName = null)
    {
        var model = modelFileName ?? config?.KeywordModels.CurrentModel ?? DefaultModel;

        if (_platformResourceService != null)
        {
            try
            {
                var platformPath = await _platformResourceService
                    .GetResourceFilePathAsync($"keywords/{model}").ConfigureAwait(false);
                if (!string.IsNullOrEmpty(platformPath))
                {
                    _logger?.LogDebug("通过平台资源服务获取模型文件: {Path}", platformPath);
                    return platformPath;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("平台资源服务获取模型文件失败，回退到文件系统: {Error}", ex.Message);
            }
        }

        var directory = await GetModelsDirectoryAsync(config).ConfigureAwait(false);
        var path = Path.Combine(directory, model);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// 获取可用的关键词模型文件名
    /// </summary>
    public async Task<string[]> GetAvailableModelsAsync(VerdureConfig? config)
    {
        if (_platformResourceService != null)
        {
            try
            {
                var platformModels = await _platformResourceService.GetAvailableKeywordModelsAsync().ConfigureAwait(false);
                if (platformModels.Length > 0)
                {
                    return platformModels;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("平台资源服务获取模型列表失败: {Error}", ex.Message);
            }
        }

        try
        {
            var directory = await GetModelsDirectoryAsync(config).ConfigureAwait(false);
            if (Directory.Exists(directory))
            {
                return Directory.GetFiles(directory, "*.table")
                    .Select(Path.GetFileName)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .Cast<string>()
                    .ToArray();
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "获取关键词模型列表失败");
        }

        return Array.Empty<string>();
    }

    private async Task<string> GetModelsDirectoryAsync(VerdureConfig? config)
    {
        var configured = config?.KeywordModels.ModelsPath;
        if (!string.IsNullOrEmpty(configured))
        {
            return Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, configured);
        }

        if (_platformResourceService != null)
        {
            try
            {
                var platformPath = await _platformResourceService.GetKeywordModelsDirectoryAsync().ConfigureAwait(false);
                if (!string.IsNullOrEmpty(platformPath) && Directory.Exists(platformPath))
                {
                    return platformPath;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug("平台资源服务获取模型路径失败: {Error}", ex.Message);
            }
        }

        return GetDefaultModelsPath();
    }

    private string GetDefaultModelsPath()
    {
        var assemblyPath = AppDomain.CurrentDomain.BaseDirectory;

        // 随程序发布的目录（WinUI Assets/keywords、MAUI keywords、Console ModelFiles）
        foreach (var candidate in new[]
        {
            Path.Combine(assemblyPath, "ModelFiles"),
            Path.Combine(assemblyPath, "Assets", "keywords"),
            Path.Combine(assemblyPath, "keywords"),
        })
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        if (IsMauiEnvironment())
        {
            var appDataDir = GetMauiAppDataDirectory();
            if (!string.IsNullOrEmpty(appDataDir))
            {
                var mauiKeywordsPath = Path.Combine(appDataDir, "keywords");
                if (Directory.Exists(mauiKeywordsPath))
                {
                    return mauiKeywordsPath;
                }
            }
        }

        // 开发环境：向上查找解决方案根目录
        var currentDir = new DirectoryInfo(assemblyPath);
        while (currentDir != null
               && !File.Exists(Path.Combine(currentDir.FullName, "Verdure.Assistant.slnx"))
               && !File.Exists(Path.Combine(currentDir.FullName, "Verdure.Assistant.sln")))
        {
            currentDir = currentDir.Parent;
        }

        if (currentDir != null)
        {
            foreach (var candidate in new[]
            {
                Path.Combine(currentDir.FullName, "src", "Verdure.Assistant.WinUI", "Assets", "keywords"),
                Path.Combine(currentDir.FullName, "src", "Verdure.Assistant.MAUI", "Resources", "Raw", "keywords"),
            })
            {
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return Path.Combine(assemblyPath, "..", "..", "..", "..", "Verdure.Assistant.WinUI", "Assets", "keywords");
    }

    private bool IsMauiEnvironment()
    {
        try
        {
            if (AppDomain.CurrentDomain.GetAssemblies()
                .Any(a => a.GetName().Name?.StartsWith("Microsoft.Maui") == true))
            {
                return true;
            }

            return Assembly.GetEntryAssembly()?.GetName().Name?.Contains("MAUI") == true;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("MAUI环境检测失败: {Error}", ex.Message);
            return false;
        }
    }

    private string? GetMauiAppDataDirectory()
    {
        try
        {
            var fileSystemType = Type.GetType("Microsoft.Maui.Storage.FileSystem, Microsoft.Maui.Essentials");
            var current = fileSystemType?.GetProperty("Current", BindingFlags.Static | BindingFlags.Public)?.GetValue(null);
            return current?.GetType().GetProperty("AppDataDirectory")?.GetValue(current) as string;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug("无法获取MAUI应用数据目录: {Error}", ex.Message);
            return null;
        }
    }
}
