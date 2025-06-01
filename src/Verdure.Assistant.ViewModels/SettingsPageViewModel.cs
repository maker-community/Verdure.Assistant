using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Models;

namespace Verdure.Assistant.ViewModels;

/// <summary>
/// 设置页面ViewModel
/// </summary>
public partial class SettingsPageViewModel : ViewModelBase
{
    private readonly ISettingsService<AppSettings>? _settingsService;
    private AppSettings? _currentSettings;

    #region 可观察属性

    [ObservableProperty]
    private string _serverUrl = "ws://localhost:8080/ws";

    [ObservableProperty]
    private bool _useWebSocket = true;

    [ObservableProperty]
    private bool _enableVoice = true;

    [ObservableProperty]
    private int _audioSampleRate = 16000;

    [ObservableProperty]
    private int _audioChannels = 1;

    [ObservableProperty]
    private string _audioFormat = "opus";    [ObservableProperty]
    private bool _wakeWordEnabled = false;

    [ObservableProperty]
    private string _wakeWords = "小智";

    [ObservableProperty]
    private string _deviceId = "";

    [ObservableProperty]
    private string _otaProtocol = "https://";

    [ObservableProperty]
    private string _otaAddress = "";

    [ObservableProperty]
    private string _wsProtocol = "wss://";

    [ObservableProperty]
    private string _wsAddress = "";

    [ObservableProperty]
    private string _wsToken = "";

    [ObservableProperty]
    private double _defaultVolume = 80.0;

    [ObservableProperty]
    private bool _autoAdjustVolume = true;

    [ObservableProperty]
    private string _audioInputDevice = "";

    [ObservableProperty]
    private string _audioOutputDevice = "";

    [ObservableProperty]
    private bool _autoStart = false;

    [ObservableProperty]
    private bool _enableLogging = true;

    [ObservableProperty]
    private int _connectionTimeout = 30;

    [ObservableProperty]
    private string _audioCodec = "Opus";

    [ObservableProperty]
    private double _wakeWordSensitivity = 0.5;

    [ObservableProperty]
    private bool _enableVoiceActivityDetection = true;

    [ObservableProperty]
    private double _vadSensitivity = 0.6;

    [ObservableProperty]
    private int _vadSilenceTimeout = 2000;

    [ObservableProperty]
    private bool _enableEchoCancellation = true;

    [ObservableProperty]
    private bool _enableNoiseSuppression = true;

    [ObservableProperty]
    private double _outputVolume = 0.8;

    [ObservableProperty]
    private double _inputGain = 1.0;

    [ObservableProperty]
    private string _theme = "Default";

    [ObservableProperty]
    private string _language = "zh-CN";

    [ObservableProperty]
    private bool _autoConnect = false;

    [ObservableProperty]
    private bool _minimizeToTray = false;

    [ObservableProperty]
    private bool _startWithWindows = false;

    [ObservableProperty]
    private string _logLevel = "Information";

    [ObservableProperty]
    private bool _isDirty = false;

    #endregion

