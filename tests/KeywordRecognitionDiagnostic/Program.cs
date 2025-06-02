using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.CognitiveServices.Speech;
using Microsoft.CognitiveServices.Speech.Audio;

namespace KeywordRecognitionDiagnostic;

/// <summary>
/// 关键词识别诊断工具
/// 直接测试 Microsoft Cognitive Services 关键词识别功能
/// </summary>
class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("=== Microsoft认知服务关键词识别诊断 ===");
        Console.WriteLine("本工具直接测试 .table 模型文件的关键词识别功能");
        Console.WriteLine();

        var host = Host.CreateDefaultBuilder()
            .ConfigureServices(ConfigureServices)
            .Build();

        await host.StartAsync();

        var logger = host.Services.GetRequiredService<ILogger<Program>>();

        try
        {
            // 查找关键词模型文件
            var keywordModelPath = FindKeywordModel();
            if (string.IsNullOrEmpty(keywordModelPath))
            {
                Console.WriteLine("❌ 未找到关键词模型文件");
                return;
            }

            Console.WriteLine($"✓ 找到关键词模型: {Path.GetFileName(keywordModelPath)}");
            Console.WriteLine($"  文件大小: {new FileInfo(keywordModelPath).Length} 字节");
            Console.WriteLine();

            // 测试 1: 验证模型文件可以加载
            Console.WriteLine("测试 1: 验证关键词模型文件");
            if (!await TestKeywordModelLoading(keywordModelPath, logger))
            {
                return;
            }

            // 测试 2: 使用默认麦克风进行关键词识别
            Console.WriteLine("\n测试 2: 使用默认麦克风进行关键词识别");
            await TestDirectKeywordRecognition(keywordModelPath, logger);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "诊断过程中发生错误");
            Console.WriteLine($"❌ 诊断失败: {ex.Message}");
        }

        Console.WriteLine("\n按任意键退出...");
        Console.ReadKey();

        await host.StopAsync();
    }

    private static string? FindKeywordModel()
    {
        try
        {
            // 查找解决方案目录
            var currentDir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (currentDir != null && !File.Exists(Path.Combine(currentDir.FullName, "Verdure.Assistant.sln")))
            {
                currentDir = currentDir.Parent;
            }

            if (currentDir != null)
            {
                var keywordsPath = Path.Combine(currentDir.FullName, "src", "Verdure.Assistant.WinUI", "Assets", "keywords");
                if (Directory.Exists(keywordsPath))
                {
                    var tableFiles = Directory.GetFiles(keywordsPath, "*.table");
                    if (tableFiles.Length > 0)
                    {
                        // 优先使用 xiaodian 模型
                        var xiaodianModel = tableFiles.FirstOrDefault(f => f.Contains("xiaodian"));
                        return xiaodianModel ?? tableFiles[0];
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"查找关键词模型时出错: {ex.Message}");
            return null;
        }
    }

    private static async Task<bool> TestKeywordModelLoading(string modelPath, ILogger logger)
    {
        try
        {
            // 测试模型文件是否可以正常加载
            using var keywordModel = KeywordRecognitionModel.FromFile(modelPath);
            Console.WriteLine("✓ 关键词模型加载成功");

            // 创建语音配置（离线模式）
            var speechConfig = SpeechConfig.FromSubscription("dummy", "dummy");
            speechConfig.SetProperty("SPEECH-UseOfflineRecognition", "true");
            Console.WriteLine("✓ 离线语音配置创建成功");

            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "关键词模型加载测试失败");
            Console.WriteLine($"❌ 关键词模型加载失败: {ex.Message}");
            return false;
        }
    }

    private static async Task TestDirectKeywordRecognition(string modelPath, ILogger logger)
    {
        KeywordRecognizer? recognizer = null;
        KeywordRecognitionModel? keywordModel = null;

        try
        {
            // 创建语音配置
            var speechConfig = SpeechConfig.FromSubscription("dummy", "dummy");
            speechConfig.SetProperty("SPEECH-UseOfflineRecognition", "true");

            // 加载关键词模型
            keywordModel = KeywordRecognitionModel.FromFile(modelPath);

            // 创建音频配置（使用默认麦克风）
            var audioConfig = AudioConfig.FromDefaultMicrophoneInput();

            // 创建关键词识别器
            recognizer = new KeywordRecognizer(audioConfig);

            // 设置事件处理
            bool keywordDetected = false;
            bool recognitionCanceled = false;
            string detectedKeyword = "";
            string cancelReason = "";

            recognizer.Recognized += (s, e) =>
            {
                if (e.Result.Reason == ResultReason.RecognizedKeyword)
                {
                    keywordDetected = true;
                    detectedKeyword = e.Result.Text;
                    Console.WriteLine($"🎯 检测到关键词: {detectedKeyword}");
                }
            };

            recognizer.Canceled += (s, e) =>
            {
                recognitionCanceled = true;
                cancelReason = $"Reason: {e.Reason}, ErrorCode: {e.ErrorCode}, Details: {e.ErrorDetails}";
                Console.WriteLine($"⚠️ 识别被取消: {cancelReason}");
            };

            Console.WriteLine("🎤 开始关键词识别测试...");
            Console.WriteLine("请说出以下关键词之一：");
            Console.WriteLine("- 你好小天");
            Console.WriteLine("- Hey Cortana");
            Console.WriteLine("- Computer");
            Console.WriteLine();

            // 开始识别
            Console.WriteLine("启动关键词识别器...");
            await recognizer.RecognizeOnceAsync(keywordModel);
            Console.WriteLine("✓ 关键词识别器启动成功");

            // 等待30秒或直到检测到关键词
            for (int i = 30; i > 0; i--)
            {
                Console.WriteLine($"等待关键词 ({i} 秒)... {(keywordDetected ? $"已检测到: {detectedKeyword}" : "未检测到")}");
                await Task.Delay(1000);

                if (keywordDetected || recognitionCanceled)
                {
                    break;
                }
            }

            // 输出结果
            Console.WriteLine("\n=== 诊断结果 ===");
            if (keywordDetected)
            {
                Console.WriteLine($"✅ 关键词识别成功: {detectedKeyword}");
            }
            else if (recognitionCanceled)
            {
                Console.WriteLine($"❌ 识别被取消: {cancelReason}");
            }
            else
            {
                Console.WriteLine("⚠️ 未检测到关键词（可能需要更清晰的发音或检查麦克风）");
            }

            // 停止识别
            await recognizer.StopRecognitionAsync();
            await Task.Delay(100); // 给SDK时间完全停止
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "直接关键词识别测试失败");
            Console.WriteLine($"❌ 关键词识别测试失败: {ex.Message}");
        }
        finally
        {
            try
            {
                recognizer?.Dispose();
                keywordModel?.Dispose();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "清理资源时发生警告");
            }
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });
    }
}
