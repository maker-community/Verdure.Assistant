namespace Verdure.Assistant.Core.Models;

/// <summary>
/// 音乐曲目信息
/// </summary>
public class MusicTrack
{
    /// <summary>
    /// 歌曲ID
    /// </summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// 歌曲名称
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 艺术家
    /// </summary>
    public string Artist { get; set; } = string.Empty;

    /// <summary>
    /// 专辑名称
    /// </summary>
    public string Album { get; set; } = string.Empty;

    /// <summary>
    /// 时长（秒）
    /// </summary>
    public double Duration { get; set; }

    /// <summary>
    /// 播放URL
    /// </summary>
    public string PlayUrl { get; set; } = string.Empty;

    /// <summary>
    /// 显示名称（歌曲名 - 艺术家）
    /// </summary>
    public string DisplayName
    {
        get
        {
            var display = Name;
            if (!string.IsNullOrEmpty(Artist))
            {
                display = $"{Name} - {Artist}";
                if (!string.IsNullOrEmpty(Album))
                {
                    display += $" ({Album})";
                }
            }
            return display;
        }
    }
}

/// <summary>
/// 歌词条目
/// </summary>
public class LyricLine
{
    /// <summary>
    /// 时间点（秒）
    /// </summary>
    public double Time { get; set; }

    /// <summary>
    /// 歌词文本
    /// </summary>
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// 播放结果
/// </summary>
public class PlaybackResult
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 状态消息
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 错误详情
    /// </summary>
    public string? ErrorDetails { get; set; }

    /// <summary>
    /// 操作类型
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// 相关曲目信息
    /// </summary>
    public MusicTrack? Track { get; set; }

    /// <summary>
    /// 当前播放位置
    /// </summary>
    public double Position { get; set; }

    /// <summary>
    /// 创建成功结果
    /// </summary>
    public static PlaybackResult CreateSuccess(string action, string message, MusicTrack? track = null, double position = 0)
    {
        return new PlaybackResult
        {
            Success = true,
            Action = action,
            Message = message,
            Track = track,
            Position = position
        };
    }

    /// <summary>
    /// 创建失败结果
    /// </summary>
    public static PlaybackResult CreateError(string action, string message, string? errorDetails = null)
    {
        return new PlaybackResult
        {
            Success = false,
            Action = action,
            Message = message,
            ErrorDetails = errorDetails
        };
    }
}

/// <summary>
/// 搜索结果
/// </summary>
public class SearchResult
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// 状态消息
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// 错误详情
    /// </summary>
    public string? ErrorDetails { get; set; }

    /// <summary>
    /// 找到的曲目
    /// </summary>
    public MusicTrack? Track { get; set; }

    /// <summary>
    /// 歌词列表
    /// </summary>
    public List<LyricLine> Lyrics { get; set; } = new();

    /// <summary>
    /// 创建成功结果
    /// </summary>
    public static SearchResult CreateSuccess(string message, MusicTrack track, List<LyricLine>? lyrics = null)
    {
        return new SearchResult
        {
            Success = true,
            Message = message,
            Track = track,
            Lyrics = lyrics ?? new List<LyricLine>()
        };
    }

    /// <summary>
    /// 创建失败结果
    /// </summary>
    public static SearchResult CreateError(string message, string? errorDetails = null)
    {
        return new SearchResult
        {
            Success = false,
            Message = message,
            ErrorDetails = errorDetails
        };
    }
}

/// <summary>
/// 酷狗音乐API配置
/// </summary>
public class KugouMusicConfig
{
    /// <summary>
    /// 搜索API URL
    /// </summary>
    public string SearchUrl { get; set; } = "http://search.kuwo.cn/r.s";

    /// <summary>
    /// 播放URL API
    /// </summary>
    public string PlayUrl { get; set; } = "http://api.xiaodaokg.com/kuwo.php";

    /// <summary>
    /// 歌词API URL
    /// </summary>
    public string LyricUrl { get; set; } = "http://m.kuwo.cn/newh5/singles/songinfoandlrc";

    /// <summary>
    /// 请求头配置
    /// </summary>
    public Dictionary<string, string> Headers { get; set; } = new()
    {
        ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36",
        ["Accept"] = "*/*",
        ["Accept-Encoding"] = "identity",
        ["Connection"] = "keep-alive",
        ["Referer"] = "https://y.kuwo.cn/",
        ["Cookie"] = ""
    };
}
