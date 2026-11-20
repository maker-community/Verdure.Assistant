using Verdure.Assistant.Bumi;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Services;
using Verdure.Assistant.Core.Services.MCP;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();

builder.Services.AddSingleton<IConfigurationService, ConfigurationService>();
builder.Services.AddSingleton<IKeywordSpottingService, KeywordSpottingService>();
builder.Services.AddSingleton<McpServer>();

// 音频设备就是这台机器人的麦克风和喇叭，不是运行本程序的电脑的声卡。
builder.Services.AddBumiAudio(options => builder.Configuration.GetSection("Bumi").Bind(options));

builder.Services.AddSingleton<IVoiceChatService, VoiceChatService>();
builder.Services.AddHostedService<BumiVoiceChatBootstrapService>();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    Service = "Verdure.Assistant.Bumi",
    Description = "机器人语音对话接口",
    Endpoints = new[]
    {
        "GET  /api/voicechat/status",
        "POST /api/voicechat/initialize",
        "POST /api/voicechat/start",
        "POST /api/voicechat/stop",
        "POST /api/voicechat/toggle",
        "POST /api/voicechat/send-text",
        "POST /api/voicechat/interrupt",
        "POST /api/voicechat/auto-mode",
        "POST /api/voicechat/disconnect"
    }
}));

app.MapControllers();

app.Run();
