using Microsoft.Extensions.Logging;
using Verdure.Assistant.ViewModels;

namespace Verdure.Assistant.MAUI.Views;

public partial class HomePage : ContentPage
{
    private readonly HomePageViewModel _viewModel;
    private readonly ILogger<HomePage>? _logger;

    public HomePage(HomePageViewModel viewModel, ILogger<HomePage>? logger = null)
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
        
        _logger?.LogInformation("HomePage appeared and ViewModel initialized");
    }

    #region 录音手势处理

    private async void OnRecordingPressed(object? sender, PointerEventArgs e)
    {
        try
        {
            await _viewModel.StartManualRecordingCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error starting manual recording");
        }
    }

    private async void OnRecordingReleased(object? sender, PointerEventArgs e)
    {
        try
        {
            await _viewModel.StopManualRecordingCommand.ExecuteAsync(null);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error stopping manual recording");
        }
    }

    #endregion
}
