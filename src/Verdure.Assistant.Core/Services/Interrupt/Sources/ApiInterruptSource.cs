using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Verdure.Assistant.Core.Services.Interrupt.Sources;

/// <summary>
/// API打断源 - 用于处理来自API项目的打断请求
/// API interrupt source for handling interrupt requests from API project
/// </summary>
public class ApiInterruptSource : InterruptSourceBase
{
    public ApiInterruptSource(ILogger<ApiInterruptSource>? logger = null)
        : base("API", InterruptTypes.Api, logger)
    {
    }

    protected override async Task MonitoringLoopAsync()
    {
        _logger?.LogInformation("API interrupt monitoring started");

        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                // API打断通过外部调用TriggerApiInterrupt方法触发
                // 这里只需要保持监听循环
                await Task.Delay(1000, _cancellationTokenSource.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error in API interrupt monitoring loop");
                await Task.Delay(1000, _cancellationTokenSource.Token);
            }
        }
    }

    /// <summary>
    /// 触发API打断
    /// </summary>
    /// <param name="endpoint">API端点</param>
    /// <param name="requestData">请求数据</param>
    public void TriggerApiInterrupt(string endpoint, object? requestData = null)
    {
        if (!_isPaused && IsEnabled)
        {
            TriggerInterrupt($"API interrupt from endpoint: {endpoint}", requestData, priority: 6);
        }
    }

    /// <summary>
    /// 触发来自外部系统的打断
    /// </summary>
    /// <param name="source">打断来源</param>
    /// <param name="description">描述</param>
    /// <param name="data">数据</param>
    public void TriggerExternalInterrupt(string source, string description, object? data = null)
    {
        if (!_isPaused && IsEnabled)
        {
            TriggerInterrupt($"External interrupt from {source}: {description}", data, priority: 5);
        }
    }
}