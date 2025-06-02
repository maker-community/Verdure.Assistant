using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Models;

namespace Verdure.Assistant.Core.Services;

/// <summary>
/// 音乐播放器IoT设备 - 对应py-xiaozhi的MusicPlayer(Thing)
/// 通过IoT设备模式封装音乐播放功能，同时保留现有的KugouMusicService
/// </summary>
public class MusicPlayerIoTDevice : IoTDevice
{
    private readonly IMusicPlayerService _musicService;
    private readonly ILogger<MusicPlayerIoTDevice> _deviceLogger;
    
    public MusicPlayerIoTDevice(IMusicPlayerService musicService, ILogger<MusicPlayerIoTDevice> logger) 
        : base(logger)
    {
        _musicService = musicService;
        _deviceLogger = logger;
        
        Name = "MusicPlayer";
        Description = "智能音乐播放器";
        Type = "media_player";
        
        // 订阅音乐服务事件来更新IoT设备状态
        _musicService.PlaybackStateChanged += OnPlaybackStateChanged;
        _musicService.LyricUpdated += OnLyricUpdated;
        _musicService.ProgressUpdated += OnProgressUpdated;
    }
    
    protected override void RegisterProperties()
    {
        // 播放状态属性
        AddProperty("playing", "是否正在播放", IoTValueType.Boolean, true, false, false);
        AddProperty("paused", "是否暂停", IoTValueType.Boolean, true, false, false);
        AddProperty("current_track", "当前播放曲目", IoTValueType.String, true, false, "");
        AddProperty("current_artist", "当前艺术家", IoTValueType.String, true, false, "");
        AddProperty("current_lyric", "当前歌词", IoTValueType.String, true, false, "");
        AddProperty("position", "播放位置(秒)", IoTValueType.Number, true, false, 0.0);
        AddProperty("duration", "总时长(秒)", IoTValueType.Number, true, false, 0.0);
        AddProperty("progress", "播放进度(%)", IoTValueType.Number, true, false, 0.0);
        AddProperty("volume", "音量", IoTValueType.Number, true, true, 1.0);
    }
    
    protected override void RegisterMethods()
    {
        // 搜索并播放 - 对应py-xiaozhi的SearchPlay方法
        AddMethod("SearchPlay", "搜索并播放歌曲", 
            new List<IoTParameter>
            {
                new("song_name", "歌曲名称", IoTValueType.String, true)
            },
            HandleSearchPlay);
        
        // 播放/暂停切换
        AddMethod("PlayPause", "播放/暂停切换", 
            new List<IoTParameter>(),
            HandlePlayPause);
        
        // 停止播放
        AddMethod("Stop", "停止播放", 
            new List<IoTParameter>(),
            HandleStop);
        
        // 跳转到指定位置
        AddMethod("Seek", "跳转到指定位置", 
            new List<IoTParameter>
            {
                new("position", "位置(秒)", IoTValueType.Number, true)
            },
            HandleSeek);
        
        // 设置音量
        AddMethod("SetVolume", "设置音量", 
            new List<IoTParameter>
            {
                new("volume", "音量(0-1)", IoTValueType.Number, true)
            },
            HandleSetVolume);
        
        // 获取歌词
        AddMethod("GetLyrics", "获取当前歌曲歌词", 
            new List<IoTParameter>(),
            HandleGetLyrics);
        
        // 搜索歌曲（不播放）
        AddMethod("SearchSong", "搜索歌曲", 
            new List<IoTParameter>
            {
                new("song_name", "歌曲名称", IoTValueType.String, true)
            },
            HandleSearchSong);
    }
    
    #region IoT方法处理器
    
    private async Task<object?> HandleSearchPlay(Dictionary<string, IoTParameter> parameters)
    {
        try
        {
            var songName = parameters["song_name"].GetValue<string>();
            if (string.IsNullOrEmpty(songName))
            {
                return new { status = "error", message = "歌曲名称不能为空" };
            }
            
            _deviceLogger.LogInformation("IoT设备搜索播放: {SongName}", songName);
            
            var result = await _musicService.SearchAndPlayAsync(songName);
            
            if (result.Success)
            {
                UpdatePlaybackProperties();
                return new 
                { 
                    status = "success", 
                    message = result.Message,
                    track = result.Track?.Name,
                    artist = result.Track?.Artist
                };
            }
            else
            {
                return new { status = "error", message = result.Message };
            }
        }
        catch (Exception ex)
        {
            _deviceLogger.LogError(ex, "IoT设备搜索播放失败");
            return new { status = "error", message = $"搜索播放失败: {ex.Message}" };
        }
    }
    
    private async Task<object?> HandlePlayPause(Dictionary<string, IoTParameter> parameters)
    {
        try
        {
            var result = await _musicService.TogglePlayPauseAsync();
            UpdatePlaybackProperties();
            
            return new 
            { 
                status = result.Success ? "success" : "error", 
                message = result.Message,
                playing = _musicService.IsPlaying,
                paused = _musicService.IsPaused
            };
        }
        catch (Exception ex)
        {
            _deviceLogger.LogError(ex, "IoT设备播放/暂停失败");
            return new { status = "error", message = $"播放/暂停失败: {ex.Message}" };
        }
    }
    
    private async Task<object?> HandleStop(Dictionary<string, IoTParameter> parameters)
    {
        try
        {
            var result = await _musicService.StopAsync();
            UpdatePlaybackProperties();
            
            return new 
            { 
                status = result.Success ? "success" : "error", 
                message = result.Message 
            };
        }
        catch (Exception ex)
        {
            _deviceLogger.LogError(ex, "IoT设备停止播放失败");
            return new { status = "error", message = $"停止播放失败: {ex.Message}" };
        }
    }
    
