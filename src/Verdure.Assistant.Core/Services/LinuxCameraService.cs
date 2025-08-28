using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Verdure.Assistant.Core.Services
{
    /// <summary>
    /// Linux 平台相机服务 - 使用 fswebcam
    /// 适用于树莓派和其他 Linux 设备
    /// </summary>
    public class LinuxCameraService : ICameraService
    {
        private readonly ILogger<LinuxCameraService> _logger;
        private readonly string _fswebcamPath;
        private CameraSettings _defaultSettings;

        public LinuxCameraService(ILogger<LinuxCameraService> logger)
        {
            _logger = logger;
            _fswebcamPath = FindFswebcamPath();
            _defaultSettings = new CameraSettings();
        }

        public async Task<byte[]> CapturePhotoAsync(CameraSettings? settings = null)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                throw new PlatformNotSupportedException("LinuxCameraService only supports Linux platform");
            }

            settings ??= _defaultSettings;
            
            if (string.IsNullOrEmpty(_fswebcamPath))
            {
                throw new InvalidOperationException("fswebcam not found. Please install it: sudo apt-get install fswebcam");
            }

            var fileName = $"capture_{Guid.NewGuid():N}.jpg";
            var filePath = Path.Combine("/tmp", fileName);

            try
            {
                var args = BuildArguments(settings, filePath);
                
                _logger.LogInformation("Capturing photo with fswebcam: {Args}", args);

                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _fswebcamPath,
                        Arguments = args,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                
                var stdout = await process.StandardOutput.ReadToEndAsync();
                var stderr = await process.StandardError.ReadToEndAsync();
                
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    _logger.LogError("fswebcam failed with exit code {ExitCode}. Error: {Error}", 
                        process.ExitCode, stderr);
                    throw new InvalidOperationException($"fswebcam failed: {stderr}");
                }

                if (!File.Exists(filePath))
                {
                    throw new InvalidOperationException("Photo capture failed - file not created");
                }

                var imageBytes = await File.ReadAllBytesAsync(filePath);
                
                _logger.LogInformation("Photo captured successfully. Size: {Size} bytes", imageBytes.Length);
                
                return imageBytes;
            }
            finally
            {
                // 清理临时文件
                if (File.Exists(filePath))
                {
                    try
                    {
                        File.Delete(filePath);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete temporary file: {FilePath}", filePath);
                    }
                }
            }
        }

        public async Task<bool> IsAvailableAsync()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return false;
            }

            if (string.IsNullOrEmpty(_fswebcamPath))
            {
                return false;
            }

            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = _fswebcamPath,
                        Arguments = "--help",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                await process.WaitForExitAsync();

                // fswebcam --help 通常返回 1，但这是正常的
                return process.ExitCode == 1 || process.ExitCode == 0;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to check fswebcam availability");
                return false;
            }
        }

        public async Task<string[]> GetSupportedResolutionsAsync()
        {
            return await Task.FromResult(new[]
            {
                "320x240",
                "640x480", 
                "800x600",
                "1024x768",
                "1280x720",
                "1280x960",
                "1600x1200",
                "1920x1080"
            });
        }

        public async Task<CameraInfo> GetCameraInfoAsync()
        {
            var info = new CameraInfo
            {
                Name = "Linux USB Camera",
                Device = _defaultSettings.Device,
                Platform = "Linux",
                IsAvailable = await IsAvailableAsync(),
                SupportedFormats = new[] { "JPEG", "PNG" },
                SupportedResolutions = await GetSupportedResolutionsAsync()
            };

            return info;
        }

        private string BuildArguments(CameraSettings settings, string outputPath)
        {
            var args = new List<string>
            {
                $"-r {settings.Width}x{settings.Height}",
                "--no-banner",
                $"-S {settings.SkipFrames}",
                $"--jpeg {settings.JpegQuality}"
            };

            if (!string.IsNullOrEmpty(settings.Device))
            {
                args.Add($"-d {settings.Device}");
            }

            if (settings.AddTimestamp)
            {
                args.Add($"--timestamp '{settings.TimestampFormat}'");
                args.Add("--font 'sans:16'");
            }

            args.Add(outputPath);

            return string.Join(" ", args);
        }

        private string FindFswebcamPath()
        {
            var possiblePaths = new[]
            {
                "/usr/bin/fswebcam",
                "/usr/local/bin/fswebcam",
                "/bin/fswebcam"
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    _logger.LogDebug("Found fswebcam at: {Path}", path);
                    return path;
                }
            }

            // 尝试使用 which 命令查找
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "which",
                        Arguments = "fswebcam",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();

                if (process.ExitCode == 0 && !string.IsNullOrEmpty(output))
                {
                    _logger.LogDebug("Found fswebcam via which: {Path}", output);
                    return output;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to find fswebcam using which command");
            }

            _logger.LogWarning("fswebcam not found. Please install it: sudo apt-get install fswebcam");
            return string.Empty;
        }
    }
}
