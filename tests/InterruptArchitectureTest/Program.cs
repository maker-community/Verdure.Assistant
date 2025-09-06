using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using InterruptArchitectureTest.Core;
using InterruptArchitectureTest.Sources;

namespace InterruptArchitectureTest;

/// <summary>
/// 打断架构测试程序主入口
/// 演示如何使用新的打断架构系统
/// </summary>
internal class Program
{
    private static IInterruptService? _interruptService;
    private static ILogger<Program>? _logger;
    private static bool _isRunning = true;

    static async Task Main(string[] args)
    {
        Console.WriteLine("=== 打断架构测试程序 ===");
        Console.WriteLine("此程序演示新的打断架构系统功能");
        Console.WriteLine();

        // 配置服务
        var host = CreateHostBuilder(args).Build();
        var serviceProvider = host.Services;

        _logger = serviceProvider.GetRequiredService<ILogger<Program>>();
        _interruptService = serviceProvider.GetRequiredService<IInterruptService>();

        try
        {
            await RunInterruptArchitectureDemo();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "程序运行出错");
            Console.WriteLine($"错误: {ex.Message}");
        }

        Console.WriteLine("\n按任意键退出...");
        Console.ReadKey();
    }

    static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                // 配置日志
                services.AddLogging(builder =>
                {
                    builder.AddConsole();
                    builder.SetMinimumLevel(LogLevel.Information);
                });

                // 注册服务
                services.AddSingleton<IInterruptService, InterruptService>();
            });

    static async Task RunInterruptArchitectureDemo()
    {
        _logger?.LogInformation("开始打断架构演示");

        // 订阅打断事件
        _interruptService!.InterruptOccurred += OnInterruptOccurred;

        // 注册各种打断源
        await RegisterInterruptSources();

        // 启动所有打断源
        await _interruptService.StartAllAsync();
        _logger?.LogInformation("所有打断源已启动");

        // 显示帮助信息
        ShowHelp();

        // 处理用户输入
        await HandleUserInput();

        // 停止所有打断源
        await _interruptService.StopAllAsync();
        _logger?.LogInformation("所有打断源已停止");
    }

    static async Task RegisterInterruptSources()
    {
        _logger?.LogInformation("注册打断源...");

        // 1. 手动打断源
        var manualSource = new ManualInterruptSource();
        _interruptService!.RegisterInterruptSource(manualSource);

        // 2. 热键打断源
        var hotkeySource = new HotkeyInterruptSource();
        _interruptService.RegisterInterruptSource(hotkeySource);

        // 3. 真实的关键字打断源（基于Microsoft认知服务）
        try
        {
            var keywordSource = new KeywordInterruptSource();
            keywordSource.AddKeyword("小点");
            keywordSource.AddKeyword("停止");
            keywordSource.AddKeyword("暂停");
            _interruptService.RegisterInterruptSource(keywordSource);
            _logger?.LogInformation("已注册真实关键字打断源");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "无法注册真实关键字打断源，使用简化版本");
            
            // 4. 简化的关键字打断源（用于演示）
            var keywordSource = new SimpleKeywordInterruptSource();
            keywordSource.AddKeyword("测试");
            keywordSource.AddKeyword("停止");
            _interruptService.RegisterInterruptSource(keywordSource);
        }

        // 5. 真实的语音活动打断源（基于SoundFlow VAD）
        var vadConfig = new RealVoiceActivityInterruptSource.VadConfiguration
        {
            EnergyThreshold = 0.001f,      // 降低阈值以便更容易触发
            ActivationTimeMs = 100f,       // 100ms确认是人声
            HangoverTimeMs = 500f,         // 500ms延迟关闭
            SpeechLowBand = 200,           // 人声频带下限
            SpeechHighBand = 4000,         // 人声频带上限
            FftSize = 1024,                // FFT大小
            DebugOutput = true             // 启用调试输出
        };
        var realVadSource = new RealVoiceActivityInterruptSource(vadConfig);
        _interruptService.RegisterInterruptSource(realVadSource);

        // 6. 简化的语音活动打断源（用于备份演示）
        var simpleVadSource = new SimpleVoiceActivityInterruptSource();
        _interruptService.RegisterInterruptSource(simpleVadSource);

        // 7. 定时器打断源（每30秒触发一次）
        var timerSource = new TimerInterruptSource(
            TimeSpan.FromSeconds(30), 
            "定时器自动触发");
        _interruptService.RegisterInterruptSource(timerSource);

        _logger?.LogInformation("已注册 {Count} 个打断源", _interruptService.GetAllInterruptSources().Count());
        _logger?.LogInformation("真实VAD配置: 阈值={Threshold}, 激活时间={ActivationTime}ms, 保持时间={HangoverTime}ms", 
            vadConfig.EnergyThreshold, vadConfig.ActivationTimeMs, vadConfig.HangoverTimeMs);

        await Task.CompletedTask;
    }

    static void ShowHelp()
    {
        Console.WriteLine("\n=== 操作指南 ===");
        Console.WriteLine("1. 按 F3 键触发热键打断");
        Console.WriteLine("2. 输入 'manual' 触发手动打断");
        Console.WriteLine("3. 输入 'pause <source>' 暂停指定打断源");
        Console.WriteLine("4. 输入 'resume <source>' 恢复指定打断源");
        Console.WriteLine("5. 输入 'status' 查看所有打断源状态");
        Console.WriteLine("6. 输入 'vad' 查看VAD详细状态");
        Console.WriteLine("7. 输入 'adjust' 调整VAD参数");
        Console.WriteLine("8. 输入 'quit' 退出程序");
        Console.WriteLine("9. 真实关键字检测会监听麦克风并识别'小点'、'停止'、'暂停'等关键词");
        Console.WriteLine("10. 简化关键字和语音活动检测会自动模拟触发");
        Console.WriteLine("11. 真实VAD会监听麦克风并检测人声");
        Console.WriteLine("12. 定时器每30秒自动触发一次");
        Console.WriteLine();
        Console.WriteLine("等待打断事件或输入命令...");
    }

    static async Task HandleUserInput()
    {
        while (_isRunning)
        {
            try
            {
                var input = Console.ReadLine()?.Trim().ToLower();
                
                if (string.IsNullOrEmpty(input))
                    continue;

                var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var command = parts[0];

                switch (command)
                {
                    case "manual":
                        await TriggerManualInterrupt();
                        break;

                    case "pause":
                        if (parts.Length > 1)
                            await PauseInterruptSource(parts[1]);
                        else
                            Console.WriteLine("用法: pause <source_name>");
                        break;

                    case "resume":
                        if (parts.Length > 1)
                            await ResumeInterruptSource(parts[1]);
                        else
                            Console.WriteLine("用法: resume <source_name>");
                        break;

                    case "status":
                        ShowInterruptSourcesStatus();
                        break;

                    case "vad":
                        ShowVadStatus();
                        break;

                    case "adjust":
                        AdjustVadParameters();
                        break;

                    case "help":
                        ShowHelp();
                        break;

                    case "quit":
                    case "exit":
                        _isRunning = false;
                        break;

                    default:
                        Console.WriteLine($"未知命令: {command}，输入 'help' 查看帮助");
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "处理用户输入时出错");
                Console.WriteLine($"错误: {ex.Message}");
            }
        }
    }

    static async Task TriggerManualInterrupt()
    {
        await _interruptService!.TriggerManualInterruptAsync("用户手动触发的打断", new { Source = "Console" });
        Console.WriteLine("已触发手动打断");
    }

    static async Task PauseInterruptSource(string sourceName)
    {
        var source = _interruptService!.GetInterruptSource(sourceName);
        if (source != null)
        {
            await _interruptService.PauseSourceAsync(sourceName);
            Console.WriteLine($"已暂停打断源: {sourceName}");
        }
        else
        {
            Console.WriteLine($"未找到打断源: {sourceName}");
            ShowAvailableSources();
        }
    }

    static async Task ResumeInterruptSource(string sourceName)
    {
        var source = _interruptService!.GetInterruptSource(sourceName);
        if (source != null)
        {
            await _interruptService.ResumeSourceAsync(sourceName);
            Console.WriteLine($"已恢复打断源: {sourceName}");
        }
        else
        {
            Console.WriteLine($"未找到打断源: {sourceName}");
            ShowAvailableSources();
        }
    }

    static void ShowInterruptSourcesStatus()
    {
        Console.WriteLine("\n=== 打断源状态 ===");
        var sources = _interruptService!.GetAllInterruptSources();
        
        foreach (var source in sources)
        {
            var status = source.IsRunning ? "运行中" : "已停止";
            var enabled = source.IsEnabled ? "启用" : "禁用";
            Console.WriteLine($"- {source.Name} ({source.InterruptType}): {status}, {enabled}");
        }
        Console.WriteLine();
    }

    static void ShowAvailableSources()
    {
        Console.WriteLine("可用的打断源:");
        var sources = _interruptService!.GetAllInterruptSources();
        foreach (var source in sources)
        {
            Console.WriteLine($"  - {source.Name}");
        }
    }

    static void OnInterruptOccurred(object? sender, InterruptEventArgs e)
    {
        var timestamp = e.Timestamp.ToString("HH:mm:ss.fff");
        var priorityStr = e.Priority > 0 ? $" [优先级: {e.Priority}]" : "";
        
        Console.WriteLine($"\n🚨 [{timestamp}] 打断事件{priorityStr}");
        Console.WriteLine($"   类型: {e.InterruptType}");
        Console.WriteLine($"   来源: {e.SourceName}");
        Console.WriteLine($"   描述: {e.Description}");
        
        if (e.Data != null)
        {
            Console.WriteLine($"   数据: {System.Text.Json.JsonSerializer.Serialize(e.Data)}");
        }
        
        Console.WriteLine();

        // 根据打断类型执行不同的处理逻辑
        HandleInterruptEvent(e);
    }

    static void HandleInterruptEvent(InterruptEventArgs e)
    {
        switch (e.InterruptType)
        {
            case InterruptTypes.Keyword:
                Console.WriteLine(">>> 处理关键字打断：可以在这里停止当前对话");
                break;

            case InterruptTypes.Hotkey:
                Console.WriteLine(">>> 处理热键打断：可以在这里切换对话状态");
                break;

            case InterruptTypes.VoiceActivity:
                Console.WriteLine(">>> 处理语音活动打断：可以在这里暂停TTS播放");
                // 如果是真实VAD，显示更多详细信息
                if (e.SourceName == "RealVAD" && e.Data != null)
                {
                    try
                    {
                        var dataJson = System.Text.Json.JsonSerializer.Serialize(e.Data, new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
                        Console.WriteLine($"    VAD详情: {dataJson}");
                    }
                    catch
                    {
                        Console.WriteLine($"    VAD数据: {e.Data}");
                    }
                }
                break;

            case InterruptTypes.Manual:
                Console.WriteLine(">>> 处理手动打断：可以在这里执行自定义逻辑");
                break;

            case InterruptTypes.Timer:
                Console.WriteLine(">>> 处理定时器打断：可以在这里执行定期任务");
                break;

            default:
                Console.WriteLine($">>> 处理自定义打断类型: {e.InterruptType}");
                break;
        }
    }

    static void ShowVadStatus()
    {
        Console.WriteLine("\n=== VAD状态详情 ===");
        
        var sources = _interruptService!.GetAllInterruptSources();
        var realVadSource = sources.FirstOrDefault(s => s.Name == "RealVAD") as RealVoiceActivityInterruptSource;
        var simpleVadSource = sources.FirstOrDefault(s => s.Name == "SimpleVAD") as SimpleVoiceActivityInterruptSource;
        
        if (realVadSource != null)
        {
            Console.WriteLine("📡 真实VAD状态:");
            var config = realVadSource.Configuration;
            var stats = realVadSource.GetStatistics();
            
            Console.WriteLine($"   状态: {(realVadSource.IsRunning ? "运行中" : "已停止")} ({(realVadSource.IsEnabled ? "启用" : "禁用")})");
            Console.WriteLine($"   配置: 阈值={config.EnergyThreshold}, 激活={config.ActivationTimeMs}ms, 保持={config.HangoverTimeMs}ms");
            Console.WriteLine($"   频带: {config.SpeechLowBand}Hz - {config.SpeechHighBand}Hz, FFT={config.FftSize}");
            Console.WriteLine($"   统计: 音频帧={stats.AudioFrameCount}, VAD触发={stats.VadTriggerCount}");
            Console.WriteLine($"   当前: VAD状态={stats.CurrentVadState}, 录音={stats.IsRecording}");
            
            var timeSinceLastAudio = DateTime.Now - stats.LastAudioFrameTime;
            var timeSinceLastVad = DateTime.Now - stats.LastVadTriggerTime;
            Console.WriteLine($"   时间: 最后音频={timeSinceLastAudio.TotalSeconds:F1}s前, 最后VAD={timeSinceLastVad.TotalSeconds:F1}s前");
        }
        else
        {
            Console.WriteLine("❌ 真实VAD源未找到");
        }
        
        if (simpleVadSource != null)
        {
            Console.WriteLine($"\n🤖 简单VAD状态: {(simpleVadSource.IsRunning ? "运行中" : "已停止")} ({(simpleVadSource.IsEnabled ? "启用" : "禁用")})");
        }
        
        Console.WriteLine();
    }

    static Task AdjustVadParameters()
    {
        var sources = _interruptService!.GetAllInterruptSources();
        var realVadSource = sources.FirstOrDefault(s => s.Name == "RealVAD") as RealVoiceActivityInterruptSource;
        
        if (realVadSource == null)
        {
            Console.WriteLine("❌ 真实VAD源未找到，无法调整参数");
            return Task.CompletedTask;
        }

        Console.WriteLine("\n=== VAD参数调整 ===");
        var config = realVadSource.Configuration;
        
        Console.WriteLine($"当前配置:");
        Console.WriteLine($"  1. 能量阈值: {config.EnergyThreshold} (推荐: 0.001-0.1)");
        Console.WriteLine($"  2. 激活时间: {config.ActivationTimeMs}ms (推荐: 50-300ms)");
        Console.WriteLine($"  3. 保持时间: {config.HangoverTimeMs}ms (推荐: 200-1000ms)");
        Console.WriteLine($"  4. 频带下限: {config.SpeechLowBand}Hz (推荐: 100-300Hz)");
        Console.WriteLine($"  5. 频带上限: {config.SpeechHighBand}Hz (推荐: 3000-8000Hz)");
        Console.WriteLine($"  6. 调试输出: {config.DebugOutput}");
        Console.WriteLine();
        
        Console.WriteLine("请选择要调整的参数 (1-6, 或按Enter跳过):");
        var choice = Console.ReadLine()?.Trim();
        
        if (string.IsNullOrEmpty(choice)) return Task.CompletedTask;
        
        try
        {
            switch (choice)
            {
                case "1":
                    Console.Write("请输入新的能量阈值 (0.0001-1.0): ");
                    if (float.TryParse(Console.ReadLine(), out float threshold) && threshold > 0 && threshold <= 1.0f)
                    {
                        realVadSource.UpdateVadConfiguration(c => c.EnergyThreshold = threshold);
                        Console.WriteLine($"✅ 能量阈值已更新为: {threshold}");
                    }
                    else
                    {
                        Console.WriteLine("❌ 无效的阈值");
                    }
                    break;
                    
                case "2":
                    Console.Write("请输入新的激活时间 (50-500ms): ");
                    if (float.TryParse(Console.ReadLine(), out float activationTime) && activationTime >= 50 && activationTime <= 500)
                    {
                        realVadSource.UpdateVadConfiguration(c => c.ActivationTimeMs = activationTime);
                        Console.WriteLine($"✅ 激活时间已更新为: {activationTime}ms");
                    }
                    else
                    {
                        Console.WriteLine("❌ 无效的激活时间");
                    }
                    break;
                    
                case "3":
                    Console.Write("请输入新的保持时间 (200-2000ms): ");
                    if (float.TryParse(Console.ReadLine(), out float hangoverTime) && hangoverTime >= 200 && hangoverTime <= 2000)
                    {
                        realVadSource.UpdateVadConfiguration(c => c.HangoverTimeMs = hangoverTime);
                        Console.WriteLine($"✅ 保持时间已更新为: {hangoverTime}ms");
                    }
                    else
                    {
                        Console.WriteLine("❌ 无效的保持时间");
                    }
                    break;
                    
                case "4":
                    Console.Write("请输入新的频带下限 (100-500Hz): ");
                    if (int.TryParse(Console.ReadLine(), out int lowBand) && lowBand >= 100 && lowBand <= 500)
                    {
                        realVadSource.UpdateVadConfiguration(c => c.SpeechLowBand = lowBand);
                        Console.WriteLine($"✅ 频带下限已更新为: {lowBand}Hz");
                    }
                    else
                    {
                        Console.WriteLine("❌ 无效的频带下限");
                    }
                    break;
                    
                case "5":
                    Console.Write("请输入新的频带上限 (3000-8000Hz): ");
                    if (int.TryParse(Console.ReadLine(), out int highBand) && highBand >= 3000 && highBand <= 8000)
                    {
                        realVadSource.UpdateVadConfiguration(c => c.SpeechHighBand = highBand);
                        Console.WriteLine($"✅ 频带上限已更新为: {highBand}Hz");
                    }
                    else
                    {
                        Console.WriteLine("❌ 无效的频带上限");
                    }
                    break;
                    
                case "6":
                    var newDebugState = !config.DebugOutput;
                    realVadSource.UpdateVadConfiguration(c => c.DebugOutput = newDebugState);
                    Console.WriteLine($"✅ 调试输出已{(newDebugState ? "启用" : "禁用")}");
                    break;
                    
                default:
                    Console.WriteLine("❌ 无效的选择");
                    break;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ 参数调整出错: {ex.Message}");
        }
        
        return Task.CompletedTask;
    }
}
