using System;
using System.Threading.Tasks;
using Verdure.Assistant.Tests;

namespace WebSocketAudioFlowTestConsole
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=== WebSocket音频流程测试 ===");
            Console.WriteLine("测试目标：验证 WebSocket音频数据 → OpusSharp解码 → SoundFlow播放");
            Console.WriteLine();

            var test = new WebSocketAudioFlowTest();
            
            try
            {
                await test.TestCompleteAudioFlow();
                Console.WriteLine("\n✅ 测试成功完成");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n❌ 测试失败: {ex.Message}");
                Console.WriteLine($"详细信息: {ex.StackTrace}");
            }
            finally
            {
                test.Dispose();
            }

            Console.WriteLine("\n按任意键退出...");
            Console.ReadKey();
        }
    }
}
