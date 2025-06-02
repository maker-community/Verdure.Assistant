using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Models;

namespace Verdure.Assistant.Core.Services
{
    /// <summary>
    /// 酷狗音乐播放服务实现 - 平台无关版本
    /// 基于py-xiaozhi的音乐播放器实现，提供音乐搜索、播放、缓存等功能
    /// </summary>
    public class KugouMusicService : IMusicPlayerService, IDisposable
    {
        private readonly ILogger<KugouMusicService> _logger;
        private readonly HttpClient _httpClient;
        private readonly IMusicAudioPlayer _audioPlayer;
        private readonly Timer _progressTimer;
        private readonly SemaphoreSlim _operationSemaphore;

        // 播放状态
        private MusicTrack? _currentTrack;
        private List<LyricLine> _currentLyrics = new();
        private int _currentLyricIndex = -1;
        private bool _isPlaying;
        private bool _isPaused;
        private TimeSpan _totalDuration;
        private TimeSpan _currentPosition;
        private DateTime _playStartTime;
        private TimeSpan _pausedDuration;

        // 缓存
        private readonly string _cacheDirectory;
        private readonly Dictionary<string, string> _urlCache = new();

        // 配置
        private readonly KugouMusicConfig _config;        
        
        public event EventHandler<MusicPlaybackEventArgs>? PlaybackStateChanged;
        public event EventHandler<LyricUpdateEventArgs>? LyricUpdated;
        public event EventHandler<ProgressUpdateEventArgs>? ProgressUpdated;

        public bool IsPlaying => _audioPlayer.IsPlaying;
        public bool IsPaused => _audioPlayer.IsPaused;
        public MusicTrack? CurrentTrack => _currentTrack;
        public IReadOnlyList<LyricLine> CurrentLyrics => _currentLyrics.AsReadOnly();
        public double CurrentPosition => _audioPlayer.CurrentPosition.TotalSeconds;
        public TimeSpan TotalDuration => _audioPlayer.Duration;
        public double Progress => TotalDuration.TotalSeconds > 0 ? 
            CurrentPosition / TotalDuration.TotalSeconds * 100 : 0;

        public KugouMusicService(
            ILogger<KugouMusicService> logger,
            IMusicAudioPlayer audioPlayer,
            string? cacheDirectory = null)
        {
            _logger = logger;
            _audioPlayer = audioPlayer ?? throw new ArgumentNullException(nameof(audioPlayer));
            _operationSemaphore = new SemaphoreSlim(1, 1);

            // 初始化缓存目录
            _cacheDirectory = cacheDirectory ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                "Verdure.Assistant", 
                "MusicCache");
            Directory.CreateDirectory(_cacheDirectory);

            // 配置HTTP客户端
            _httpClient = new HttpClient();
            _config = CreateDefaultConfig();
            ConfigureHttpClient();

            // 绑定音频播放器事件
            _audioPlayer.StateChanged += OnAudioPlayerStateChanged;
            _audioPlayer.ProgressUpdated += OnAudioPlayerProgressUpdated;

            // 初始化进度更新定时器
            _progressTimer = new Timer(UpdateProgress, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));

