using System;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Services.MCP;

namespace Verdure.Assistant.Core.Services
{
    /// <summary>
    /// 相机服务工厂 - 根据平台自动选择合适的相机服务实现
    /// </summary>
    public static class CameraServiceFactory
    {
        /// <summary>
        /// 创建适用于当前平台的相机服务
        /// </summary>
        /// <param name="serviceProvider">服务提供者</param>
        /// <param name="forceType">强制使用特定类型（可选）</param>
        /// <returns>相机服务实例</returns>
        public static ICameraService CreateCameraService(IServiceProvider serviceProvider, CameraServiceType? forceType = null)
        {
            var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
            
            var serviceType = forceType ?? DetectPlatformServiceType();
            
            return serviceType switch
            {
                CameraServiceType.Linux => new LinuxCameraService(
                    loggerFactory?.CreateLogger<LinuxCameraService>() ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<LinuxCameraService>.Instance),
                
                CameraServiceType.Windows => new WindowsCameraService(
                    loggerFactory?.CreateLogger<WindowsCameraService>() ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<WindowsCameraService>.Instance),
                
                CameraServiceType.Mock => new MockCameraService(
                    loggerFactory?.CreateLogger<MockCameraService>() ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<MockCameraService>.Instance),
                
                _ => new MockCameraService(
                    loggerFactory?.CreateLogger<MockCameraService>() ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<MockCameraService>.Instance)
            };
        }

        /// <summary>
        /// 检测当前平台应该使用的相机服务类型
        /// </summary>
        /// <returns>相机服务类型</returns>
        public static CameraServiceType DetectPlatformServiceType()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return CameraServiceType.Linux;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return CameraServiceType.Windows;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                // macOS 暂时使用 Mock 服务
                return CameraServiceType.Mock;
            }
            else
            {
                return CameraServiceType.Mock;
            }
        }

        /// <summary>
        /// 获取当前平台信息的描述
        /// </summary>
        /// <returns>平台信息</returns>
        public static string GetPlatformInfo()
        {
            var platform = "Unknown";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                platform = "Linux";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                platform = "Windows";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                platform = "macOS";

            return $"{platform} ({RuntimeInformation.OSArchitecture}) - {RuntimeInformation.OSDescription}";
        }
    }

    /// <summary>
    /// 相机服务类型枚举
    /// </summary>
    public enum CameraServiceType
    {
        /// <summary>
        /// Linux 平台服务（使用 fswebcam）
        /// </summary>
        Linux,
        
        /// <summary>
        /// Windows 平台服务（使用 PowerShell）
        /// </summary>
        Windows,
        
        /// <summary>
        /// 模拟服务（用于测试或不支持的平台）
        /// </summary>
        Mock
    }

    /// <summary>
    /// 服务注册扩展方法
    /// </summary>
    public static class CameraServiceExtensions
    {
        /// <summary>
        /// 注册相机服务到依赖注入容器
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <param name="forceType">强制使用特定类型（可选）</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddCameraService(this IServiceCollection services, CameraServiceType? forceType = null)
        {
            services.AddSingleton<ICameraService>(serviceProvider =>
            {
                return CameraServiceFactory.CreateCameraService(serviceProvider, forceType);
            });

            return services;
        }

        /// <summary>
        /// 注册增强的 MCP 相机设备
        /// </summary>
        /// <param name="services">服务集合</param>
        /// <returns>服务集合</returns>
        public static IServiceCollection AddEnhancedMcpCameraDevice(this IServiceCollection services)
        {
            services.AddTransient<EnhancedMcpCameraDevice>();
            return services;
        }
    }
}
