using System;
using System.Threading.Tasks;
using System.Threading;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Services;
using Verdure.Assistant.Core.Models;
using Verdure.Assistant.Api.Audio;

namespace Verdure.Assistant.Api.Services
{
    /// <summary>
    /// API音乐服务实现 - 移植自Console项目
    /// 使用mpg123作为音频播放后端，提供更好的稳定性和跨平台兼容性
    /// </summary>
    public class ApiMusicService : IMusicPlayerService, IDisposable
    {
        private readonly ILogger<ApiMusicService> _logger;
        private readonly KugouMusicService _kugouMusicService;
        private readonly Mpg123AudioPlayer _mpg123AudioPlayer;
        private bool _disposed;

        public event EventHandler<MusicPlaybackEventArgs>? PlaybackStateChanged
        {
            add => _kugouMusicService.PlaybackStateChanged += value;
            remove => _kugouMusicService.PlaybackStateChanged -= value;
        }

        public event EventHandler<LyricUpdateEventArgs>? LyricUpdated
        {
            add => _kugouMusicService.LyricUpdated += value;
            remove => _kugouMusicService.LyricUpdated -= value;
        }

        public event EventHandler<ProgressUpdateEventArgs>? ProgressUpdated
        {
            add => _kugouMusicService.ProgressUpdated += value;
            remove => _kugouMusicService.ProgressUpdated -= value;
        }

        public bool IsPlaying => _kugouMusicService.IsPlaying;
        public bool IsPaused => _kugouMusicService.IsPaused;
        public MusicTrack? CurrentTrack => _kugouMusicService.CurrentTrack;
        public double CurrentPosition => _kugouMusicService.CurrentPosition;

        public ApiMusicService(ILogger<ApiMusicService> logger, ILoggerFactory loggerFactory)
        {
            _logger = logger;
            
            // 创建mpg123音频播放器
            _mpg123AudioPlayer = new Mpg123AudioPlayer(loggerFactory.CreateLogger<Mpg123AudioPlayer>());
            
            // 创建酷狗音乐服务，使用mpg123作为音频播放器
            var cacheDirectory = GetMusicCacheDirectory();
            _kugouMusicService = new KugouMusicService(
                loggerFactory.CreateLogger<KugouMusicService>(),
                _mpg123AudioPlayer,
                cacheDirectory
            );
            
            _logger.LogInformation("API音乐服务初始化完成，音乐缓存目录: {CacheDirectory}", cacheDirectory);
            Console.WriteLine($"[音乐缓存] API音乐服务缓存目录: {cacheDirectory}");
        }

        private string GetMusicCacheDirectory()
        {
            // 使用与Console项目相同的缓存目录结构
            var cacheDirectory = Path.Combine(Path.GetTempPath(), "VerdureMusicCache");
            Directory.CreateDirectory(cacheDirectory);
            return cacheDirectory;
        }

        public async Task<SearchResult> SearchSongAsync(string songName)
        {
            try
            {
                _logger.LogInformation("搜索歌曲: {SongName}", songName);
                return await _kugouMusicService.SearchSongAsync(songName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "搜索歌曲失败: {SongName}", songName);
                throw;
            }
        }

        public async Task<PlaybackResult> PlayTrackAsync(MusicTrack track, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("播放音乐: {TrackName} - {Artist}", track.Name, track.Artist);
                Console.WriteLine($"[音乐缓存] 播放音乐: {track.Name} - {track.Artist}");
                return await _kugouMusicService.PlayTrackAsync(track, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "播放音乐失败: {TrackName}", track.Name);
                throw;
            }
        }

        public async Task<PlaybackResult> SearchAndPlayAsync(string songName)
        {
            try
            {
                _logger.LogInformation("搜索并播放歌曲: {SongName}", songName);
                Console.WriteLine($"[音乐缓存] 搜索并播放歌曲: {songName}");
                return await _kugouMusicService.SearchAndPlayAsync(songName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "搜索并播放歌曲失败: {SongName}", songName);
                throw;
            }
        }

        public async Task<PlaybackResult> TogglePlayPauseAsync()
        {
            try
            {
                _logger.LogInformation("切换播放/暂停状态");
                return await _kugouMusicService.TogglePlayPauseAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "切换播放/暂停状态失败");
                throw;
            }
        }

        public async Task<PlaybackResult> StopAsync()
        {
            try
            {
                _logger.LogInformation("停止播放");
                return await _kugouMusicService.StopAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止播放失败");
                throw;
            }
        }

        public async Task<PlaybackResult> SeekAsync(double position)
        {
            try
            {
                _logger.LogInformation("跳转到位置: {Position}秒", position);
                return await _kugouMusicService.SeekAsync(position);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "跳转失败");
                throw;
            }
        }

        public async Task<string> GetLyricsAsync()
        {
            try
            {
                return await _kugouMusicService.GetLyricsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取歌词失败");
                throw;
            }
        }

        public async Task ClearCacheAsync()
        {
            try
            {
                _logger.LogInformation("清理音乐缓存");
                Console.WriteLine("[音乐缓存] 开始清理音乐缓存");
                await _kugouMusicService.ClearCacheAsync();
                Console.WriteLine("[音乐缓存] 音乐缓存清理完成");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理音乐缓存失败");
                throw;
            }
        }

        public async Task<PlaybackResult> SetVolumeAsync(double volume)
        {
            try
            {
                _logger.LogInformation("设置音量: {Volume}%", volume);
                return await _kugouMusicService.SetVolumeAsync(volume);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设置音量失败");
                throw;
            }
        }

        // 非接口方法，用于扩展功能
        public async Task PauseAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("暂停播放");
                await _kugouMusicService.PauseAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "暂停播放失败");
                throw;
            }
        }

        public async Task ResumeAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("恢复播放");
                await _kugouMusicService.ResumeAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "恢复播放失败");
                throw;
            }
        }

        // 额外的属性用于API Controller
        public IReadOnlyList<LyricLine> CurrentLyrics => _kugouMusicService.CurrentLyrics;
        public TimeSpan TotalDuration => _kugouMusicService.TotalDuration;
        public double Progress => _kugouMusicService.Progress;

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            
            try
            {
                _kugouMusicService?.Dispose();
                _mpg123AudioPlayer?.Dispose();
                _logger.LogInformation("API音乐服务已释放");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "释放API音乐服务时发生异常");
            }
        }
    }
}
