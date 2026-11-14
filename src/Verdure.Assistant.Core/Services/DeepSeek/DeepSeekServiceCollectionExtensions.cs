using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Services.MCP;

namespace Verdure.Assistant.Core.Services.DeepSeek;

public static class DeepSeekServiceCollectionExtensions
{
    /// <summary>
    /// 注册 <see cref="DeepSeekOptions"/>、<see cref="IDeepSeekClient"/>（单例，内部持有长生命周期 HttpClient）
    /// 与 <see cref="DeepSeekChatSession"/>（瞬时，每次解析得到独立会话；若容器中有 <see cref="IDeepSeekToolExecutor"/> 则自动使用）。
    /// </summary>
    public static IServiceCollection AddDeepSeek(this IServiceCollection services, Action<DeepSeekOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(_ =>
        {
            var options = new DeepSeekOptions();
            configure?.Invoke(options);
            return options;
        });

        services.AddSingleton<IDeepSeekClient>(sp =>
        {
            var handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) };
            // 超时由 DeepSeekOptions.TimeoutSeconds 控制，避免 HttpClient 默认 100 秒限制流式长思考
            var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
            return new DeepSeekClient(
                http,
                sp.GetRequiredService<DeepSeekOptions>(),
                sp.GetService<ILogger<DeepSeekClient>>());
        });

        services.TryAddSingleton<IDeepSeekSpeechClient>(sp =>
            new DeepSeekSpeechClient(
                new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(2) })
                {
                    Timeout = Timeout.InfiniteTimeSpan
                },
                sp.GetRequiredService<DeepSeekOptions>(),
                sp.GetService<ILogger<DeepSeekSpeechClient>>()));

        // 图像理解：供拍照/截屏等设备工具使用（需要 using Verdure.Assistant.Core.Interfaces）
        services.TryAddSingleton<Verdure.Assistant.Core.Interfaces.IVisionService>(sp => new DeepSeekVisionService(
            sp.GetRequiredService<IDeepSeekClient>(),
            sp.GetRequiredService<DeepSeekOptions>(),
            sp.GetService<ILogger<DeepSeekVisionService>>()));

        services.AddTransient(sp => new DeepSeekChatSession(
            sp.GetRequiredService<IDeepSeekClient>(),
            sp.GetRequiredService<DeepSeekOptions>(),
            sp.GetService<IDeepSeekToolExecutor>(),
            sp.GetService<ILogger<DeepSeekChatSession>>()));

        return services;
    }

    /// <summary>
    /// 把容器中已注册的 <see cref="McpServer"/> 暴露为 DeepSeek 工具（需先注册 McpServer）。
    /// </summary>
    public static IServiceCollection AddDeepSeekMcpTools(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IDeepSeekToolExecutor>(sp => new McpDeepSeekToolExecutor(
            sp.GetRequiredService<McpServer>(),
            sp.GetService<ILogger<McpDeepSeekToolExecutor>>()));
        return services;
    }
}
