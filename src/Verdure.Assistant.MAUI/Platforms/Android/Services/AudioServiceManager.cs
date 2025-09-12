using Android.Content;
using AndroidX.Core.Content;
using Verdure.Assistant.MAUI.Services;
using Microsoft.Extensions.Logging;
using AndroidApp = Android.App.Application;

namespace Verdure.Assistant.MAUI.Platforms.Android.Services;

/// <summary>
/// Android平台的音频服务管理器实现
/// </summary>
public class AudioServiceManager : IAudioServiceManager
{
    private readonly ILogger<AudioServiceManager> _logger;
    private readonly AudioServiceConnection _serviceConnection;
    private Context? _context;
    
    public bool IsServiceRunning => _serviceConnection.IsBound;
    public bool IsRecording => _serviceConnection.Service?.IsRecording ?? false;
    public bool HasLastRecording => _serviceConnection.Service?.HasLastRecording ?? false;

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<string>? AudioDataReceived;
    public event EventHandler<bool>? RecordingAvailable;

    public AudioServiceManager(ILogger<AudioServiceManager> logger)
    {
        _logger = logger;
        var connectionLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<AudioServiceConnection>.Instance;
        _serviceConnection = new AudioServiceConnection(connectionLogger);
        _serviceConnection.ServiceConnected += OnServiceConnected;
        _serviceConnection.ServiceDisconnected += OnServiceDisconnected;
        
        // 获取当前应用上下文
        _context = Platform.CurrentActivity ?? AndroidApp.Context;
    }

    private void OnServiceConnected(object? sender, EventArgs e)
    {
        var service = _serviceConnection.Service;
        if (service != null)
        {
            // 订阅服务事件
            service.StatusChanged += (s, status) => StatusChanged?.Invoke(s, status);
            service.AudioDataReceived += (s, data) => AudioDataReceived?.Invoke(s, data);
            service.RecordingAvailable += (s, available) => RecordingAvailable?.Invoke(s, available);
        }
        
        _logger.LogInformation("音频服务已连接");
        StatusChanged?.Invoke(this, "服务连接成功");
    }

    private void OnServiceDisconnected(object? sender, EventArgs e)
    {
        _logger.LogInformation("音频服务已断开");
        StatusChanged?.Invoke(this, "服务连接断开");
    }

    public async Task<bool> StartServiceAsync()
    {
        try
        {
            if (_context == null)
            {
                _logger.LogError("Context为空，无法启动服务");
                return false;
            }

            // 启动前台服务
            var serviceIntent = new Intent(_context, typeof(AudioForegroundService));
            ContextCompat.StartForegroundService(_context, serviceIntent);

            // 绑定服务
            var bindIntent = new Intent(_context, typeof(AudioForegroundService));
            var result = _context.BindService(bindIntent, _serviceConnection, Bind.AutoCreate);
            
            if (result)
            {
                _logger.LogInformation("音频服务启动成功");
                return true;
            }
            else
            {
                _logger.LogError("绑定音频服务失败");
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动音频服务失败");
            return false;
        }
    }

    public async Task<bool> StopServiceAsync()
    {
        try
        {
            if (_context == null || !_serviceConnection.IsBound)
                return true;

            // 解绑服务
            _context.UnbindService(_serviceConnection);
            
            // 停止前台服务
            var serviceIntent = new Intent(_context, typeof(AudioForegroundService));
            _context.StopService(serviceIntent);
            
            _logger.LogInformation("音频服务已停止");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停止音频服务失败");
            return false;
        }
    }

    public async Task<bool> StartRecordingAsync()
    {
        var service = _serviceConnection.Service;
        if (service == null)
        {
            _logger.LogWarning("服务未连接，无法开始录音");
            return false;
        }

        return await service.StartRecordingAsync();
    }

    public async Task<bool> StopRecordingAsync()
    {
        var service = _serviceConnection.Service;
        if (service == null)
        {
            _logger.LogWarning("服务未连接，无法停止录音");
            return false;
        }

        return await service.StopRecordingAsync();
    }

    public async Task<bool> SpeakTextAsync(string text)
    {
        var service = _serviceConnection.Service;
        if (service == null)
        {
            _logger.LogWarning("服务未连接，无法播放语音");
            return false;
        }

        return await service.SpeakTextAsync(text);
    }

    public async Task<bool> PlayMp3FileAsync(string fileName = "test.mp3")
    {
        var service = _serviceConnection.Service;
        if (service == null)
        {
            _logger.LogWarning("服务未连接，无法播放MP3");
            return false;
        }

        return await service.PlayMp3FileAsync(fileName);
    }

    public async Task<bool> StopMp3PlaybackAsync()
    {
        var service = _serviceConnection.Service;
        if (service == null)
        {
            _logger.LogWarning("服务未连接，无法停止MP3播放");
            return false;
        }

        return await service.StopMp3PlaybackAsync();
    }

    public async Task<bool> PlayLastRecordingAsync()
    {
        var service = _serviceConnection.Service;
        if (service == null)
        {
            _logger.LogWarning("服务未连接，无法播放录音");
            return false;
        }

        return await service.PlayLastRecordingAsync();
    }

    public async Task<bool> StopRecordingPlaybackAsync()
    {
        var service = _serviceConnection.Service;
        if (service == null)
        {
            _logger.LogWarning("服务未连接，无法停止录音播放");
            return false;
        }

        return await service.StopRecordingPlaybackAsync();
    }
}
