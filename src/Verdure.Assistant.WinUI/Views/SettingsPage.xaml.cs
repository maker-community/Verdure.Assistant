using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Verdure.Assistant.ViewModels;
using Windows.Storage.Pickers;
using Windows.Storage;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using Verdure.Assistant.Core.Interfaces;
using Verdure.Assistant.Core.Models;
using Verdure.Assistant.WinUI.Services;
using System.Text.Json;
using System.IO;

namespace Verdure.Assistant.WinUI.Views;

public sealed partial class SettingsPage : Page
{
    private readonly ILogger<SettingsPage>? _logger;    
    public SettingsPageViewModel ViewModel { get; private set; } = null!;

    public SettingsPage()
    {
        this.InitializeComponent();
        
        try
        {
            _logger = App.GetService<ILogger<SettingsPage>>();
            ViewModel = App.GetService<SettingsPageViewModel>() ?? CreateDefaultViewModel();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to get services: {ex.Message}");
            // Create a default ViewModel if service resolution fails
            ViewModel = CreateDefaultViewModel();
        }        // Subscribe to ViewModel events
        if (ViewModel != null)
        {
            ViewModel.ExportSettingsRequested += OnExportSettingsRequested;
            ViewModel.ImportSettingsRequested += OnImportSettingsRequested;
            ViewModel.RefreshAudioDevicesRequested += OnRefreshAudioDevicesRequested;
            ViewModel.ThemeChangeRequested += OnThemeChangeRequested;
            ViewModel.SettingsError += OnSettingsError;
            ViewModel.SettingsSaved += OnSettingsSaved;
            ViewModel.SettingsReset += OnSettingsReset;
        }_ = InitializeAsync();
    }    
    
    private SettingsPageViewModel CreateDefaultViewModel()
    {
        // Try to get logger service for ViewModel, or create a minimal logger
        var viewModelLogger = App.GetService<ILogger<SettingsPageViewModel>>();
        
        // If we can't get the logger service, we need to create one or pass null carefully
        if (viewModelLogger == null)
        {
            // Create a simple console logger as fallback
            using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            viewModelLogger = loggerFactory.CreateLogger<SettingsPageViewModel>();
        }
        
        return new SettingsPageViewModel(viewModelLogger);
    }

