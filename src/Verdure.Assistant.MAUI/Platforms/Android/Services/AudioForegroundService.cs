using Android;
using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;
using Plugin.Maui.Audio;
using Microsoft.Extensions.Logging;
using AndroidPM = Android.Content.PM;
using AndroidMedia = Android.Media;

namespace Verdure.Assistant.MAUI.Platforms.Android.Services;

/// <summary>
/// Android前台服务，用于后台音频录制和播放
/// </summary>
[Service(Exported = true)]
public class AudioForegroundService : Service
{
    private const int NOTIFICATION_ID = 1001;
    private const string CHANNEL_ID = "VerdureAssistantAudioService";
    
    private readonly ILogger<AudioForegroundService>? _logger;
    private IAudioManager? _audioManager;
    private IAudioRecorder? _audioRecorder;
    private IAudioPlayer? _audioPlayer;
    private bool _isRecording;
    private string? _lastRecordingPath;
    private NotificationManager? _notificationManager;

    public event EventHandler<string>? StatusChanged;
    public event EventHandler<string>? AudioDataReceived;
    public event EventHandler<bool>? RecordingAvailable;

    public bool IsRecording => _isRecording;
    public bool HasLastRecording => !string.IsNullOrEmpty(_lastRecordingPath);

    public AudioForegroundService()
    {
        // 获取日志服务（如果可用）
        try
        {
            var serviceProvider = IPlatformApplication.Current?.Services;
            _logger = serviceProvider?.GetService<ILogger<AudioForegroundService>>();
        }
        catch
        {
            // 忽略错误，继续无日志运行
        }
    }

    public override IBinder? OnBind(Intent? intent)
    {
        return new AudioServiceBinder(this);
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        CreateNotificationChannel();
        StartForeground(NOTIFICATION_ID, CreateNotification());
        
        InitializeAudioServices();
        
        _logger?.LogInformation("AudioForegroundService started");
        OnStatusChanged("服务已启动");
        
        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        _ = StopRecordingAsync();
        CleanupAudioServices();
        
        _logger?.LogInformation("AudioForegroundService destroyed");
        OnStatusChanged("服务已停止");
        
        base.OnDestroy();
    }

