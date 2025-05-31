using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.Resources;
using Windows.Storage;
using XiaoZhi.Core.Interfaces;
using XiaoZhi.Core.Models;

namespace XiaoZhi.WinUI.Views
{
    public sealed partial class SettingsPage : Page
    {
        private readonly ILogger<SettingsPage>? _logger;
        private readonly ISettingsService<AppSettings>? _settingsService;
        private readonly ApplicationDataContainer _localSettings;
        private readonly ResourceLoader _resourceLoader;
        private AppSettings? _currentSettings;        
        public SettingsPage()
        {
            this.InitializeComponent();

            // Initialize ResourceLoader
            _resourceLoader = new();

            try
            {
                _logger = App.GetService<ILogger<SettingsPage>>();
                _settingsService = App.GetService<ISettingsService<AppSettings>>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to get services: {ex.Message}");
            }

            _localSettings = ApplicationData.Current.LocalSettings;
            _ = LoadSettingsAsync();
        }        
        private async Task LoadSettingsAsync()
        {
            try
            {
                // Load settings using the new settings service
                if (_settingsService is not null)
                {
                    _currentSettings = await _settingsService.LoadSettingsAsync();
                }
                else
                {
                    // Fallback to default settings
                    _currentSettings = new AppSettings();
                }

                // Update UI controls with loaded settings
                WakeWordToggle.IsOn = _currentSettings.WakeWordEnabled;
                WakeWordsTextBox.Text = _currentSettings.WakeWords;
                DeviceIdTextBox.Text = _currentSettings.DeviceId;
                WsAddressTextBox.Text = _currentSettings.WsAddress;
                WsTokenTextBox.Text = _currentSettings.WsToken;
                DefaultVolumeSlider.Value = _currentSettings.DefaultVolume;
                AutoAdjustVolumeToggle.IsOn = _currentSettings.AutoAdjustVolume;
                AudioInputDeviceTextBox.Text = _currentSettings.AudioInputDevice;
                AudioOutputDeviceTextBox.Text = _currentSettings.AudioOutputDevice;
                AutoStartToggle.IsOn = _currentSettings.AutoStart;
                MinimizeToTrayToggle.IsOn = _currentSettings.MinimizeToTray;
                EnableLoggingToggle.IsOn = _currentSettings.EnableLogging;
                ConnectionTimeoutNumberBox.Value = _currentSettings.ConnectionTimeout;
                AudioSampleRateNumberBox.Value = _currentSettings.AudioSampleRate;
                AudioChannelsNumberBox.Value = _currentSettings.AudioChannels;

                await LoadAudioDevicesAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to load settings");
                await ShowErrorDialog(string.Format(_resourceLoader.GetString("Error_LoadSettings"), ex.Message));
            }
        }

        private async Task LoadAudioDevicesAsync()
        {
            try
            {
                // Load audio input devices
                var inputDevices = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(
                    Windows.Media.Devices.MediaDevice.GetAudioCaptureSelector());

                // Load audio output devices  
                var outputDevices = await Windows.Devices.Enumeration.DeviceInformation.FindAllAsync(
                    Windows.Media.Devices.MediaDevice.GetAudioRenderSelector());

                // Update device text boxes with current default devices
                if (inputDevices.Count > 0)
                {
                    AudioInputDeviceTextBox.Text = inputDevices[0].Name;
                }

                if (outputDevices.Count > 0)
                {
                    AudioOutputDeviceTextBox.Text = outputDevices[0].Name;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to load audio devices");
                await ShowErrorDialog(string.Format(_resourceLoader.GetString("Error_LoadAudioDevices"), ex.Message));
            }
        }

        #region Event Handlers

        private void WakeWordToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleSwitch toggle)
            {
                _localSettings.Values["WakeWordEnabled"] = toggle.IsOn;
            }
        }

