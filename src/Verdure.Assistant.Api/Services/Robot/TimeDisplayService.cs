namespace Verdure.Assistant.Api.Services.Robot;

/// <summary>
/// 时间显示后台服务 - 每秒更新1.47寸屏幕的时间显示
/// </summary>
public class TimeDisplayService : BackgroundService
{
    private readonly DisplayService _displayService;
    private readonly ILogger<TimeDisplayService> _logger;

    public TimeDisplayService(DisplayService displayService, ILogger<TimeDisplayService> logger)
    {
        _displayService = displayService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("时间显示后台服务启动");
        
        // 等待一小段时间让其他服务初始化完成
        await Task.Delay(1000, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _displayService.DisplayTimeAsync(stoppingToken);
                
                // 每秒更新一次
                await Task.Delay(1000, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // 正常停止
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "时间显示服务发生错误");
                
                // 错误后等待5秒再重试
                await Task.Delay(5000, stoppingToken);
            }
        }
        
        _logger.LogInformation("时间显示后台服务停止");
    }
}