            _logger.LogInformation("酷狗音乐播放服务初始化完成");
        }

        public async Task<PlaybackResult> SearchAndPlayAsync(string songName, CancellationToken cancellationToken = default)
        {
            await _operationSemaphore.WaitAsync(cancellationToken);
            try
            {
                _logger.LogInformation("搜索并播放歌曲: {SongName}", songName);                // 搜索歌曲
                var searchResult = await SearchSongAsync(songName, cancellationToken);
                if (!searchResult.Success || searchResult.Track == null)
                {
                    return PlaybackResult.CreateError("search", searchResult.Message, searchResult.ErrorDetails);
                }

                // 播放歌曲
                var playResult = await PlayTrackAsync(searchResult.Track, cancellationToken);
                return playResult;
            }
            finally
            {
                _operationSemaphore.Release();
            }
        }

        // Interface-compatible overloads without CancellationToken
        public async Task<PlaybackResult> SearchAndPlayAsync(string songName)
        {
            return await SearchAndPlayAsync(songName, CancellationToken.None);
        }

        public async Task<SearchResult> SearchSongAsync(string songName)
        {
            return await SearchSongAsync(songName, CancellationToken.None);
        }

        public async Task<PlaybackResult> TogglePlayPauseAsync()
        {
            return await TogglePlayPauseAsync(CancellationToken.None);
        }

        public async Task<PlaybackResult> StopAsync()
        {
            return await StopAsync(CancellationToken.None);
        }

        public async Task<PlaybackResult> SeekAsync(double position)
        {
            return await SeekAsync(position, CancellationToken.None);
        }

        public async Task<SearchResult> SearchSongAsync(string songName, CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogInformation("搜索歌曲: {SongName}", songName);

                var searchUrl = $"{_config.BaseUrl}/v1/search/song?keyword={Uri.EscapeDataString(songName)}&page=1&pagesize=10";
                var response = await _httpClient.GetStringAsync(searchUrl, cancellationToken);
                
                var jsonDoc = JsonDocument.Parse(response);
                var root = jsonDoc.RootElement;                
                
                if (!root.TryGetProperty("data", out var dataElement) ||
                    !dataElement.TryGetProperty("lists", out var listsElement))
                {
                    return SearchResult.CreateError("搜索结果格式不正确");
                }

                var tracks = new List<MusicTrack>();
                foreach (var item in listsElement.EnumerateArray())
                {
                    if (item.TryGetProperty("SongName", out var songNameProp) &&
                        item.TryGetProperty("SingerName", out var artistProp) &&
                        item.TryGetProperty("FileHash", out var hashProp))
                    {                        var track = new MusicTrack
                        {
                            Id = hashProp.GetString() ?? "",
                            Name = songNameProp.GetString() ?? "",
                            Artist = artistProp.GetString() ?? "",
                            Album = item.TryGetProperty("AlbumName", out var albumProp) ? albumProp.GetString() ?? "" : "",
                            Duration = item.TryGetProperty("Duration", out var durationProp) ? durationProp.GetInt32() : 0
                        };
                        tracks.Add(track);
                    }
                }                
                if (tracks.Count == 0)
                {
                    return SearchResult.CreateError("未找到匹配的歌曲");
                }

                return SearchResult.CreateSuccess("搜索成功", tracks.First());
            }            catch (Exception ex)
            {
                _logger.LogError(ex, "搜索歌曲失败: {SongName}", songName);
                return SearchResult.CreateError(ex.Message, ex.ToString());
            }
        }

        public async Task<PlaybackResult> PlayTrackAsync(MusicTrack track, CancellationToken cancellationToken = default)
        {
            await _operationSemaphore.WaitAsync(cancellationToken);
            try
            {
                _logger.LogInformation("播放歌曲: {TrackName} - {Artist}", track.Name, track.Artist);                
                
                // 获取播放URL
                var playUrl = await GetPlayUrlAsync(track.Id, cancellationToken);

                if (string.IsNullOrEmpty(playUrl))
                {
                    return PlaybackResult.CreateError("play", "无法获取播放链接");
                }

                // 获取歌词
                var lyrics = await GetLyricsAsync(track.Id, cancellationToken);
                _currentLyrics = lyrics ?? new List<LyricLine>();
                _currentLyricIndex = -1;                
                
                // 检查缓存
                var cachedFile = await GetCachedFileAsync(track.Id, playUrl, cancellationToken);
                
                // 加载到音频播放器
                _currentTrack = track;
                
                if (!string.IsNullOrEmpty(cachedFile))
                {
                    await _audioPlayer.LoadAsync(cachedFile);
                }
                else
                {
                    await _audioPlayer.LoadFromUrlAsync(playUrl);
                }                
                
                // 开始播放
                await _audioPlayer.PlayAsync();
                // 触发播放状态变化事件
                OnPlaybackStateChanged("Playing", null);

                return new PlaybackResult { Success = true };
            }            
            catch (Exception ex)
            {                _logger.LogError(ex, "播放歌曲失败: {TrackName}", track.Name);
                OnPlaybackStateChanged("Failed", ex.Message);
                return new PlaybackResult
                {
                    Success = false,
                    Message = ex.Message
                };
            }
            finally
            {
                _operationSemaphore.Release();
            }
        }

        public async Task<PlaybackResult> TogglePlayPauseAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (IsPlaying)
                {
                    await PauseAsync(cancellationToken);
                }
                else
                {
                    await ResumeAsync(cancellationToken);                
                }
                return new PlaybackResult { Success = true };
            }
            catch (Exception ex)
            {                _logger.LogError(ex, "切换播放/暂停失败");
                return new PlaybackResult
                {
                    Success = false,
                    Message = ex.Message
                };
            }
        }

        public async Task PauseAsync(CancellationToken cancellationToken = default)
        {
            await _audioPlayer.PauseAsync();
            OnPlaybackStateChanged("Paused", null);
        }

        public async Task ResumeAsync(CancellationToken cancellationToken = default)
        {
            await _audioPlayer.PlayAsync();
            OnPlaybackStateChanged("Playing", null);
        }

        public async Task<PlaybackResult> StopAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await _audioPlayer.StopAsync();
                _currentTrack = null;
                _currentLyrics.Clear();
                _currentLyricIndex = -1;
                OnPlaybackStateChanged("Stopped", null);
                return new PlaybackResult { Success = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止播放失败");
                return new PlaybackResult
                {
                    Success = false,
                    Message = ex.Message
                };
            }
        }        
        
        public async Task<PlaybackResult> SeekAsync(double position, CancellationToken cancellationToken = default)
        {
            try
            {
                await _audioPlayer.SeekAsync(TimeSpan.FromSeconds(position));
                var timeStr = $"{TimeSpan.FromSeconds(position):mm\\:ss}";
                return PlaybackResult.CreateSuccess("seek", $"已跳转到 {timeStr}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "跳转失败");
                return PlaybackResult.CreateError("seek", "跳转失败", ex.Message);
            }
        }

        public async Task<string> GetLyricsAsync()
        {
            await Task.CompletedTask;
            if (_currentLyrics.Count == 0)
                return string.Empty;

            var lyricsText = string.Join("\n", _currentLyrics.Select(l => $"[{l.Time:mm\\:ss\\.ff}] {l.Text}"));
            return lyricsText;
        }

        public async Task ClearCacheAsync()
        {
            try
            {
                if (Directory.Exists(_cacheDirectory))
                {
                    var files = Directory.GetFiles(_cacheDirectory);
                    foreach (var file in files)
                    {
                        File.Delete(file);
                    }
                    _logger.LogInformation("音乐缓存已清理");
                }
                _urlCache.Clear();
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理缓存失败");
                throw;
            }        }

        public Task<PlaybackResult> SetVolumeAsync(double volume)
        {
            return SetVolumeAsync(volume, CancellationToken.None);
        }

        public Task<PlaybackResult> SetVolumeAsync(double volume, CancellationToken cancellationToken = default)
        {
            try
            {
                _audioPlayer.Volume = Math.Max(0, Math.Min(100, volume));
                return Task.FromResult(PlaybackResult.CreateSuccess("set_volume", $"音量已设置为 {volume:F0}%"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设置音量失败");
                return Task.FromResult(PlaybackResult.CreateError("set_volume", "设置音量失败", ex.Message));
            }
        }

        #region 私有方法

        private KugouMusicConfig CreateDefaultConfig()
        {
            return new KugouMusicConfig
            {
                BaseUrl = "http://mobilecdnbj.kugou.com",
                UserAgent = "KuGou2012-8275-ExpandSearchResult",
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        private void ConfigureHttpClient()
        {
            _httpClient.DefaultRequestHeaders.Add("User-Agent", _config.UserAgent);
            _httpClient.Timeout = _config.Timeout;
        }

        private async Task<string?> GetPlayUrlAsync(string hash, CancellationToken cancellationToken)
        {
            // 检查缓存
            if (_urlCache.TryGetValue(hash, out var cachedUrl))
            {
                return cachedUrl;
            }

            try
            {
                var urlApiUrl = $"{_config.BaseUrl}/v1/audio/urlv2?hash={hash}&cmd=playInfo";
                var response = await _httpClient.GetStringAsync(urlApiUrl, cancellationToken);
                
                var jsonDoc = JsonDocument.Parse(response);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("data", out var dataElement) &&
                    dataElement.TryGetProperty("url", out var urlElement))
                {
                    var playUrl = urlElement.GetString();
                    if (!string.IsNullOrEmpty(playUrl))
                    {
                        // 缓存URL
                        _urlCache[hash] = playUrl;
                        return playUrl;
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取播放URL失败: {Hash}", hash);
                return null;
            }
        }

        private async Task<List<LyricLine>?> GetLyricsAsync(string hash, CancellationToken cancellationToken)
        {
            try
            {
                var lyricsUrl = $"{_config.BaseUrl}/v1/audio/lrc?hash={hash}&cmd=100&timelength=999999";
                var response = await _httpClient.GetStringAsync(lyricsUrl, cancellationToken);
                
                var jsonDoc = JsonDocument.Parse(response);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("data", out var dataElement) &&
                    dataElement.TryGetProperty("lrc", out var lrcElement))
                {
                    var lrcContent = lrcElement.GetString();
                    if (!string.IsNullOrEmpty(lrcContent))
                    {
                        return ParseLyrics(lrcContent);
                    }
                }

                return new List<LyricLine>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取歌词失败: {Hash}", hash);
                return new List<LyricLine>();
            }
        }

        private List<LyricLine> ParseLyrics(string lrcContent)
        {
            var lyrics = new List<LyricLine>();
            var regex = new Regex(@"\[(\d{2}):(\d{2})\.(\d{2})\](.*)");

            foreach (var line in lrcContent.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var match = regex.Match(line.Trim());
                if (match.Success)
                {
                    var minutes = int.Parse(match.Groups[1].Value);
                    var seconds = int.Parse(match.Groups[2].Value);
                    var milliseconds = int.Parse(match.Groups[3].Value) * 10;
                    var text = match.Groups[4].Value.Trim();

                    if (!string.IsNullOrEmpty(text))
                    {                        lyrics.Add(new LyricLine
                        {
                            Time = new TimeSpan(0, 0, minutes, seconds, milliseconds).TotalSeconds,
                            Text = text
                        });
                    }
                }
            }

            return lyrics.OrderBy(l => l.Time).ToList();
        }

        private async Task<string?> GetCachedFileAsync(string hash, string playUrl, CancellationToken cancellationToken)
        {
            var fileName = $"{hash}.mp3";
            var filePath = Path.Combine(_cacheDirectory, fileName);

            if (File.Exists(filePath))
            {
                _logger.LogDebug("使用缓存文件: {FilePath}", filePath);
                return filePath;
            }

            try
            {
                _logger.LogInformation("下载音乐文件: {Hash}", hash);
                var response = await _httpClient.GetByteArrayAsync(playUrl, cancellationToken);
                
                // 原子性写入
                var tempPath = filePath + ".tmp";
                await File.WriteAllBytesAsync(tempPath, response, cancellationToken);
                File.Move(tempPath, filePath);

                _logger.LogInformation("音乐文件缓存完成: {FilePath}", filePath);
                return filePath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "下载音乐文件失败: {Hash}", hash);
                return null; // 返回null，使用流播放
            }
        }

        private void OnAudioPlayerStateChanged(object? sender, MusicPlayerStateChangedEventArgs e)
        {
            var state = e.State switch
            {
                MusicPlayerState.Playing => "Playing",
                MusicPlayerState.Paused => "Paused",
                MusicPlayerState.Stopped => "Stopped",
                MusicPlayerState.Ended => "Ended",
                MusicPlayerState.Error => "Failed",
                _ => "Unknown"
            };

            OnPlaybackStateChanged(state, e.ErrorMessage);
        }        
        
        private void OnAudioPlayerProgressUpdated(object? sender, MusicPlayerProgressEventArgs e)
        {
            var args = new ProgressUpdateEventArgs
            {
                Position = e.Position.TotalSeconds,
                Duration = e.Duration.TotalSeconds,
                Progress = e.Duration.TotalSeconds > 0 ? (e.Position.TotalSeconds / e.Duration.TotalSeconds * 100) : 0
            };
            ProgressUpdated?.Invoke(this, args);
        }
        
        private void OnPlaybackStateChanged(string state, string? errorMessage)
        {
            var args = new MusicPlaybackEventArgs
            {
                Track = _currentTrack,
                Status = state,
                Action = state.ToLower(),
                Message = errorMessage ?? string.Empty
            };
            PlaybackStateChanged?.Invoke(this, args);
        }        
        
        private void UpdateProgress(object? state)
        {
            if (_currentTrack == null || !IsPlaying) return;

            try
            {
                var currentPos = CurrentPosition;
                
                // 更新歌词
                UpdateCurrentLyric(currentPos);
                
                // 触发进度更新事件
                var args = new ProgressUpdateEventArgs
                {
                    Position = currentPos,
                    Duration = TotalDuration.TotalSeconds,
                    Progress = TotalDuration.TotalSeconds > 0 ? (currentPos / TotalDuration.TotalSeconds * 100) : 0
                };
                ProgressUpdated?.Invoke(this, args);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新播放进度失败");
            }
        }        
        
        private void UpdateCurrentLyric(double currentPosition)
        {
            if (_currentLyrics.Count == 0) return;

            var newLyricIndex = -1;
            for (int i = _currentLyrics.Count - 1; i >= 0; i--)
            {
                if (_currentLyrics[i].Time <= currentPosition)
                {
                    newLyricIndex = i;
                    break;
                }
            }

            if (newLyricIndex != _currentLyricIndex && newLyricIndex >= 0)
            {
                _currentLyricIndex = newLyricIndex;
                var currentLyric = _currentLyrics[_currentLyricIndex];
                var args = new LyricUpdateEventArgs
                {
                    LyricText = currentLyric.Text,
                    Position = currentLyric.Time,
                    Duration = TotalDuration.TotalSeconds
                };
                LyricUpdated?.Invoke(this, args);
            }
        }

        #endregion

        public void Dispose()
        {
            _progressTimer?.Dispose();
            _audioPlayer?.Dispose();
            _httpClient?.Dispose();
            _operationSemaphore?.Dispose();
        }
    }    
    
    
    /// <summary>
    /// 酷狗音乐配置
    /// </summary>
    public class KugouMusicConfig
    {
        public string BaseUrl { get; set; } = "http://mobilecdnbj.kugou.com";
        public string UserAgent { get; set; } = "KuGou2012-8275-ExpandSearchResult";
        public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
    }
}
