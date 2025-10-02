using Microsoft.Extensions.Options;
using Verdure.Assistant.Api.Models;
using Verdure.Assistant.Api.Services.WiFi;

namespace Verdure.Assistant.Api.Services.Robot;

/// <summary>
/// 时间显示后台服务 - 每秒更新1.47寸屏幕的时间显示
/// 当网络连接时，同时显示设备IP和服务端口
/// </summary>
public class TimeDisplayService : BackgroundService
{
    private readonly DisplayService _displayService;
    private readonly ILogger<TimeDisplayService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private WiFiNetworkManager? _networkManager;
    private readonly DeviceConfig _deviceConfig;
    private readonly WiFiSetupConfig _config;

    public TimeDisplayService(
        DisplayService displayService,
        ILogger<TimeDisplayService> logger,
        IServiceProvider serviceProvider,
        IOptions<WiFiSetupConfig> config)
    {
        _displayService = displayService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _config = config.Value ?? throw new ArgumentNullException(nameof(config));
        _deviceConfig = _config.DeviceConfig;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("时间显示后台服务启动");

        // 等待一小段时间让其他服务初始化完成
        await Task.Delay(5000, stoppingToken);

        string? lastIpAddress = null;
        // 初始化服务
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        var networkManagerLogger = loggerFactory.CreateLogger<WiFiNetworkManager>();
        _networkManager = new WiFiNetworkManager(networkManagerLogger, _deviceConfig);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("时间显示服务循环开始");
                var currentIpAddress = await _networkManager.GetWiFiConnectedIpAddressAsync();
                _logger.LogDebug("当前设备IP地址: {IpAddress}", currentIpAddress ?? "无");
                if (currentIpAddress != lastIpAddress)
                {
                    lastIpAddress = currentIpAddress;
                    _logger.LogInformation("设备IP地址更新: {IpAddress}", currentIpAddress ?? "无");
                }
                // 显示时间（如果有网络，同时显示IP和端口）
                await _displayService.DisplayTimeWithNetworkInfoAsync(currentIpAddress, stoppingToken);

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