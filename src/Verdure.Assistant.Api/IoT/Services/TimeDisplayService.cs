using Verdure.Assistant.Api.IoT.Interfaces;

namespace Verdure.Assistant.Api.IoT.Services;

/// <summary>
/// 时间显示后台服务 - 在1.47寸屏幕上持续显示时间
/// </summary>
public class TimeDisplayService : BackgroundService
{
    private readonly IDisplayService _displayService;
    private readonly ILogger<TimeDisplayService> _logger;
    private readonly TimeSpan _updateInterval = TimeSpan.FromSeconds(1); // 每秒更新一次

    public TimeDisplayService(IDisplayService displayService, ILogger<TimeDisplayService> logger)
    {
        _displayService = displayService ?? throw new ArgumentNullException(nameof(displayService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("时间显示服务开始运行");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _displayService.DisplayTimeAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // 正常的取消操作，不记录错误
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "时间显示更新失败");
                    // 即使出错也继续运行，避免服务停止
                }

                try
                {
                    await Task.Delay(_updateInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "时间显示服务发生未处理的异常");
        }
        finally
        {
            _logger.LogInformation("时间显示服务已停止");
        }
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("正在启动时间显示服务...");
        await base.StartAsync(cancellationToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("正在停止时间显示服务...");
        await base.StopAsync(cancellationToken);
    }
}