using Microsoft.Extensions.Logging;
using Windows.ApplicationModel.Resources;
using Windows.Storage;
using XiaoZhi.Core.Services;

namespace XiaoZhi.WinUI.Views
{
    public sealed partial class SettingsPage : Page
    {
        private readonly ILogger<SettingsPage>? _logger;
        private readonly IConfigurationService? _configurationService;
        private readonly ApplicationDataContainer _localSettings;
        private readonly ResourceLoader _resourceLoader;
        public SettingsPage()
        {
            this.InitializeComponent();

            // Initialize ResourceLoader
            _resourceLoader = new();

            try
            {
                _logger = App.GetService<ILogger<SettingsPage>>();
                _configurationService = App.GetService<IConfigurationService>();
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
                // Load wake word settings
                var wakeWordEnabled = _localSettings?.Values["WakeWordEnabled"] as bool? ?? false;
                WakeWordToggle.IsOn = wakeWordEnabled;

                var wakeWords = _localSettings?.Values["WakeWords"] as string ?? _resourceLoader.GetString("WakeWords_Default");
                WakeWordsTextBox.Text = wakeWords;

                // Load device settings
                var deviceId = _localSettings?.Values["DeviceId"] as string ?? "";
                DeviceIdTextBox.Text = deviceId;

                // Load server settings
                var wsAddress = _localSettings?.Values["WsAddress"] as string ?? "ws://localhost:8765";
                WsAddressTextBox.Text = wsAddress;

                var wsToken = _localSettings?.Values["WsToken"] as string ?? "";
                WsTokenTextBox.Text = wsToken;

                // Load audio settings
                var defaultVolume = _localSettings?.Values["DefaultVolume"] as double? ?? 50.0;
                DefaultVolumeSlider.Value = defaultVolume;

                var autoAdjustVolume = _localSettings?.Values["AutoAdjustVolume"] as bool? ?? true;
                AutoAdjustVolumeToggle.IsOn = autoAdjustVolume;

                var audioInputDevice = _localSettings?.Values["AudioInputDevice"] as string ?? "";
                AudioInputDeviceTextBox.Text = audioInputDevice;

                var audioOutputDevice = _localSettings?.Values["AudioOutputDevice"] as string ?? "";
                AudioOutputDeviceTextBox.Text = audioOutputDevice;

                // Load application settings
                var autoStart = _localSettings?.Values["AutoStart"] as bool? ?? false;
                AutoStartToggle.IsOn = autoStart;

                var minimizeToTray = _localSettings?.Values["MinimizeToTray"] as bool? ?? true;
                MinimizeToTrayToggle.IsOn = minimizeToTray;

                var enableLogging = _localSettings?.Values["EnableLogging"] as bool? ?? true;
                EnableLoggingToggle.IsOn = enableLogging;

                // Load advanced settings
                var connectionTimeout = _localSettings?.Values["ConnectionTimeout"] as double? ?? 10.0;
                ConnectionTimeoutNumberBox.Value = connectionTimeout;

                var audioSampleRate = _localSettings?.Values["AudioSampleRate"] as double? ?? 16000.0;
                AudioSampleRateNumberBox.Value = audioSampleRate;

                var audioChannels = _localSettings?.Values["AudioChannels"] as double? ?? 1.0;
                AudioChannelsNumberBox.Value = audioChannels;

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
                // Settings are automatically saved when changed
                await ShowInfoDialog(_resourceLoader.GetString("Info_SettingsSaved"));
            }
            catch (Exception ex)
            {
                await ShowErrorDialog(string.Format(_resourceLoader.GetString("Error_SaveSettings"), ex.Message));
            }
        }

        private async void ExportSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Export settings functionality - to be implemented
                await ShowInfoDialog(_resourceLoader.GetString("Info_ExportNotImplemented"));
            }
            catch (Exception ex)
            {
                await ShowErrorDialog(string.Format(_resourceLoader.GetString("Error_ExportSettings"), ex.Message));
            }
        }

        private async void ImportSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Import settings functionality - to be implemented
                await ShowInfoDialog(_resourceLoader.GetString("Info_ImportNotImplemented"));
            }
            catch (Exception ex)
            {
                await ShowErrorDialog(string.Format(_resourceLoader.GetString("Error_ImportSettings"), ex.Message));
            }
        }

        private async void ResetToDefaultsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Clear all settings
                _localSettings.Values.Clear();
                // Reload default settings
                await LoadSettingsAsync();

                await ShowInfoDialog(_resourceLoader.GetString("Info_SettingsReset"));
            }
            catch (Exception ex)
            {
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