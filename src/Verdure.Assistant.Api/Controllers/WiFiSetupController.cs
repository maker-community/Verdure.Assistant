using Microsoft.AspNetCore.Mvc;
using System.Runtime.InteropServices;
using Verdure.Assistant.Api.Models;
using Verdure.Assistant.Api.Services.WiFi;

namespace Verdure.Assistant.Api.Controllers;

/// <summary>
/// WiFi配网控制器
/// </summary>
[ApiController]
[Route("api/wifi")]
public class WiFiSetupController : ControllerBase
{
    private readonly ILogger<WiFiSetupController> _logger;
    private readonly WiFiNetworkManager _networkManager;
    private readonly LocalizationService _localizationService;

    public WiFiSetupController(
        ILogger<WiFiSetupController> logger,
        WiFiNetworkManager networkManager,
        LocalizationService localizationService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _networkManager = networkManager ?? throw new ArgumentNullException(nameof(networkManager));
        _localizationService = localizationService ?? throw new ArgumentNullException(nameof(localizationService));
    }

    /// <summary>
    /// 获取WiFi配网主页
    /// </summary>
    [HttpGet("setup")]
    [HttpGet("")]
    public IActionResult GetSetupPage([FromQuery] string? lang = null)
    {
        try
        {
            // 设置语言
            if (!string.IsNullOrEmpty(lang))
            {
                _localizationService.SetLanguage(lang);
            }

            var strings = _localizationService.GetAllStrings();
            var currentLanguage = _localizationService.GetCurrentLanguage();
            var languages = _localizationService.GetAvailableLanguages()
                .Select(l => new LanguageItem { Code = l, Name = _localizationService.GetLanguageDisplayName(l) })
                .ToList();

            // 生成HTML页面
            var html = GenerateSetupPageHtml(strings, currentLanguage, languages);
            
            return Content(html, "text/html");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取WiFi配网页面失败");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// 处理WiFi配置提交
    /// </summary>
    [HttpPost("config")]
    public async Task<IActionResult> ConfigureWiFi([FromForm] WiFiConfigRequest request)
    {
        try
        {
            // 设置语言
            if (!string.IsNullOrEmpty(request.Language))
            {
                _localizationService.SetLanguage(request.Language);
            }

            var strings = _localizationService.GetAllStrings();

            // 验证输入
            if (string.IsNullOrEmpty(request.Ssid))
            {
                var errorHtml = GenerateErrorPageHtml(
                    strings["Error"],
                    strings["WifiNameRequired"],
                    strings["BackLink"]);
                return Content(errorHtml, "text/html");
            }

            // 生成成功页面
            var successHtml = GenerateSuccessPageHtml(strings, request.Ssid);

            // 在后台处理WiFi配置
            _ = Task.Run(async () =>
            {
                try
                {
                    await SaveWifiConfigAsync(request.Ssid, request.Password);
                    await Task.Delay(10_000); // 等待10秒
                    await _networkManager.RebootAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "后台WiFi配置失败");
                }
            });

            return Content(successHtml, "text/html");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WiFi配置处理失败");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// 获取WiFi配网状态
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        try
        {
            var isNetworkAvailable = await _networkManager.IsNetworkAvailableAsync();
            var connectedIp = await _networkManager.GetWiFiConnectedIpAddressAsync();

            var status = new
            {
                IsConnected = isNetworkAvailable,
                ConnectedIp = connectedIp,
                Platform = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "Linux" : "Other",
                Timestamp = DateTime.UtcNow
            };

            return Ok(status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "获取WiFi状态失败");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// 生成QR码图片（测试用）
    /// </summary>
    [HttpGet("qrcode")]
    public IActionResult GenerateQrCode([FromQuery] string url = "http://192.168.4.1:5241")
    {
        try
        {
            var outputPath = Path.Combine(Path.GetTempPath(), "wifi_setup_qrcode.png");
            WiFiSetupUtils.GenerateQrCodeImage(url, outputPath);
            
            if (System.IO.File.Exists(outputPath))
            {
                var fileBytes = System.IO.File.ReadAllBytes(outputPath);
                return File(fileBytes, "image/png", "wifi_setup_qrcode.png");
            }

            return NotFound("QR code generation failed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "生成QR码失败");
            return StatusCode(500, "QR code generation failed");
        }
    }

    /// <summary>
    /// 生成测试配网页面图片
    /// </summary>
    [HttpGet("test-page")]
    public IActionResult GenerateTestPage([FromQuery] string url = "http://192.168.4.1:5241", [FromQuery] string ip = "192.168.4.1")
    {
        try
        {
            var outputPath = Path.Combine(Path.GetTempPath(), "wifi_setup_page.png");
            WiFiSetupUtils.GenerateTestConfigPageImage(url, ip, outputPath);
            
            if (System.IO.File.Exists(outputPath))
            {
                var fileBytes = System.IO.File.ReadAllBytes(outputPath);
                return File(fileBytes, "image/png", "wifi_setup_page.png");
            }

            return NotFound("Test page generation failed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "生成测试页面失败");
            return StatusCode(500, "Test page generation failed");
        }
    }

    #region Private Methods

    /// <summary>
    /// 保存WiFi配置
    /// </summary>
    private async Task SaveWifiConfigAsync(string ssid, string password)
    {
        _logger.LogInformation("正在保存WiFi配置: {Ssid}", ssid);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            _logger.LogInformation("非Linux系统，模拟WiFi配置保存");
            return;
        }

        try
        {
            // 关闭热点，恢复接口管理
            await _networkManager.StopHotspotAsync();
            await _networkManager.SetDeviceManagedAsync(true);

            // 启动WiFi连接
            await _networkManager.ConnectDeviceAsync();

            // 连接到WiFi
            var success = await _networkManager.ConnectToWifiAsync(ssid, password);
            if (success)
            {
                _logger.LogInformation("WiFi配置已保存并连接成功");
            }
            else
            {
                _logger.LogError("WiFi配置保存失败");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存WiFi配置时发生错误");
        }
    }

    /// <summary>
    /// 生成设置页面HTML
    /// </summary>
    private string GenerateSetupPageHtml(Dictionary<string, string> strings, string currentLanguage, List<LanguageItem> languages)
    {
        var languageOptions = string.Join("", languages.Select(lang => 
            $"<option value=\"{lang.Code}\" {(lang.Code == currentLanguage ? "selected" : "")}>{lang.Name}</option>"));

        return $@"
<!DOCTYPE html>
<html lang=""{currentLanguage}"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{strings["Title"]}</title>
    <style>
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            max-width: 400px;
            margin: 50px auto;
            padding: 20px;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: #333;
            min-height: 100vh;
        }}
        .container {{
            background: white;
            padding: 30px;
            border-radius: 15px;
            box-shadow: 0 10px 30px rgba(0,0,0,0.3);
        }}
        h1 {{
            text-align: center;
            color: #4a5568;
            margin-bottom: 10px;
            font-size: 24px;
        }}
        .welcome {{
            text-align: center;
            color: #718096;
            margin-bottom: 30px;
            font-size: 14px;
            line-height: 1.5;
        }}
        .form-group {{
            margin-bottom: 20px;
        }}
        label {{
            display: block;
            margin-bottom: 8px;
            font-weight: 600;
            color: #4a5568;
        }}
        input, select {{
            width: 100%;
            padding: 12px;
            border: 2px solid #e2e8f0;
            border-radius: 8px;
            font-size: 16px;
            transition: border-color 0.3s;
            box-sizing: border-box;
        }}
        input:focus, select:focus {{
            outline: none;
            border-color: #667eea;
        }}
        button {{
            width: 100%;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            padding: 12px;
            border: none;
            border-radius: 8px;
            font-size: 16px;
            font-weight: 600;
            cursor: pointer;
            transition: transform 0.2s;
        }}
        button:hover {{
            transform: translateY(-2px);
        }}
        .language-selector {{
            margin-bottom: 20px;
            text-align: center;
        }}
    </style>
</head>
<body>
    <div class=""container"">
        <h1>{strings["Title"]}</h1>
        <p class=""welcome"">{strings["WelcomeMessage"]}</p>
        
        <form action=""/api/wifi/config"" method=""post"">
            <div class=""language-selector"">
                <label for=""language"">{strings["Language"]}:</label>
                <select name=""language"" id=""language"" onchange=""changeLanguage(this.value)"">
                    {languageOptions}
                </select>
            </div>
            
            <div class=""form-group"">
                <label for=""ssid"">{strings["WifiName"]}:</label>
                <input type=""text"" name=""ssid"" id=""ssid"" required placeholder=""{strings["WifiNamePlaceholder"]}"">
            </div>
            
            <div class=""form-group"">
                <label for=""password"">{strings["WifiPassword"]}:</label>
                <input type=""password"" name=""password"" id=""password"" placeholder=""{strings["WifiPasswordPlaceholder"]}"">
            </div>
            
            <button type=""submit"">{strings["Connect"]}</button>
        </form>
    </div>

    <script>
        function changeLanguage(lang) {{
            window.location.href = '?lang=' + lang;
        }}
    </script>
</body>
</html>";
    }

    /// <summary>
    /// 生成错误页面HTML
    /// </summary>
    private string GenerateErrorPageHtml(string errorTitle, string errorMessage, string backLink)
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{errorTitle}</title>
    <style>
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            max-width: 400px;
            margin: 50px auto;
            padding: 20px;
            background: linear-gradient(135deg, #fc8181 0%, #f56565 100%);
            color: #333;
            min-height: 100vh;
        }}
        .container {{
            background: white;
            padding: 30px;
            border-radius: 15px;
            box-shadow: 0 10px 30px rgba(0,0,0,0.3);
            text-align: center;
        }}
        h1 {{ color: #e53e3e; }}
        a {{
            display: inline-block;
            margin-top: 20px;
            color: #667eea;
            text-decoration: none;
            font-weight: 600;
        }}
    </style>
</head>
<body>
    <div class=""container"">
        <h1>{errorTitle}</h1>
        <p>{errorMessage}</p>
        <a href=""/api/wifi/setup"">{backLink}</a>
    </div>
</body>
</html>";
    }

    /// <summary>
    /// 生成成功页面HTML
    /// </summary>
    private string GenerateSuccessPageHtml(Dictionary<string, string> strings, string ssid)
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>{strings["Success"]}</title>
    <style>
        body {{
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            max-width: 400px;
            margin: 50px auto;
            padding: 20px;
            background: linear-gradient(135deg, #48bb78 0%, #38a169 100%);
            color: #333;
            min-height: 100vh;
        }}
        .container {{
            background: white;
            padding: 30px;
            border-radius: 15px;
            box-shadow: 0 10px 30px rgba(0,0,0,0.3);
            text-align: center;
        }}
        h1 {{ color: #38a169; }}
        .spinner {{
            border: 3px solid #f3f3f3;
            border-top: 3px solid #38a169;
            border-radius: 50%;
            width: 40px;
            height: 40px;
            animation: spin 1s linear infinite;
            margin: 20px auto;
        }}
        @keyframes spin {{
            0% {{ transform: rotate(0deg); }}
            100% {{ transform: rotate(360deg); }}
        }}
    </style>
</head>
<body>
    <div class=""container"">
        <h1>{strings["Success"]}</h1>
        <p>{strings["ConnectingTo"]}: <strong>{ssid}</strong></p>
        <div class=""spinner""></div>
        <p>{strings["RestartingMessage"]}</p>
        <p><small>{strings["SuccessMessage"]}</small></p>
    </div>

    <script>
        // 10秒后尝试重定向到根路径
        setTimeout(function() {{
            window.location.href = '/';
        }}, 10000);
    </script>
</body>
</html>";
    }

    #endregion
}

/// <summary>
/// WiFi配置请求模型
/// </summary>
public class WiFiConfigRequest
{
    public string Ssid { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
}