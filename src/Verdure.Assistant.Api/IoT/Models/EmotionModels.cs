namespace Verdure.Assistant.Api.IoT.Models;

/// <summary>
/// 表情类型常量
/// </summary>
public static class EmotionTypes
{
    public const string Neutral = "neutral";
    public const string Happy = "happy";
    public const string Sad = "sad";
    public const string Angry = "angry";
    public const string Surprised = "surprised";
    public const string Confused = "confused";
    public const string Random = "random";
    
    public static readonly string[] All = { Neutral, Happy, Sad, Angry, Surprised, Confused, Random };
    
    public static bool IsValid(string? emotionType)
    {
        return !string.IsNullOrEmpty(emotionType) && 
               All.Contains(emotionType, StringComparer.OrdinalIgnoreCase);
    }
    
    public static string GetRandomEmotion()
    {
        var availableEmotions = new[] { Neutral, Happy, Sad, Angry, Surprised, Confused };
        var random = new Random();
        return availableEmotions[random.Next(availableEmotions.Length)];
    }
}

/// <summary>
/// 表情配置
/// </summary>
public class EmotionConfig
{
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string LottieFile { get; set; } = string.Empty;
    public Dictionary<int, float> ActionAngles { get; set; } = new();
    public int Duration { get; set; } = 3000; // 毫秒
}

/// <summary>
/// 播放请求
/// </summary>
public class PlayRequest
{
    public string? EmotionType { get; set; }
    public bool IncludeAction { get; set; } = true;
    public bool IncludeEmotion { get; set; } = true;
    public int Loops { get; set; } = 1;
    public int Fps { get; set; } = 30;
}

/// <summary>
/// 播放状态
/// </summary>
public enum PlaybackStatus
{
    Stopped,
    Playing,
    Paused,
    Cancelled
}

/// <summary>
/// 播放状态信息
/// </summary>
public class PlaybackState
{
    public PlaybackStatus Status { get; set; }
    public string? CurrentEmotion { get; set; }
    public DateTime StartTime { get; set; }
    public int CurrentLoop { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// 表情映射服务 - 将各种表情映射到6种基本表情
/// </summary>
public static class EmotionMappingService
{
    private static readonly Dictionary<string, string> EmotionMappings = new()
    {
        // 中性/平静类表情 -> neutral
        ["neutral"] = EmotionTypes.Neutral,
        ["relaxed"] = EmotionTypes.Neutral,
        ["sleepy"] = EmotionTypes.Neutral,

        // 积极/开心类表情 -> happy
        ["happy"] = EmotionTypes.Happy,
        ["laughing"] = EmotionTypes.Happy,
        ["funny"] = EmotionTypes.Happy,
        ["loving"] = EmotionTypes.Happy,
        ["confident"] = EmotionTypes.Happy,
        ["winking"] = EmotionTypes.Happy,
        ["cool"] = EmotionTypes.Happy,
        ["delicious"] = EmotionTypes.Happy,
        ["kissy"] = EmotionTypes.Happy,
        ["silly"] = EmotionTypes.Happy,

        // 悲伤类表情 -> sad
        ["sad"] = EmotionTypes.Sad,
        ["crying"] = EmotionTypes.Sad,

        // 愤怒类表情 -> angry
        ["angry"] = EmotionTypes.Angry,

        // 惊讶类表情 -> surprised
        ["surprised"] = EmotionTypes.Surprised,
        ["shocked"] = EmotionTypes.Surprised,

        // 思考/困惑类表情 -> confused
        ["thinking"] = EmotionTypes.Confused,
        ["confused"] = EmotionTypes.Confused,
        ["embarrassed"] = EmotionTypes.Confused,
    };

    /// <summary>
    /// 将输入的表情映射到基本表情类型
    /// </summary>
    public static string MapEmotion(string? inputEmotion)
    {
        if (string.IsNullOrWhiteSpace(inputEmotion))
            return EmotionTypes.Neutral;

        var emotion = inputEmotion.ToLowerInvariant();
        
        return EmotionMappings.TryGetValue(emotion, out var mappedEmotion) 
            ? mappedEmotion 
            : EmotionTypes.Neutral;
    }
    
    /// <summary>
    /// 获取所有支持的输入表情类型
    /// </summary>
    public static IEnumerable<string> GetSupportedEmotions()
    {
        return EmotionMappings.Keys;
    }
    
    /// <summary>
    /// 检查是否支持指定的表情
    /// </summary>
    public static bool IsSupported(string? emotion)
    {
        return !string.IsNullOrWhiteSpace(emotion) && 
               EmotionMappings.ContainsKey(emotion.ToLowerInvariant());
    }
}