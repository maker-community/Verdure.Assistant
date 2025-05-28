using PortAudioSharp;
using System;

// 测试文件来了解PortAudioSharp2的API
class TestPortAudio
{
    static void Main()
    {
        try
        {
            // 查看可用的类型和方法
            Console.WriteLine("Testing PortAudioSharp2 API...");
            
            // 测试静态方法
            Console.WriteLine($"Version: {PortAudio.VersionText}");
            
            // 初始化
            PortAudio.Initialize();
            
            // 获取设备信息
            var deviceCount = PortAudio.DeviceCount;
            Console.WriteLine($"Device count: {deviceCount}");
            
            var defaultInput = PortAudio.DefaultInputDevice;
            Console.WriteLine($"Default input device: {defaultInput?.Name}");
            
            // 终止
            PortAudio.Terminate();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }
}
