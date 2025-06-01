using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Text;

namespace Verdure.Assistant.Core.Services
{
    /// <summary>
    /// 验证码处理服务，对应Python中的_handle_verification_code功能
    /// </summary>
    public interface IVerificationService
    {
        Task<string?> ExtractVerificationCodeAsync(string responseText);
        Task CopyToClipboardAsync(string text);
        Task OpenBrowserAsync(string url);
    }

    public class VerificationService : IVerificationService
    {
        private readonly ILogger<VerificationService>? _logger;

        public VerificationService(ILogger<VerificationService>? logger = null)
        {
            _logger = logger;
        }        /// <summary>
        /// 从服务器响应中提取验证码
        /// </summary>
        public Task<string?> ExtractVerificationCodeAsync(string responseText)
        {
            try
            {
                // 正则表达式匹配验证码模式，支持多种格式
                var patterns = new[]
                {
                    @"验证码[：:\s]*([A-Za-z0-9]{4,8})",
                    @"verification[_\s]*code[：:\s]*([A-Za-z0-9]{4,8})",
                    @"code[：:\s]*([A-Za-z0-9]{4,8})",
                    @"授权码[：:\s]*([A-Za-z0-9]{4,8})"
                };

                foreach (var pattern in patterns)
                {
                    var match = Regex.Match(responseText, pattern, RegexOptions.IgnoreCase);
                    if (match.Success && match.Groups.Count > 1)
                    {
                        var code = match.Groups[1].Value.Trim();
                        _logger?.LogInformation("提取到验证码: {Code}", code);
                        return Task.FromResult<string?>(code);
                    }
                }

                _logger?.LogWarning("未能从响应中提取验证码: {Response}", responseText);
                return Task.FromResult<string?>(null);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "提取验证码时发生错误");
                return Task.FromResult<string?>(null);
            }
        }

        /// <summary>
        /// 复制文本到剪贴板
        /// </summary>
        public async Task CopyToClipboardAsync(string text)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    await CopyToClipboardWindowsAsync(text);
                }
                else if (OperatingSystem.IsLinux())
                {
                    await CopyToClipboardLinuxAsync(text);
                }
                else if (OperatingSystem.IsMacOS())
                {
                    await CopyToClipboardMacOSAsync(text);
                }
                else
                {
                    _logger?.LogWarning("不支持的操作系统，无法复制到剪贴板");
                }

                _logger?.LogInformation("已复制到剪贴板: {Text}", text);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "复制到剪贴板失败");
            }
        }

        /// <summary>
        /// 打开浏览器访问指定URL
        /// </summary>
        public async Task OpenBrowserAsync(string url)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                else if (OperatingSystem.IsLinux())
                {
                    Process.Start("xdg-open", url);
                }
                else if (OperatingSystem.IsMacOS())
                {
                    Process.Start("open", url);
                }
                else
                {
                    _logger?.LogWarning("不支持的操作系统，无法打开浏览器");
                    return;
                }

                _logger?.LogInformation("已打开浏览器: {Url}", url);
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "打开浏览器失败");
            }
        }

        private async Task CopyToClipboardWindowsAsync(string text)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell",                    Arguments = $"-Command \"Set-Clipboard -Value '{text.Replace("'", "''")}'\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync();
        }

        private async Task CopyToClipboardLinuxAsync(string text)
        {
            // 尝试使用 xclip
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "xclip",
                        Arguments = "-selection clipboard",
                        RedirectStandardInput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                await process.StandardInput.WriteAsync(text);
                process.StandardInput.Close();
                await process.WaitForExitAsync();
            }
            catch
            {
                // 尝试使用 xsel
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "xsel",
                        Arguments = "--clipboard --input",
                        RedirectStandardInput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                await process.StandardInput.WriteAsync(text);
                process.StandardInput.Close();
                await process.WaitForExitAsync();
            }
        }

        private async Task CopyToClipboardMacOSAsync(string text)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "pbcopy",
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.StandardInput.WriteAsync(text);
            process.StandardInput.Close();
            await process.WaitForExitAsync();
        }
    }
}
