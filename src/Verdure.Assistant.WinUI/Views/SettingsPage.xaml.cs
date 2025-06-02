using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Verdure.Assistant.ViewModels;
using Windows.Storage.Pickers;
using Windows.Storage;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Verdure.Assistant.WinUI.Views;

public sealed partial class SettingsPage : Page
{
    private readonly ILogger<SettingsPage>? _logger;

    public SettingsPageViewModel ViewModel { get; } = null!;

    public SettingsPage()
    {
        this.InitializeComponent();        try
        {
            _logger = App.GetService<ILogger<SettingsPage>>();
            ViewModel = App.GetService<SettingsPageViewModel>() ?? CreateDefaultViewModel();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to get services: {ex.Message}");
            // Create a default ViewModel if service resolution fails
            ViewModel = CreateDefaultViewModel();
        }

        // Subscribe to ViewModel events
        if (ViewModel != null)
        {
            ViewModel.ExportSettingsRequested += OnExportSettingsRequested;
            ViewModel.ImportSettingsRequested += OnImportSettingsRequested;
            ViewModel.RefreshAudioDevicesRequested += OnRefreshAudioDevicesRequested;
            ViewModel.ThemeChangeRequested += OnThemeChangeRequested;
            ViewModel.SettingsError += OnSettingsError;
        }        _ = InitializeAsync();
    }    private SettingsPageViewModel CreateDefaultViewModel()
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
    }

    private async void OnExportSettingsRequested(object? sender, EventArgs e)
    {
        try
        {
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
                // Export settings logic would go here
                _logger?.LogInformation($"Exporting settings to: {file.Path}");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to export settings");
            //ViewModel.SettingsError?.Invoke(this, $"导出设置失败: {ex.Message}");
        }
    }

    private async void OnImportSettingsRequested(object? sender, EventArgs e)
    {
        try
        {
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
                // Import settings logic would go here
                _logger?.LogInformation($"Importing settings from: {file.Path}");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to import settings");
            //ViewModel.SettingsError?.Invoke(this, $"导入设置失败: {ex.Message}");
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
    }

    private void OnThemeChangeRequested(object? sender, string theme)
    {
        try
        {
            _logger?.LogInformation($"Theme change requested: {theme}");
            
            // Theme change logic would be handled by the app-level theme service
            // This event can be used to notify other parts of the application
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
}