        private void WakeWordsTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                _localSettings.Values["WakeWords"] = textBox.Text;
            }
        }

        private void DeviceIdTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                _localSettings.Values["DeviceId"] = textBox.Text;
            }
        }

        private void WsAddressTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                _localSettings.Values["WsAddress"] = textBox.Text;
            }
        }

        private void WsTokenTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                _localSettings.Values["WsToken"] = textBox.Text;
            }
        }

        private void DefaultVolumeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
        {
            if (sender is Slider slider && _localSettings != null)
            {
                _localSettings.Values["DefaultVolume"] = slider.Value;
            }
        }

        private void AutoAdjustVolumeToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleSwitch toggle)
            {
                _localSettings.Values["AutoAdjustVolume"] = toggle.IsOn;
            }
        }

        private void AudioInputDeviceTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                _localSettings.Values["AudioInputDevice"] = textBox.Text;
            }
        }

        private void AudioOutputDeviceTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                _localSettings.Values["AudioOutputDevice"] = textBox.Text;
            }
        }

        private async void RefreshAudioDevicesButton_Click(object sender, RoutedEventArgs e)
        {
            await LoadAudioDevicesAsync();
            await ShowInfoDialog(_resourceLoader.GetString("Info_AudioDevicesRefreshed"));
        }

        private void AutoStartToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleSwitch toggle)
            {
                _localSettings.Values["AutoStart"] = toggle.IsOn;
            }
        }

        private void MinimizeToTrayToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleSwitch toggle)
            {
                _localSettings.Values["MinimizeToTray"] = toggle.IsOn;
            }
        }

        private void EnableLoggingToggle_Toggled(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleSwitch toggle)
            {
                _localSettings.Values["EnableLogging"] = toggle.IsOn;
            }
        }

        private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem item)
            {
                _localSettings.Values["Theme"] = item.Content?.ToString() ?? "Default";
            }
        }

        private void ConnectionTimeoutNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            _localSettings.Values["ConnectionTimeout"] = sender.Value;
        }

        private void AudioSampleRateNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            _localSettings.Values["AudioSampleRate"] = sender.Value;
        }

        private void AudioChannelsNumberBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            _localSettings.Values["AudioChannels"] = sender.Value;
        }

        private void AudioCodecComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem item)
            {
                _localSettings.Values["AudioCodec"] = item.Content?.ToString() ?? "PCM";
            }
        }        
        private async void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Update current settings from UI
                UpdateCurrentSettingsFromUI();

                // Save using the settings service
                if (_settingsService is not null && _currentSettings is not null)
                {
                    await _settingsService.SaveSettingsAsync(_currentSettings);
                    await ShowInfoDialog(_resourceLoader.GetString("Info_SettingsSaved"));
                }
                else
                {
                    // Fallback to local settings
                    SaveToLocalSettings();
                    await ShowInfoDialog(_resourceLoader.GetString("Info_SettingsSaved"));
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to save settings");
                await ShowErrorDialog(string.Format(_resourceLoader.GetString("Error_SaveSettings"), ex.Message));
            }
        }        private void UpdateCurrentSettingsFromUI()
        {
            if (_currentSettings is null)
                _currentSettings = new AppSettings();

            _currentSettings.WakeWordEnabled = WakeWordToggle.IsOn;
            _currentSettings.WakeWords = WakeWordsTextBox.Text;
            _currentSettings.DeviceId = DeviceIdTextBox.Text;
            _currentSettings.WsAddress = WsAddressTextBox.Text;
            _currentSettings.WsToken = WsTokenTextBox.Text;
            _currentSettings.DefaultVolume = DefaultVolumeSlider.Value;
            _currentSettings.AutoAdjustVolume = AutoAdjustVolumeToggle.IsOn;
            _currentSettings.AudioInputDevice = AudioInputDeviceTextBox.Text;
            _currentSettings.AudioOutputDevice = AudioOutputDeviceTextBox.Text;
            _currentSettings.AutoStart = AutoStartToggle.IsOn;
            _currentSettings.MinimizeToTray = MinimizeToTrayToggle.IsOn;
            _currentSettings.EnableLogging = EnableLoggingToggle.IsOn;
            _currentSettings.ConnectionTimeout = ConnectionTimeoutNumberBox.Value;
            _currentSettings.AudioSampleRate = AudioSampleRateNumberBox.Value;
            _currentSettings.AudioChannels = AudioChannelsNumberBox.Value;
        }

        private void UpdateUIFromSettings()
        {
            if (_currentSettings is null) return;

            WakeWordToggle.IsOn = _currentSettings.WakeWordEnabled;
            WakeWordsTextBox.Text = _currentSettings.WakeWords;
            DeviceIdTextBox.Text = _currentSettings.DeviceId;
            WsAddressTextBox.Text = _currentSettings.WsAddress;
            WsTokenTextBox.Text = _currentSettings.WsToken;
            DefaultVolumeSlider.Value = _currentSettings.DefaultVolume;
            AutoAdjustVolumeToggle.IsOn = _currentSettings.AutoAdjustVolume;
            AudioInputDeviceTextBox.Text = _currentSettings.AudioInputDevice;
            AudioOutputDeviceTextBox.Text = _currentSettings.AudioOutputDevice;
            AutoStartToggle.IsOn = _currentSettings.AutoStart;
            MinimizeToTrayToggle.IsOn = _currentSettings.MinimizeToTray;
            EnableLoggingToggle.IsOn = _currentSettings.EnableLogging;
            ConnectionTimeoutNumberBox.Value = _currentSettings.ConnectionTimeout;
            AudioSampleRateNumberBox.Value = _currentSettings.AudioSampleRate;
            AudioChannelsNumberBox.Value = _currentSettings.AudioChannels;
        }

        private void SaveToLocalSettings()
        {
            if (_currentSettings is null || _localSettings is null) return;

            _localSettings.Values["WakeWordEnabled"] = _currentSettings.WakeWordEnabled;
            _localSettings.Values["WakeWords"] = _currentSettings.WakeWords;
            _localSettings.Values["DeviceId"] = _currentSettings.DeviceId;
            _localSettings.Values["WsAddress"] = _currentSettings.WsAddress;
            _localSettings.Values["WsToken"] = _currentSettings.WsToken;
            _localSettings.Values["DefaultVolume"] = _currentSettings.DefaultVolume;
            _localSettings.Values["AutoAdjustVolume"] = _currentSettings.AutoAdjustVolume;
            _localSettings.Values["AudioInputDevice"] = _currentSettings.AudioInputDevice;
            _localSettings.Values["AudioOutputDevice"] = _currentSettings.AudioOutputDevice;
            _localSettings.Values["AutoStart"] = _currentSettings.AutoStart;
            _localSettings.Values["MinimizeToTray"] = _currentSettings.MinimizeToTray;
            _localSettings.Values["EnableLogging"] = _currentSettings.EnableLogging;
            _localSettings.Values["ConnectionTimeout"] = _currentSettings.ConnectionTimeout;
            _localSettings.Values["AudioSampleRate"] = _currentSettings.AudioSampleRate;
            _localSettings.Values["AudioChannels"] = _currentSettings.AudioChannels;
        }        
        private async void ExportSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_settingsService is not null && _currentSettings is not null)
                {
                    // Update current settings from UI first
                    UpdateCurrentSettingsFromUI();

                    // Use Windows file picker for export
                    var picker = new Windows.Storage.Pickers.FileSavePicker();
                    picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
                    picker.FileTypeChoices.Add("JSON Settings", new List<string>() { ".json" });
                    picker.SuggestedFileName = "xiaozhi-settings";

                    // Initialize the picker
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                    var file = await picker.PickSaveFileAsync();
                    if (file != null)
                    {
                        var success = await _settingsService.ExportSettingsAsync(file.Path, _currentSettings);
                        if (success)
                        {
                            await ShowInfoDialog(_resourceLoader.GetString("Info_SettingsExported"));
                        }
                        else
                        {
                            await ShowErrorDialog(_resourceLoader.GetString("Error_ExportSettings"));
                        }
                    }
                }
                else
                {
                    await ShowInfoDialog(_resourceLoader.GetString("Info_ExportNotImplemented"));
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to export settings");
                await ShowErrorDialog(string.Format(_resourceLoader.GetString("Error_ExportSettings"), ex.Message));
            }
        }        
        private async void ImportSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_settingsService is not null)
                {
                    // Use Windows file picker for import
                    var picker = new Windows.Storage.Pickers.FileOpenPicker();
                    picker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
                    picker.FileTypeFilter.Add(".json");

                    // Initialize the picker
                    var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                    var file = await picker.PickSingleFileAsync();
                    if (file != null)
                    {
                        var importedSettings = await _settingsService.ImportSettingsAsync(file.Path);                        if (importedSettings != null)
                        {
                            _currentSettings = importedSettings;
                            // Update UI with imported settings
                            UpdateUIFromSettings();
                            await ShowInfoDialog(_resourceLoader.GetString("Info_SettingsImported"));
                        }
                        else
                        {
                            await ShowErrorDialog(_resourceLoader.GetString("Error_ImportSettings"));
                        }
                    }
                }
                else
                {
                    await ShowInfoDialog(_resourceLoader.GetString("Info_ImportNotImplemented"));
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to import settings");
                await ShowErrorDialog(string.Format(_resourceLoader.GetString("Error_ImportSettings"), ex.Message));
            }
        }        private async void ResetToDefaultsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {                if (_settingsService is not null)
                {
                    // Use the settings service to reset to defaults
                    await _settingsService.ResetToDefaultAsync();
                    // Reload the settings
                    _currentSettings = await _settingsService.LoadSettingsAsync();
                    // Update UI with default settings
                    UpdateUIFromSettings();
                }
                else
                {
                    // Fallback: Clear all settings
                    _localSettings.Values.Clear();
                    // Reload default settings
                    await LoadSettingsAsync();
                }

                await ShowInfoDialog(_resourceLoader.GetString("Info_SettingsReset"));
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to reset settings");
                await ShowErrorDialog(string.Format(_resourceLoader.GetString("Error_ResetSettings"), ex.Message));
            }
        }

        #endregion

        #region Helper Methods

        private async Task ShowInfoDialog(string message)
        {
            var dialog = new ContentDialog()
            {
                Title = _resourceLoader.GetString("Dialog_InfoTitle"),
                Content = message,
                CloseButtonText = _resourceLoader.GetString("Dialog_OkButton"),
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }

        private async Task ShowErrorDialog(string message)
        {
            var dialog = new ContentDialog()
            {
                Title = _resourceLoader.GetString("Dialog_ErrorTitle"),
                Content = message,
                CloseButtonText = _resourceLoader.GetString("Dialog_OkButton"),
                XamlRoot = this.XamlRoot
            };
            await dialog.ShowAsync();
        }

        #endregion
    }
}