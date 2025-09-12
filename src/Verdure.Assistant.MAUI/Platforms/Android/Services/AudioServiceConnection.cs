using Android.Content;
using Android.OS;
using Microsoft.Extensions.Logging;

namespace Verdure.Assistant.MAUI.Platforms.Android.Services;

/// <summary>
/// 音频服务连接管理器
/// </summary>
public class AudioServiceConnection : Java.Lang.Object, IServiceConnection
{
    private readonly ILogger<AudioServiceConnection>? _logger;
    private AudioForegroundService? _service;
    private bool _isBound;

    public event EventHandler? ServiceConnected;
    public event EventHandler? ServiceDisconnected;

    public bool IsBound => _isBound;
    public AudioForegroundService? Service => _service;

    public AudioServiceConnection(ILogger<AudioServiceConnection>? logger = null)
    {
        _logger = logger;
    }

    public void OnServiceConnected(ComponentName? name, IBinder? service)
    {
        if (service is AudioServiceBinder binder)
        {
            _service = binder.GetService();
            _isBound = true;
            
            _logger?.LogInformation("AudioForegroundService connected");
            ServiceConnected?.Invoke(this, EventArgs.Empty);
        }
    }

    public void OnServiceDisconnected(ComponentName? name)
    {
        _service = null;
        _isBound = false;
        
        _logger?.LogInformation("AudioForegroundService disconnected");
        ServiceDisconnected?.Invoke(this, EventArgs.Empty);
    }
}