    public SettingsPageViewModel(ILogger<SettingsPageViewModel> logger,
        ISettingsService<AppSettings>? settingsService = null) : base(logger)
    {
        _settingsService = settingsService;
    }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        await LoadSettingsAsync();
    }

    #region 命令

    [RelayCommand]
    private async Task LoadSettingsAsync()
    {
        try
        {
            if (_settingsService != null)
            {
                _currentSettings = await _settingsService.LoadSettingsAsync();
            }
            else
            {
                _currentSettings = new AppSettings();
            }

            // 更新UI属性
            UpdatePropertiesFromSettings(_currentSettings);
            IsDirty = false;

            _logger?.LogInformation("Settings loaded successfully");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load settings");
            _currentSettings = new AppSettings();
            UpdatePropertiesFromSettings(_currentSettings);
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        try
        {
            if (_currentSettings == null || _settingsService == null)
            {
                _logger?.LogWarning("Settings service or current settings is null");
                return;
            }

            // 更新设置对象
            UpdateSettingsFromProperties(_currentSettings);

            // 保存设置
            await _settingsService.SaveSettingsAsync(_currentSettings);
            IsDirty = false;

            _logger?.LogInformation("Settings saved successfully");
            SettingsSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save settings");
            SettingsError?.Invoke(this, $"保存设置失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ResetSettingsAsync()
    {
        try
        {
            _currentSettings = new AppSettings();
            UpdatePropertiesFromSettings(_currentSettings);
            IsDirty = true;

            _logger?.LogInformation("Settings reset to defaults");
            SettingsReset?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to reset settings");
            SettingsError?.Invoke(this, $"重置设置失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private void TestConnection()
    {
        try
        {
            ConnectionTestRequested?.Invoke(this, ServerUrl);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to test connection");
            SettingsError?.Invoke(this, $"连接测试失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private void TestAudio()
    {
        try
        {
            AudioTestRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to test audio");
            SettingsError?.Invoke(this, $"音频测试失败: {ex.Message}");
        }
    }    [RelayCommand]
    private async Task ExportSettingsAsync()
    {
        try
        {
            ExportSettingsRequested?.Invoke(this, EventArgs.Empty);
            _logger?.LogInformation("Export settings requested");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to export settings");
            SettingsError?.Invoke(this, $"导出设置失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ImportSettingsAsync()
    {
        try
        {
            ImportSettingsRequested?.Invoke(this, EventArgs.Empty);
            _logger?.LogInformation("Import settings requested");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to import settings");
            SettingsError?.Invoke(this, $"导入设置失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task RefreshAudioDevicesAsync()
    {
        try
        {
            RefreshAudioDevicesRequested?.Invoke(this, EventArgs.Empty);
            _logger?.LogInformation("Refresh audio devices requested");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to refresh audio devices");
            SettingsError?.Invoke(this, $"刷新音频设备失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ChangeTheme(string theme)
    {
        try
        {
            Theme = theme;
            ThemeChangeRequested?.Invoke(this, theme);
            _logger?.LogInformation($"Theme changed to: {theme}");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to change theme");
            SettingsError?.Invoke(this, $"主题切换失败: {ex.Message}");
        }
    }    [RelayCommand]
    private void VolumeChanged(double volume)
    {
        try
        {
            DefaultVolume = volume;
            VolumeChangeRequested?.Invoke(this, volume);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to change volume");
            SettingsError?.Invoke(this, $"音量调整失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            LogFolderOpenRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to open log folder");
            SettingsError?.Invoke(this, $"打开日志文件夹失败: {ex.Message}");
        }
    }

    #endregion

    #region 辅助方法    
    private void UpdatePropertiesFromSettings(AppSettings settings)
    {
        //ServerUrl = settings.ServerUrl;
        //UseWebSocket = settings.UseWebSocket;
        //EnableVoice = settings.EnableVoice;
        //AudioSampleRate = settings.AudioSampleRate;
        //AudioChannels = settings.AudioChannels;
        //AudioFormat = settings.AudioFormat;
        //WakeWordEnabled = settings.EnableWakeWord;
        //WakeWords = settings.WakeWord;
        //WakeWordSensitivity = settings.WakeWordSensitivity;
        //EnableVoiceActivityDetection = settings.EnableVoiceActivityDetection;
        //VadSensitivity = settings.VadSensitivity;
        //VadSilenceTimeout = settings.VadSilenceTimeout;
        //EnableEchoCancellation = settings.EnableEchoCancellation;
        //EnableNoiseSuppression = settings.EnableNoiseSuppression;
        //OutputVolume = settings.OutputVolume;
        //InputGain = settings.InputGain;
        //Theme = settings.Theme;
        //Language = settings.Language;
        //AutoConnect = settings.AutoConnect;
        //MinimizeToTray = settings.MinimizeToTray;
        //StartWithWindows = settings.StartWithWindows;
        //LogLevel = settings.LogLevel;
        
        // Additional properties - may need default values if not in settings
        DefaultVolume = OutputVolume * 100; // Convert to percentage
        AutoAdjustVolume = true; // Default value
        AutoStart = StartWithWindows;
        EnableLogging = !string.IsNullOrEmpty(LogLevel) && LogLevel != "None";
        ConnectionTimeout = 30; // Default value
        AudioCodec = AudioFormat ?? "Opus";
    }    
    private void UpdateSettingsFromProperties(AppSettings settings)
    {
        //settings.ServerUrl = ServerUrl;
        //settings.UseWebSocket = UseWebSocket;
        //settings.EnableVoice = EnableVoice;
        //settings.AudioSampleRate = AudioSampleRate;
        //settings.AudioChannels = AudioChannels;
        //settings.AudioFormat = AudioFormat;
        //settings.EnableWakeWord = WakeWordEnabled;
        //settings.WakeWord = WakeWords;
        //settings.WakeWordSensitivity = WakeWordSensitivity;
        //settings.EnableVoiceActivityDetection = EnableVoiceActivityDetection;
        //settings.VadSensitivity = VadSensitivity;
        //settings.VadSilenceTimeout = VadSilenceTimeout;
        //settings.EnableEchoCancellation = EnableEchoCancellation;
        //settings.EnableNoiseSuppression = EnableNoiseSuppression;
        //settings.OutputVolume = DefaultVolume / 100.0; // Convert from percentage
        //settings.InputGain = InputGain;
        //settings.Theme = Theme;
        //settings.Language = Language;
        //settings.AutoConnect = AutoConnect;
        //settings.MinimizeToTray = MinimizeToTray;
        //settings.StartWithWindows = AutoStart;
        //settings.LogLevel = EnableLogging ? LogLevel : "None";
    }

    private void MarkDirty()
    {
        IsDirty = true;
    }

    #endregion    
    #region 属性变化处理

    partial void OnServerUrlChanged(string value) => MarkDirty();
    partial void OnUseWebSocketChanged(bool value) => MarkDirty();
    partial void OnEnableVoiceChanged(bool value) => MarkDirty();
    partial void OnAudioSampleRateChanged(int value) => MarkDirty();
    partial void OnAudioChannelsChanged(int value) => MarkDirty();
    partial void OnAudioFormatChanged(string value) => MarkDirty();
    partial void OnWakeWordEnabledChanged(bool value) => MarkDirty();
    partial void OnWakeWordsChanged(string value) => MarkDirty();
    partial void OnWakeWordSensitivityChanged(double value) => MarkDirty();
    partial void OnEnableVoiceActivityDetectionChanged(bool value) => MarkDirty();
    partial void OnVadSensitivityChanged(double value) => MarkDirty();
    partial void OnVadSilenceTimeoutChanged(int value) => MarkDirty();
    partial void OnEnableEchoCancellationChanged(bool value) => MarkDirty();
    partial void OnEnableNoiseSuppressionChanged(bool value) => MarkDirty();
    partial void OnOutputVolumeChanged(double value) => MarkDirty();
    partial void OnInputGainChanged(double value) => MarkDirty();
    partial void OnThemeChanged(string value) => MarkDirty();
    partial void OnLanguageChanged(string value) => MarkDirty();
    partial void OnAutoConnectChanged(bool value) => MarkDirty();
    partial void OnMinimizeToTrayChanged(bool value) => MarkDirty();
    partial void OnStartWithWindowsChanged(bool value) => MarkDirty();
    partial void OnLogLevelChanged(string value) => MarkDirty();
    partial void OnDeviceIdChanged(string value) => MarkDirty();
    partial void OnOtaProtocolChanged(string value) => MarkDirty();
    partial void OnOtaAddressChanged(string value) => MarkDirty();
    partial void OnWsProtocolChanged(string value) => MarkDirty();
    partial void OnWsAddressChanged(string value) => MarkDirty();
    partial void OnWsTokenChanged(string value) => MarkDirty();
    partial void OnDefaultVolumeChanged(double value) => MarkDirty();
    partial void OnAutoAdjustVolumeChanged(bool value) => MarkDirty();
    partial void OnAudioInputDeviceChanged(string value) => MarkDirty();
    partial void OnAudioOutputDeviceChanged(string value) => MarkDirty();
    partial void OnAutoStartChanged(bool value) => MarkDirty();
    partial void OnEnableLoggingChanged(bool value) => MarkDirty();
    partial void OnConnectionTimeoutChanged(int value) => MarkDirty();
    partial void OnAudioCodecChanged(string value) => MarkDirty();

    #endregion  
    
    #region 事件

    public event EventHandler? SettingsSaved;
    public event EventHandler? SettingsReset;
    public event EventHandler<string>? SettingsError;
    public event EventHandler<string>? ConnectionTestRequested;
    public event EventHandler? AudioTestRequested;
    public event EventHandler? LogFolderOpenRequested;
    public event EventHandler? ExportSettingsRequested;
    public event EventHandler? ImportSettingsRequested;
    public event EventHandler? RefreshAudioDevicesRequested;
    public event EventHandler<string>? ThemeChangeRequested;
    public event EventHandler<double>? VolumeChangeRequested;

    #endregion
}
