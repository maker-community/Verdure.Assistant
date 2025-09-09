using Microsoft.Extensions.Logging;
using Verdure.Assistant.ViewModels;

namespace Verdure.Assistant.MAUI.Views;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsPageViewModel _viewModel;
    private readonly ILogger<SettingsPage>? _logger;

    public SettingsPage(SettingsPageViewModel viewModel, ILogger<SettingsPage>? logger = null)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _logger = logger;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        
        // 初始化ViewModel
        await _viewModel.InitializeAsync();
        
        _logger?.LogInformation("SettingsPage appeared and ViewModel initialized");
    }
}