    private void InitializeAudioServices()
    {
        try
        {
            // 初始化音频管理器
            _audioManager = AudioManager.Current;
            _notificationManager = GetSystemService(NotificationService) as NotificationManager;
            
            _logger?.LogInformation("音频服务初始化成功");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "初始化音频服务失败");
            OnStatusChanged($"初始化失败: {ex.Message}");
        }
    }

    private void CleanupAudioServices()
    {
        try
        {
            _ = StopRecordingAsync();
            _audioRecorder = null;
            _audioPlayer = null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "清理音频服务失败");
        }
    }

    #region 录音功能

    public async Task<bool> StartRecordingAsync()
    {
        if (_isRecording || _audioManager == null)
            return false;

        try
        {
            // 创建录音器
            _audioRecorder = _audioManager.CreateRecorder();
            
            // 配置录音参数
            var recordOptions = new AudioRecorderOptions
            {
                SampleRate = 44100,
                Channels = ChannelType.Mono,
                BitDepth = BitDepth.Pcm16bit,
                Encoding = Encoding.Wav,
            };

            // 生成录音文件路径
            var documentsPath = global::Android.OS.Environment.GetExternalStoragePublicDirectory(
                global::Android.OS.Environment.DirectoryDocuments)?.AbsolutePath;
            var fileName = $"recording_{DateTime.Now:yyyyMMdd_HHmmss}.wav";
            _lastRecordingPath = Path.Combine(documentsPath ?? "", fileName);

            // 开始录音
            await _audioRecorder.StartAsync(_lastRecordingPath, recordOptions);
            _isRecording = true;
            
            UpdateNotification("正在录音...");
            OnStatusChanged("开始录音");
            OnAudioDataReceived($"录音文件: {fileName}");
            OnRecordingAvailable(true);
            
            _logger?.LogInformation("录音开始: {Path}", _lastRecordingPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "开始录音失败");
            OnStatusChanged($"录音失败: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> StopRecordingAsync()
    {
        if (!_isRecording || _audioRecorder == null)
            return false;

        try
        {
            await _audioRecorder.StopAsync();
            _isRecording = false;
            
            UpdateNotification("录音已停止");
            OnStatusChanged("录音停止");
            OnAudioDataReceived($"录音保存完成: {Path.GetFileName(_lastRecordingPath)}");
            
            _logger?.LogInformation("录音停止: {Path}", _lastRecordingPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "停止录音失败");
            OnStatusChanged($"停止录音失败: {ex.Message}");
            return false;
        }
        finally
        {
            _audioRecorder = null;
        }
    }

    #endregion

    #region 播放功能

    public async Task<bool> SpeakTextAsync(string text)
    {
        try
        {
            UpdateNotification($"语音播放: {text}");
            OnStatusChanged($"语音播放: {text}");
            
            // 模拟TTS播放
            await Task.Delay(2000);
            
            UpdateNotification("准备就绪");
            OnStatusChanged("语音播放完成");
            
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "语音播放失败");
            OnStatusChanged($"语音播放失败: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> PlayLastRecordingAsync()
    {
        if (string.IsNullOrEmpty(_lastRecordingPath) || !File.Exists(_lastRecordingPath))
        {
            OnStatusChanged("没有可播放的录音文件");
            return false;
        }

        try
        {
            _audioPlayer?.Dispose();
            _audioPlayer = _audioManager?.CreatePlayer();
            
            if (_audioPlayer == null)
            {
                OnStatusChanged("创建播放器失败");
                return false;
            }

            // 简化播放逻辑 - 暂时记录文件路径供后续播放
            UpdateNotification("录音播放准备中...");
            OnStatusChanged("开始播放录音");
            OnAudioDataReceived($"播放文件: {Path.GetFileName(_lastRecordingPath)}");
            
            _logger?.LogInformation("开始播放录音: {Path}", _lastRecordingPath);
            
            // TODO: 实现真正的播放逻辑
            await Task.Delay(1000); // 模拟播放
            
            UpdateNotification("录音播放完成");
            OnStatusChanged("录音播放完成");
            
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "播放录音失败");
            OnStatusChanged($"播放录音失败: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> StopRecordingPlaybackAsync()
    {
        try
        {
            if (_audioPlayer != null)
            {
                _audioPlayer.Stop();
                _audioPlayer.Dispose();
                _audioPlayer = null;
            }
            
            UpdateNotification("准备就绪");
            OnStatusChanged("录音播放停止");
            
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "停止录音播放失败");
            OnStatusChanged($"停止播放失败: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> PlayMp3FileAsync(string fileName)
    {
        try
        {
            UpdateNotification($"播放音乐: {fileName}");
            OnStatusChanged($"播放音乐: {fileName}");
            
            // 模拟MP3播放
            await Task.Delay(3000);
            
            UpdateNotification("准备就绪");
            OnStatusChanged("音乐播放完成");
            
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "播放MP3失败");
            OnStatusChanged($"播放失败: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> StopMp3PlaybackAsync()
    {
        try
        {
            UpdateNotification("准备就绪");
            OnStatusChanged("音乐播放停止");
            
            await Task.CompletedTask;
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "停止MP3播放失败");
            OnStatusChanged($"停止播放失败: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region 通知管理

    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var channel = new NotificationChannel(
                CHANNEL_ID,
                "绿荫助手音频服务",
                NotificationImportance.Low)
            {
                Description = "后台音频录制和播放服务"
            };

            _notificationManager?.CreateNotificationChannel(channel);
        }
    }

    private Notification CreateNotification(string? content = null)
    {
        var intent = new Intent(this, typeof(MainActivity));
        intent.SetFlags(ActivityFlags.SingleTop);
        
        var pendingIntent = PendingIntent.GetActivity(
            this, 0, intent, 
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var builder = new NotificationCompat.Builder(this, CHANNEL_ID)
            .SetContentTitle("绿荫助手")
            .SetContentText(content ?? "音频服务运行中")
            .SetSmallIcon(global::Android.Resource.Drawable.IcMenuCall)
            .SetContentIntent(pendingIntent)
            .SetOngoing(true)
            .SetPriority(NotificationCompat.PriorityLow);

        return builder.Build();
    }

    private void UpdateNotification(string content)
    {
        var notification = CreateNotification(content);
        _notificationManager?.Notify(NOTIFICATION_ID, notification);
    }

    #endregion

    #region 事件触发

    private void OnStatusChanged(string status)
    {
        StatusChanged?.Invoke(this, status);
    }

    private void OnAudioDataReceived(string data)
    {
        AudioDataReceived?.Invoke(this, data);
    }

    private void OnRecordingAvailable(bool available)
    {
        RecordingAvailable?.Invoke(this, available);
    }

    #endregion
}

/// <summary>
/// 服务绑定器
/// </summary>
public class AudioServiceBinder : Binder
{
    private readonly AudioForegroundService _service;

    public AudioServiceBinder(AudioForegroundService service)
    {
        _service = service;
    }

    public AudioForegroundService GetService() => _service;
}
