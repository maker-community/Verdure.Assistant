using Microsoft.Extensions.Options;
using System.Runtime.InteropServices;
using Verdure.Assistant.Api.Models;
using Verdure.Assistant.Api.Services.Robot;

namespace Verdure.Assistant.Api.Services.WiFi;

/// <summary>
/// WiFi配网服务 - 整合WiFi功能到现有系统
/// </summary>
public class WiFiSetupService : BackgroundService
{
    private readonly ILogger<WiFiSetupService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly WiFiSetupConfig _config;
    private readonly DeviceConfig _deviceConfig;
    private WiFiNetworkManager? _networkManager;
    private LocalizationService? _localizationService;

    public WiFiSetupService(
        ILogger<WiFiSetupService> logger,
        IServiceProvider serviceProvider,
        IOptions<WiFiSetupConfig> config)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _config = config.Value ?? throw new ArgumentNullException(nameof(config));
        _deviceConfig = _config.DeviceConfig;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_config.Enabled)
        {
            _logger.LogInformation("WiFi配网功能已禁用，跳过启动");
            return;
        }

        _logger.LogInformation("WiFi配网服务启动中...");

        try
        {
            // 初始化服务
            using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var networkManagerLogger = loggerFactory.CreateLogger<WiFiNetworkManager>();
            _networkManager = new WiFiNetworkManager(networkManagerLogger, _deviceConfig);
            _localizationService = new LocalizationService();

            // 启动延时，确保系统服务和网络接口准备就绪
            _logger.LogInformation("启动延时 {DelaySeconds} 秒，等待系统网络接口初始化", _config.StartupDelaySeconds);
            await Task.Delay(_config.StartupDelaySeconds * 1000, stoppingToken);

            if (stoppingToken.IsCancellationRequested)
                return;

            // 主循环 - 持续监控网络状态
            bool isInApMode = false;
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // 检查网络连接状态
                    _logger.LogDebug("检查网络连接状态...");
                    var isNetworkAvailable = await _networkManager.IsNetworkAvailableAsync();

                    if (!isNetworkAvailable && !isInApMode)
                    {
                        // 网络断开且未处于AP模式，启动AP热点
                        _logger.LogInformation("检测到网络断开，启动AP热点模式进行配网");
                        await StartAccessPointModeAsync(stoppingToken);
                        isInApMode = true;
                    }
                    else if (isNetworkAvailable && isInApMode)
                    {
                        // 网络已连接但仍在AP模式，关闭AP热点
                        _logger.LogInformation("检测到网络已连接，关闭AP热点模式");
                        await StopAccessPointModeAsync();
                        await ShowConnectedStatusAsync();
                        isInApMode = false;
                    }
                    else if (isNetworkAvailable && !isInApMode)
                    {
                        // 网络已连接，定期更新显示
                        _logger.LogDebug("网络正常，保持连接状态");
                        await ShowConnectedStatusAsync();
                    }

                    // 每30秒检查一次网络状态
                    await Task.Delay(30000, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "网络状态检查循环发生错误");
                    await Task.Delay(10000, stoppingToken); // 错误后等待10秒再重试
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("WiFi配网服务正常停止");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WiFi配网服务运行时发生错误");
        }
    }

    /// <summary>
    /// 启动AP热点模式
    /// </summary>
    private async Task StartAccessPointModeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var apConfig = _deviceConfig.ApConfig;
            var apIp = !string.IsNullOrWhiteSpace(apConfig.Ip) ? apConfig.Ip : "192.168.4.1";

            _logger.LogInformation("预设热点IP地址: {ApIp}", apIp);

            // 启动AP热点
            var actualApIp = await StartAccessPointAsync(apIp);
            if (string.IsNullOrEmpty(actualApIp))
            {
                _logger.LogError("AP热点启动失败");
                return;
            }

            // 构建配网地址URL
            var configUrl = $"http://{actualApIp}:{_config.WebServerPort}/api/wifi/setup";
            _logger.LogInformation("配网地址: {ConfigUrl}", configUrl);

            // 生成并显示QR码
            await ShowQrCodeOnDisplayAsync(configUrl, actualApIp);

            _logger.LogInformation("WiFi配网模式已启动，等待用户配置...");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动AP热点模式失败");
        }
    }

    /// <summary>
    /// 停止AP热点模式
    /// </summary>
    private async Task StopAccessPointModeAsync()
    {
        try
        {
            if (_networkManager == null)
            {
                _logger.LogWarning("NetworkManager未初始化，无法停止AP热点");
                return;
            }

            _logger.LogInformation("正在停止AP热点模式...");
            var success = await _networkManager.StopHotspotAsync();

            if (success)
            {
                _logger.LogInformation("AP热点已成功停止");
            }
            else
            {
                _logger.LogWarning("停止AP热点可能未完全成功");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止AP热点模式失败");
        }
    }

    /// <summary>
    /// 启动热点
    /// </summary>
    private async Task<string> StartAccessPointAsync(string ip)
    {
        if (_networkManager == null)
        {
            _logger.LogError("NetworkManager未初始化");
            return string.Empty;
        }

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            _logger.LogInformation("非Linux系统，模拟AP热点启动");
            return ip;
        }

        var ap = _deviceConfig.ApConfig;
        _logger.LogInformation("正在启动AP热点: {Ssid}", ap.Ssid);

        var success = await _networkManager.StartHotspotAsync(ap.Ssid, ap.Password);
        if (success)
        {
            _logger.LogInformation("热点启动成功，等待网络接口初始化...");
            await Task.Delay(2000); // 等待网络接口完全初始化

            var actualIp = WiFiSetupUtils.GetHotspotGatewayIp(ip);
            _logger.LogInformation("AP热点已启动，实际网关IP地址: {ActualIp}", actualIp);
            return actualIp;
        }
        else
        {
            _logger.LogError("AP热点启动失败");
            return string.Empty;
        }
    }

    /// <summary>
    /// 在屏幕上显示QR码
    /// </summary>
    private Task ShowQrCodeOnDisplayAsync(string url, string gatewayIp)
    {
        try
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                _logger.LogInformation("非Linux系统，生成测试图片");

                // 生成QR码图片用于测试
                WiFiSetupUtils.GenerateQrCodeImage(url);
                WiFiSetupUtils.GenerateTestConfigPageImage(url, gatewayIp);

                _logger.LogInformation("配网地址: {Url}", url);
                _logger.LogInformation("网关IP: {GatewayIp}", gatewayIp);
                return Task.CompletedTask;
            }

            // 获取显示服务
            var displayService = _serviceProvider.GetService<DisplayService>();
            if (displayService != null)
            {
                _logger.LogInformation("使用现有DisplayService显示QR码");

                // 为2.4寸屏幕生成QR码图像数据
                var qrImage24Data = WiFiSetupUtils.CreateQrCodeWithTextImageData(url, $"IP: {gatewayIp}", 320, 240);

                // 为1.47寸屏幕生成QR码图像数据
                var qrImage47Data = WiFiSetupUtils.CreateQrCodeWithTextImageData(url, $"IP: {gatewayIp}", 320, 172);

                if (qrImage24Data != null && qrImage47Data != null)
                {
                    // 这里可以扩展DisplayService来支持直接发送RGB565数据
                    // 或者集成到现有的显示逻辑中
                    _logger.LogInformation("QR码图像数据已生成，准备显示");

                    // 目前先输出日志，后续可以集成到DisplayService
                    _logger.LogInformation("2.4寸屏幕数据大小: {Size24} bytes", qrImage24Data.Length);
                    _logger.LogInformation("1.47寸屏幕数据大小: {Size47} bytes", qrImage47Data.Length);
                }
            }
            else
            {
                _logger.LogWarning("DisplayService不可用，无法显示QR码");
            }

            _logger.LogInformation("请访问 {Url} 配置WiFi", url);
            _logger.LogInformation("或直接访问网关IP: {GatewayIp}:{Port}", gatewayIp, _config.WebServerPort);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "显示QR码时出错");
            // 生成备用图片
            WiFiSetupUtils.GenerateQrCodeImage(url);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 显示已连接状态
    /// </summary>
    private async Task ShowConnectedStatusAsync()
    {
        try
        {
            if (_networkManager == null)
            {
                _logger.LogError("NetworkManager未初始化");
                return;
            }

            var connectedIp = await _networkManager.GetWiFiConnectedIpAddressAsync();
            if (!string.IsNullOrEmpty(connectedIp))
            {
                _logger.LogInformation("设备已连接WiFi，IP地址: {ConnectedIp}", connectedIp);

                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    _logger.LogInformation("非Linux系统，跳过IP地址屏幕显示");
                    return;
                }

                // 获取显示服务并显示IP地址
                var displayService = _serviceProvider.GetService<DisplayService>();
                if (displayService != null)
                {
                    // 为屏幕生成IP显示图像数据
                    var ipImage24Data = WiFiSetupUtils.CreateIpDisplayImageData(connectedIp, 320, 240);
                    var ipImage47Data = WiFiSetupUtils.CreateIpDisplayImageData(connectedIp, 320, 172);

                    if (ipImage24Data != null && ipImage47Data != null)
                    {
                        _logger.LogInformation("IP地址显示数据已生成");
                        // 这里可以扩展DisplayService来支持IP地址显示
                    }
                }
            }
            else
            {
                _logger.LogWarning("无法获取WiFi连接的IP地址");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "显示连接状态时出错");
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("WiFi配网服务正在停止...");

        try
        {
            // 清理资源
            if (_networkManager != null && RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                await _networkManager.StopHotspotAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WiFi配网服务停止时清理资源失败");
        }

        await base.StopAsync(cancellationToken);
        _logger.LogInformation("WiFi配网服务已停止");
    }
}