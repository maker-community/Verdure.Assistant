using Microsoft.AspNetCore.Mvc;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Models;
using Verdure.Assistant.Api.Services;

namespace Verdure.Assistant.Api.Controllers
{
    /// <summary>
    /// 音乐播放控制器
    /// 提供音乐搜索、播放、控制等API接口
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class MusicController : ControllerBase
    {
        private readonly ILogger<MusicController> _logger;
        private readonly ApiMusicService _musicService; // 使用具体类型而不是接口

        public MusicController(ILogger<MusicController> logger, IMusicPlayerService musicService)
        {
            _logger = logger;
            _musicService = (ApiMusicService)musicService; // 转换为具体类型
        }

        /// <summary>
        /// 搜索歌曲
        /// </summary>
        /// <param name="songName">歌曲名称</param>
        /// <returns>搜索结果</returns>
        [HttpGet("search")]
        public async Task<IActionResult> SearchSong([FromQuery] string songName)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(songName))
                {
                    return BadRequest("歌曲名称不能为空");
                }

                _logger.LogInformation("API搜索歌曲: {SongName}", songName);
                Console.WriteLine($"[音乐缓存] API搜索歌曲: {songName}");
                
                var result = await _musicService.SearchSongAsync(songName);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "搜索歌曲失败: {SongName}", songName);
                return StatusCode(500, $"搜索失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 搜索并播放歌曲
        /// </summary>
        /// <param name="songName">歌曲名称</param>
        /// <returns>播放结果</returns>
        [HttpPost("search-and-play")]
        public async Task<IActionResult> SearchAndPlay([FromBody] SearchAndPlayRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.SongName))
                {
                    return BadRequest("歌曲名称不能为空");
                }

                _logger.LogInformation("API搜索并播放歌曲: {SongName}", request.SongName);
                Console.WriteLine($"[音乐缓存] API搜索并播放歌曲: {request.SongName}");
                
                var result = await _musicService.SearchAndPlayAsync(request.SongName);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "搜索并播放歌曲失败: {SongName}", request.SongName);
                return StatusCode(500, $"播放失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 播放指定音乐
        /// </summary>
        /// <param name="request">播放请求</param>
        /// <returns>播放结果</returns>
        [HttpPost("play")]
        public async Task<IActionResult> PlayTrack([FromBody] PlayTrackRequest request)
        {
            try
            {
                if (request.Track == null)
                {
                    return BadRequest("音乐信息不能为空");
                }

                _logger.LogInformation("API播放音乐: {TrackName} - {Artist}", request.Track.Name, request.Track.Artist);
                Console.WriteLine($"[音乐缓存] API播放音乐: {request.Track.Name} - {request.Track.Artist}");
                
                var result = await _musicService.PlayTrackAsync(request.Track);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "播放音乐失败: {TrackName}", request.Track?.Name);
                return StatusCode(500, $"播放失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 切换播放/暂停状态
        /// </summary>
        /// <returns>操作结果</returns>
        [HttpPost("toggle")]
        public async Task<IActionResult> TogglePlayPause()
        {
            try
            {
                _logger.LogInformation("API切换播放/暂停状态");
                var result = await _musicService.TogglePlayPauseAsync();
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "切换播放/暂停状态失败");
                return StatusCode(500, $"操作失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 暂停播放
        /// </summary>
        /// <returns>操作结果</returns>
        [HttpPost("pause")]
        public async Task<IActionResult> Pause()
        {
            try
            {
                _logger.LogInformation("API暂停播放");
                await _musicService.PauseAsync();
                return Ok(new { Success = true, Message = "暂停成功" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "暂停播放失败");
                return StatusCode(500, $"暂停失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 恢复播放
        /// </summary>
        /// <returns>操作结果</returns>
        [HttpPost("resume")]
        public async Task<IActionResult> Resume()
        {
            try
            {
                _logger.LogInformation("API恢复播放");
                await _musicService.ResumeAsync();
                return Ok(new { Success = true, Message = "恢复播放成功" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "恢复播放失败");
                return StatusCode(500, $"恢复播放失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 停止播放
        /// </summary>
        /// <returns>操作结果</returns>
        [HttpPost("stop")]
        public async Task<IActionResult> Stop()
        {
            try
            {
                _logger.LogInformation("API停止播放");
                var result = await _musicService.StopAsync();
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "停止播放失败");
                return StatusCode(500, $"停止失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 跳转到指定位置
        /// </summary>
        /// <param name="request">跳转请求</param>
        /// <returns>操作结果</returns>
        [HttpPost("seek")]
        public async Task<IActionResult> Seek([FromBody] SeekRequest request)
        {
            try
            {
                if (request.Position < 0)
                {
                    return BadRequest("跳转位置不能为负数");
                }

                _logger.LogInformation("API跳转到位置: {Position}秒", request.Position);
                var result = await _musicService.SeekAsync(request.Position);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "跳转失败");
                return StatusCode(500, $"跳转失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置音量
        /// </summary>
        /// <param name="request">音量设置请求</param>
        /// <returns>操作结果</returns>
        [HttpPost("volume")]
        public async Task<IActionResult> SetVolume([FromBody] SetVolumeRequest request)
        {
            try
            {
                if (request.Volume < 0 || request.Volume > 100)
                {
                    return BadRequest("音量必须在0-100之间");
                }

                _logger.LogInformation("API设置音量: {Volume}%", request.Volume);
                var result = await _musicService.SetVolumeAsync(request.Volume);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "设置音量失败");
                return StatusCode(500, $"设置音量失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取当前播放状态
        /// </summary>
        /// <returns>播放状态信息</returns>
        [HttpGet("status")]
        public IActionResult GetStatus()
        {
            try
            {
                var status = new
                {
                    IsPlaying = _musicService.IsPlaying,
                    IsPaused = _musicService.IsPaused,
                    CurrentTrack = _musicService.CurrentTrack,
                    CurrentPosition = _musicService.CurrentPosition,
                    TotalDuration = _musicService.TotalDuration.TotalSeconds,
                    Progress = _musicService.Progress
                };

                return Ok(status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取播放状态失败");
                return StatusCode(500, $"获取状态失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取当前歌词
        /// </summary>
        /// <returns>歌词信息</returns>
        [HttpGet("lyrics")]
        public async Task<IActionResult> GetLyrics()
        {
            try
            {
                var lyrics = await _musicService.GetLyricsAsync();
                return Ok(new { Lyrics = lyrics });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "获取歌词失败");
                return StatusCode(500, $"获取歌词失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 清理音乐缓存
        /// </summary>
        /// <returns>操作结果</returns>
        [HttpPost("clear-cache")]
        public async Task<IActionResult> ClearCache()
        {
            try
            {
                _logger.LogInformation("API清理音乐缓存");
                Console.WriteLine("[音乐缓存] API清理音乐缓存请求");
                await _musicService.ClearCacheAsync();
                return Ok(new { Success = true, Message = "缓存清理成功" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理缓存失败");
                return StatusCode(500, $"清理缓存失败: {ex.Message}");
            }
        }
    }

    // 请求模型
    public class SearchAndPlayRequest
    {
        public string SongName { get; set; } = string.Empty;
    }

    public class PlayTrackRequest
    {
        public MusicTrack? Track { get; set; }
    }

    public class SeekRequest
    {
        public double Position { get; set; }
    }

    public class SetVolumeRequest
    {
        public double Volume { get; set; }
    }
}
