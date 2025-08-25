using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Console;

namespace Verdure.Assistant.Console.TestMusic
{
    public class MusicPlayerTest
    {
        public static async Task TestMusicPlayback()
        {
            // 创建日志工厂
            using var loggerFactory = LoggerFactory.Create(builder => 
                builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
            
            var logger = loggerFactory.CreateLogger<MusicPlayerTest>();
            
            try
            {
                logger.LogInformation("开始测试 NLayer + PortAudioSharp2 音乐播放器");
                
                // 创建音乐播放器实例
                var musicPlayer = new ConsoleMusicAudioPlayer(loggerFactory.CreateLogger<ConsoleMusicAudioPlayer>());
                
                // 订阅事件
                musicPlayer.StateChanged += (sender, e) =>
                {
                    logger.LogInformation($"播放状态变更: {e.State} {(e.ErrorMessage != null ? $"- {e.ErrorMessage}" : "")}");
                };
                
                musicPlayer.ProgressUpdated += (sender, e) =>
                {
                    logger.LogDebug($"播放进度: {e.Position:mm\\:ss} / {e.Duration:mm\\:ss}");
                };
                
                // 测试文件路径 - 尝试多个可能的位置
                var possiblePaths = new[]
                {
                    @"C:\Users\gil\AppData\Local\Temp\VerdureMusicCache\1245657.mp3", // 刚才播放的文件
                    @"C:\path\to\your\test.mp3",
                    @"D:\music\test.mp3",
                    @"C:\Windows\Media\Alarm01.wav", // Windows 系统音频文件（如果存在）
                };

                string? testMp3File = null;
                foreach (var path in possiblePaths)
                {
                    if (File.Exists(path))
                    {
                        testMp3File = path;
                        break;
                    }
                }

                if (testMp3File != null)
                {
                    logger.LogInformation($"找到测试音频文件: {testMp3File}");
                    logger.LogInformation($"加载音频文件: {testMp3File}");
                    await musicPlayer.LoadAsync(testMp3File);
                    
                    logger.LogInformation($"开始播放，时长: {musicPlayer.Duration}");
                    await musicPlayer.PlayAsync();
                    
                    // 播放 10 秒
                    await Task.Delay(10000);
                    
                    logger.LogInformation("暂停播放");
                    await musicPlayer.PauseAsync();
                    
                    await Task.Delay(2000);
                    
                    logger.LogInformation("继续播放");
                    await musicPlayer.PlayAsync();
                    
                    await Task.Delay(5000);
                    
                    logger.LogInformation("停止播放");
                    await musicPlayer.StopAsync();
                }
                else
                {
                    logger.LogWarning("未找到可用的测试音频文件");
                    logger.LogInformation("请将 MP3 文件放在以下位置之一:");
                    foreach (var path in possiblePaths)
                    {
                        logger.LogInformation($"  - {path}");
                    }
                }
                
                // 清理资源
                musicPlayer.Dispose();
                
                logger.LogInformation("音乐播放器测试完成");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "测试过程中发生错误");
            }
        }
    }
}
