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
    /// 酷我音乐播放服务实现 - 兼容py-xiaozhi的酷我API
    /// 基于py-xiaozhi的音乐播放器实现，提供音乐搜索、播放、缓存等功能
    /// </summary>
    public class KugouMusicService : IMusicPlayerService, IDisposable
    {
        private readonly ILogger<KugouMusicService> _logger;
        private readonly HttpClient _httpClient;
        private readonly IMusicAudioPlayer _audioPlayer;
        private readonly Timer _progressTimer;
        private readonly SemaphoreSlim _operationSemaphore;        // 播放状态
        private MusicTrack? _currentTrack;
        private List<LyricLine> _currentLyrics = new();
        private int _currentLyricIndex = -1;

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
            _httpClient = new HttpClient();
            _audioPlayer = audioPlayer;
            _operationSemaphore = new SemaphoreSlim(1, 1);

            _cacheDirectory = cacheDirectory ?? Path.Combine(Path.GetTempPath(), "VerdureMusicCache");
            Directory.CreateDirectory(_cacheDirectory);

            _config = CreateDefaultConfig();
            ConfigureHttpClient();

            _audioPlayer.StateChanged += OnAudioPlayerStateChanged;
            _audioPlayer.ProgressUpdated += OnAudioPlayerProgressUpdated;

            _progressTimer = new Timer(UpdateProgress, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        public async Task<PlaybackResult> SearchAndPlayAsync(string songName, CancellationToken cancellationToken = default)
        {
            await _operationSemaphore.WaitAsync(cancellationToken);
            try
            {
                _logger.LogInformation("搜索并播放歌曲: {SongName}", songName);
                
                // 搜索歌曲
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

                // 构建搜索参数 - 兼容酷我API
                var searchParams = new Dictionary<string, string>(_config.SearchParams)
                {
                    ["all"] = songName
                };

                // 构建完整的搜索URL
                var queryString = string.Join("&", searchParams.Select(kvp => $"{kvp.Key}={Uri.EscapeDataString(kvp.Value)}"));
                var searchUrl = $"{_config.SearchUrl}?{queryString}";

                _logger.LogInformation("搜索URL: {SearchUrl}", searchUrl);

                var response = await _httpClient.GetStringAsync(searchUrl, cancellationToken);
                
                _logger.LogDebug("搜索API响应内容: {Response}", response.Substring(0, Math.Min(200, response.Length)));

                // 处理响应文本 - 酷我API可能返回不标准的JSON
                var responseText = response.Replace("'", "\"");

                // 提取歌曲信息 - 按照py-xiaozhi的解析方式
                var songId = ExtractFieldFromResponse(responseText, "DC_TARGETID");
                if (string.IsNullOrEmpty(songId))
                {
                    return SearchResult.CreateError("未找到匹配的歌曲");
                }

                var songName_extracted = ExtractFieldFromResponse(responseText, "NAME") ?? songName;
                var artist = ExtractFieldFromResponse(responseText, "ARTIST") ?? "";
                var album = ExtractFieldFromResponse(responseText, "ALBUM") ?? "";
                var durationStr = ExtractFieldFromResponse(responseText, "DURATION") ?? "0";
                
                if (!int.TryParse(durationStr, out var duration))
                {
                    duration = 0;
                }

                var track = new MusicTrack
                {
                    Id = songId,
                    Name = songName_extracted,
                    Artist = artist,
                    Album = album,
                    Duration = duration
                };

                _logger.LogInformation("找到歌曲: {Track}", $"{track.Name} - {track.Artist}");
                return SearchResult.CreateSuccess("搜索成功", track);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "搜索歌曲失败: {SongName}", songName);
                return SearchResult.CreateError(ex.Message, ex.ToString());
            }
        }

        private string? ExtractFieldFromResponse(string responseText, string fieldName)
        {
            var searchPattern = $"\"{fieldName}\":\"";
            var startPos = responseText.IndexOf(searchPattern);
            if (startPos == -1) return null;

            startPos += searchPattern.Length;
            var endPos = responseText.IndexOf("\"", startPos);
            if (endPos == -1) return null;

            return responseText.Substring(startPos, endPos - startPos);
        }

        public async Task<PlaybackResult> PlayTrackAsync(MusicTrack track, CancellationToken cancellationToken = default)
        {
            //await _operationSemaphore.WaitAsync(cancellationToken);
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
                
                if (!string.IsNullOrEmpty(cachedFile))
                {
                    await _audioPlayer.LoadAsync(cachedFile);
                }
                else
                {
                    await _audioPlayer.LoadFromUrlAsync(playUrl);
                }

                await _audioPlayer.PlayAsync();
                _currentTrack = track;
                
                OnPlaybackStateChanged("Playing", null);
                return new PlaybackResult { Success = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "播放歌曲失败: {TrackName}", track.Name);
                OnPlaybackStateChanged("Failed", ex.Message);
                return new PlaybackResult
                {
                    Success = false,
                    Message = $"播放失败: {ex.Message}"
                };
            }
            finally
            {
                //_operationSemaphore.Release();
            }
        }

        public async Task<PlaybackResult> TogglePlayPauseAsync(CancellationToken cancellationToken = default)
        {
            if (IsPlaying)
            {
                await PauseAsync(cancellationToken);
            }
            else if (IsPaused)
            {
                await ResumeAsync(cancellationToken);
            }
            return new PlaybackResult { Success = true };
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
                OnPlaybackStateChanged("Stopped", null);
                return new PlaybackResult { Success = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止播放失败");
                return PlaybackResult.CreateError("stop", ex.Message);
            }
        }
        
        public async Task<PlaybackResult> SeekAsync(double position, CancellationToken cancellationToken = default)
        {
            try
            {
                await _audioPlayer.SeekAsync(TimeSpan.FromSeconds(position));
                return new PlaybackResult { Success = true };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "跳转失败");
                return PlaybackResult.CreateError("seek", ex.Message);
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
            }
        }

        public Task<PlaybackResult> SetVolumeAsync(double volume)
        {
            return SetVolumeAsync(volume, CancellationToken.None);
        }

        public Task<PlaybackResult> SetVolumeAsync(double volume, CancellationToken cancellationToken = default)
        {
            try
            {
                // TODO: 实现音量控制
                return Task.FromResult(new PlaybackResult { Success = true });
            }
            catch (Exception ex)
            {
                return Task.FromResult(PlaybackResult.CreateError("volume", ex.Message));
            }
        }

        #region 私有方法

        private KugouMusicConfig CreateDefaultConfig()
        {
            return new KugouMusicConfig
            {
                SearchUrl = "http://search.kuwo.cn/r.s",
                PlayUrl = "http://api.xiaodaokg.com/kuwo.php",
                LyricUrl = "http://m.kuwo.cn/newh5/singles/songinfoandlrc",
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        private void ConfigureHttpClient()
        {
            _httpClient.DefaultRequestHeaders.Clear();
            
            // 添加所有必要的请求头
            foreach (var header in _config.Headers)
            {
                if (header.Key.Equals("User-Agent", StringComparison.OrdinalIgnoreCase))
                {
                    _httpClient.DefaultRequestHeaders.Add("User-Agent", header.Value);
                }
                else if (!header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    _httpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
                }
            }
            
            _httpClient.Timeout = _config.Timeout;
        }

        private async Task<string?> GetPlayUrlAsync(string songId, CancellationToken cancellationToken)
        {
            // 检查缓存
            if (_urlCache.TryGetValue(songId, out var cachedUrl))
            {
                return cachedUrl;
            }

            try
            {
                var playApiUrl = $"{_config.PlayUrl}?ID={songId}";
                _logger.LogInformation("获取播放URL: {PlayApiUrl}", playApiUrl);
                
                var response = await _httpClient.GetStringAsync(playApiUrl, cancellationToken);
                var playUrl = response.Trim();

                // 检查URL是否有效
                if (string.IsNullOrEmpty(playUrl) || !playUrl.StartsWith("http"))
                {
                    _logger.LogWarning("返回的播放链接格式不正确: {PlayUrl}", playUrl);
                    return null;
                }

                // 缓存URL
                _urlCache[songId] = playUrl;
                _logger.LogInformation("获取到有效播放URL: {PlayUrl}", playUrl.Substring(0, Math.Min(60, playUrl.Length)));
                return playUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取播放URL失败: {SongId}", songId);
                return null;
            }
        }

        private async Task<List<LyricLine>?> GetLyricsAsync(string songId, CancellationToken cancellationToken)
        {
            try
            {
                var lyricsUrl = $"{_config.LyricUrl}?musicId={songId}";
                _logger.LogInformation("获取歌词URL: {LyricsUrl}", lyricsUrl);
                
                var response = await _httpClient.GetStringAsync(lyricsUrl, cancellationToken);
                
                var jsonDoc = JsonDocument.Parse(response);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("status", out var statusElement) && statusElement.GetInt32() == 200 &&
                    root.TryGetProperty("data", out var dataElement) &&
                    dataElement.TryGetProperty("lrclist", out var lrcListElement))
                {
                    var lyrics = new List<LyricLine>();
                    
                    foreach (var lrcItem in lrcListElement.EnumerateArray())
                    {
                        if (lrcItem.TryGetProperty("time", out var timeElement) &&
                            lrcItem.TryGetProperty("lineLyric", out var textElement))
                        {
                            var timeSec = timeElement.GetDouble();
                            var text = textElement.GetString()?.Trim();
                              // 跳过空歌词和元信息歌词
                            if (!string.IsNullOrEmpty(text) && 
                                !text.StartsWith("作词") && 
                                !text.StartsWith("作曲") && 
                                !text.StartsWith("编曲"))
                            {
                                lyrics.Add(new LyricLine
                                {
                                    Time = timeSec,
                                    Text = text
                                });
                            }
                        }
                    }
                    
                    _logger.LogInformation("成功获取歌词，共 {Count} 行", lyrics.Count);
                    return lyrics;
                }

                _logger.LogWarning("未获取到歌词或歌词格式错误");
                return new List<LyricLine>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取歌词失败: {SongId}", songId);
                return new List<LyricLine>();
            }
        }

        private async Task<string?> GetCachedFileAsync(string songId, string playUrl, CancellationToken cancellationToken)
        {
            try
            {
                var cacheFileName = $"{songId}.mp3";
                var filePath = Path.Combine(_cacheDirectory, cacheFileName);
                
                if (File.Exists(filePath))
                {
                    _logger.LogInformation("使用缓存文件: {FilePath}", filePath);
                    return filePath;
                }

                // 下载到缓存
                _logger.LogInformation("下载音乐文件: {SongId}", songId);
                using var response = await _httpClient.GetAsync(playUrl, cancellationToken);
                response.EnsureSuccessStatusCode();

                var tempFilePath = filePath + ".tmp";
                using (var fileStream = File.Create(tempFilePath))
                {
                    await response.Content.CopyToAsync(fileStream, cancellationToken);
                }

                // 原子性写入
                File.Move(tempFilePath, filePath);
                _logger.LogInformation("音乐文件已缓存: {FilePath}", filePath);
                return filePath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "下载音乐文件失败: {SongId}", songId);
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
}