    private async Task InitializeAsync()
    {
        try
        {
            await ViewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to initialize SettingsPage");
        }
    }    private async void OnExportSettingsRequested(object? sender, EventArgs e)
    {
        try
        {
            _logger?.LogInformation("Export settings requested");
            
            var settingsService = App.GetService<ISettingsService<AppSettings>>();
            if (settingsService != null)
            {
                // Get current settings
                var currentSettings = await settingsService.LoadSettingsAsync();
                if (currentSettings != null)
                {
                    // Use the settings service export functionality which handles file picker
                    bool success = await settingsService.ExportSettingsAsync("", currentSettings);
                      if (success)
                    {
                        _logger?.LogInformation("Settings exported successfully");
                        await ShowSuccessNotificationAsync("Settings exported successfully", "Your settings have been exported to file.");
                    }
                    else
                    {
                        _logger?.LogWarning("Settings export was cancelled or failed");
                    }
                }
                else
                {
                    _logger?.LogError("Failed to load current settings for export");
                }
            }
            else
            {
                _logger?.LogError("Settings service not found");
                
                // Fallback to manual file picker
                var savePicker = new FileSavePicker();
                savePicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
                savePicker.FileTypeChoices.Add("JSON files", new[] { ".json" });
                savePicker.SuggestedFileName = "VerdureAssistantSettings";

                // Get the current window handle
                var window = App.MainWindow;
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                WinRT.Interop.InitializeWithWindow.Initialize(savePicker, hwnd);

                var file = await savePicker.PickSaveFileAsync();
                if (file != null)
                {
                    _logger?.LogInformation($"Fallback: Exporting settings to: {file.Path}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to export settings");
        }
    }    
    
    private async void OnImportSettingsRequested(object? sender, EventArgs e)
    {
        try
        {
            _logger?.LogInformation("Import settings requested");

            // Fallback to manual file picker
            var openPicker = new FileOpenPicker();
            openPicker.ViewMode = PickerViewMode.List;
            openPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            openPicker.FileTypeFilter.Add(".json");

            // Get the current window handle
            var window = App.MainWindow;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
            WinRT.Interop.InitializeWithWindow.Initialize(openPicker, hwnd);
            var file = await openPicker.PickSingleFileAsync();
            if (file != null)
            {
                _logger?.LogInformation($"Selected file for import: {file.Path}");

                // Manual import logic implementation
                await ImportSettingsFromFileAsync(file.Path);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to import settings");     
        }
    }

    private void OnRefreshAudioDevicesRequested(object? sender, EventArgs e)
    {
        try
        {
            // Audio device refresh logic would go here
            _logger?.LogInformation("Refreshing audio devices");
            
            // For now, just simulate device discovery
            ViewModel.AudioInputDevice = "Default Microphone";
            ViewModel.AudioOutputDevice = "Default Speakers";
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to refresh audio devices");
            //ViewModel.SettingsError?.Invoke(this, $"刷新音频设备失败: {ex.Message}");
        }
    }    private void OnThemeChangeRequested(object? sender, string theme)
    {
        try
        {
            _logger?.LogInformation($"Theme change requested: {theme}");

            App.ChangeTheme(theme);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to change theme");
        }
    }

    private void OnSettingsError(object? sender, string error)
    {
        try
        {
            // Show error message to user (could use InfoBar or ContentDialog)
            _logger?.LogError($"Settings error: {error}");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to handle settings error");
        }
    }

    private async void OnSettingsSaved(object? sender, EventArgs e)
    {
        try
        {
            _logger?.LogInformation("Settings saved successfully");
            
            // Show success notification to user
            await ShowSuccessNotificationAsync("Settings saved successfully", "Your settings have been saved.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to handle settings saved event");
        }
    }

    private async void OnSettingsReset(object? sender, EventArgs e)
    {
        try
        {
            _logger?.LogInformation("Settings reset successfully");
            
            // Show success notification to user
            await ShowSuccessNotificationAsync("Settings reset", "Settings have been reset to defaults.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to handle settings reset event");
        }
    }

    private async Task ShowSuccessNotificationAsync(string title, string message)
    {
        try
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };

            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to show success notification");
        }
    }

    private async Task ImportSettingsFromFileAsync(string filePath)
    {
        try
        {
            _logger?.LogInformation($"Importing settings from file: {filePath}");
            
            if (!File.Exists(filePath))
            {
                _logger?.LogError($"Import file not found: {filePath}");
                return;
            }

            // Read and deserialize the JSON file
            var jsonContent = await File.ReadAllTextAsync(filePath);
            
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                AllowTrailingCommas = true,
                ReadCommentHandling = JsonCommentHandling.Skip
            };

            var importedSettings = JsonSerializer.Deserialize<AppSettings>(jsonContent, jsonOptions);
            
            if (importedSettings == null)
            {
                _logger?.LogError("Failed to deserialize settings from imported file");
                return;
            }

            // Apply the imported settings using the settings service
            var settingsService = App.GetService<ISettingsService<AppSettings>>();
            if (settingsService != null)
            {
                await settingsService.SaveSettingsAsync(importedSettings);
                _logger?.LogInformation("Imported settings saved successfully");
            }            
            // Reload the ViewModel with the imported settings
            await ViewModel.LoadSettingsCommand.ExecuteAsync(null);
            
            // Show success notification
            await ShowSuccessNotificationAsync("Settings imported successfully", "Your settings have been imported and applied.");
        }
        catch (JsonException ex)
        {
            _logger?.LogError(ex, "Failed to parse JSON from imported settings file");
            await ShowErrorNotificationAsync("Import Failed", "The selected file is not a valid settings file.");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to import settings from file");
            await ShowErrorNotificationAsync("Import Failed", $"Failed to import settings: {ex.Message}");
        }
    }

    private async Task ShowErrorNotificationAsync(string title, string message)
    {
        try
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = message,
                CloseButtonText = "OK",
                XamlRoot = this.XamlRoot
            };

            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to show error notification");
        }
    }
}