    private async Task<object?> HandleSeek(Dictionary<string, IoTParameter> parameters)
    {
        try
        {
            var position = parameters["position"].GetValue<double>();
            var result = await _musicService.SeekAsync(position);
            UpdatePlaybackProperties();
            
            return new 
            { 
                status = result.Success ? "success" : "error", 
                message = result.Message,
                position = position
            };
        }
        catch (Exception ex)
        {
            _deviceLogger.LogError(ex, "IoT设备跳转失败");
            return new { status = "error", message = $"跳转失败: {ex.Message}" };
        }
    }
    
    private async Task<object?> HandleSetVolume(Dictionary<string, IoTParameter> parameters)
    {
        try
        {
            var volume = parameters["volume"].GetValue<double>();
            
            // 这里可以实现音量设置，目前KugouMusicService可能还没有音量控制
            // 可以通过底层音频播放器设置音量
            SetPropertyValue("volume", volume);
            
            return new 
            { 
                status = "success", 
                message = $"音量已设置为 {volume:P0}",
                volume = volume
            };
        }
        catch (Exception ex)
        {
            _deviceLogger.LogError(ex, "IoT设备设置音量失败");
            return new { status = "error", message = $"设置音量失败: {ex.Message}" };
        }
    }
      private async Task<object?> HandleGetLyrics(Dictionary<string, IoTParameter> parameters)
    {
        try
        {
            var lyrics = await _musicService.GetLyricsAsync();
            
            return new 
            { 
                status = "success", 
                message = "获取歌词成功",
                lyrics = lyrics,
                current_lyric = GetPropertyValue<string>("current_lyric")
            };
        }
        catch (Exception ex)
        {
            _deviceLogger.LogError(ex, "IoT设备获取歌词失败");
            return new { status = "error", message = $"获取歌词失败: {ex.Message}" };
        }
    }
    
    private async Task<object?> HandleSearchSong(Dictionary<string, IoTParameter> parameters)
    {
        try
        {
            var songName = parameters["song_name"].GetValue<string>();
            if (string.IsNullOrEmpty(songName))
            {
                return new { status = "error", message = "歌曲名称不能为空" };
            }
            
            var result = await _musicService.SearchSongAsync(songName);
            
            if (result.Success && result.Track != null)
            {
                return new 
                { 
                    status = "success", 
                    message = "搜索成功",
                    track = new
                    {
                        name = result.Track.Name,
                        artist = result.Track.Artist,
                        duration = result.Track.Duration,
                        album = result.Track.Album
                    }
                };
            }
            else
            {
                return new { status = "error", message = result.Message };
            }
        }
        catch (Exception ex)
        {
            _deviceLogger.LogError(ex, "IoT设备搜索歌曲失败");
            return new { status = "error", message = $"搜索歌曲失败: {ex.Message}" };
        }
    }
    
    #endregion
    
    #region 音乐服务事件处理
    
    private void OnPlaybackStateChanged(object? sender, MusicPlaybackEventArgs e)
    {
        try
        {
            UpdatePlaybackProperties();
            _deviceLogger.LogDebug("IoT设备状态更新: {Status}", e.Status);
        }
        catch (Exception ex)
        {
            _deviceLogger.LogError(ex, "更新IoT设备播放状态失败");
        }
    }
      private void OnLyricUpdated(object? sender, LyricUpdateEventArgs e)
    {
        try
        {
            SetPropertyValue("current_lyric", e.LyricText);
            _deviceLogger.LogDebug("IoT设备歌词更新: {Lyric}", e.LyricText);
        }
        catch (Exception ex)
        {
            _deviceLogger.LogError(ex, "更新IoT设备歌词失败");
        }
    }
    
    private void OnProgressUpdated(object? sender, ProgressUpdateEventArgs e)
    {
        try
        {
            SetPropertyValue("position", e.Position);
            SetPropertyValue("duration", e.Duration);
            SetPropertyValue("progress", e.Progress);
        }
        catch (Exception ex)
        {
            _deviceLogger.LogError(ex, "更新IoT设备进度失败");
        }
    }
    
    #endregion
      /// <summary>
    /// 更新播放状态属性
    /// </summary>
    private void UpdatePlaybackProperties()
    {
        SetPropertyValue("playing", _musicService.IsPlaying);
        SetPropertyValue("paused", _musicService.IsPaused);
        
        var currentTrack = _musicService.CurrentTrack;
        if (currentTrack != null)
        {
            SetPropertyValue("current_track", currentTrack.Name);
            SetPropertyValue("current_artist", currentTrack.Artist);
            SetPropertyValue("duration", currentTrack.Duration);
        }
        else
        {
            SetPropertyValue("current_track", "");
            SetPropertyValue("current_artist", "");
            SetPropertyValue("duration", 0.0);
        }
        
        SetPropertyValue("position", _musicService.CurrentPosition);
        
        // 计算播放进度百分比
        var currentPos = _musicService.CurrentPosition;
        var totalDuration = currentTrack?.Duration ?? 0.0;
        var progress = totalDuration > 0 ? (currentPos / totalDuration) * 100.0 : 0.0;
        SetPropertyValue("progress", progress);
    }
    
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // 取消订阅事件
            _musicService.PlaybackStateChanged -= OnPlaybackStateChanged;
            _musicService.LyricUpdated -= OnLyricUpdated;
            _musicService.ProgressUpdated -= OnProgressUpdated;
        }
        base.Dispose(disposing);
    }
}
