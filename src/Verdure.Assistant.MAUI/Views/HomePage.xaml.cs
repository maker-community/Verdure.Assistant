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
        
        // 订阅表情变化事件
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        
        // 初始化ViewModel
        await _viewModel.InitializeAsync();
        
        _logger?.LogInformation("HomePage appeared and ViewModel initialized");
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    #region 表情显示处理

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HomePageViewModel.CurrentEmotion))
        {
            UpdateEmotionDisplay();
        }
    }

    private void UpdateEmotionDisplay()
    {
        try
        {
            var emotion = _viewModel.CurrentEmotion;
            
            // 检查是否为GIF路径或文件名
            if (!string.IsNullOrEmpty(emotion) && (emotion.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) || 
                emotion.Contains("/") || IsEmotionGifFile(emotion)))
            {
                try
                {
                    // 显示GIF
                    string gifPath = emotion;
                    
                    // 如果只是表情名称，构建完整路径
                    if (!emotion.Contains("/") && !emotion.EndsWith(".gif", StringComparison.OrdinalIgnoreCase))
                    {
                        gifPath = $"{emotion.ToLower()}.gif";
                    }
                    
                    if (emotion.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        EmotionGifImage.Source = ImageSource.FromUri(new Uri(emotion));
                    }
                    else
                    {
                        // 对于本地GIF文件，使用FromFile方法
                        EmotionGifImage.Source = ImageSource.FromFile(gifPath);
                    }
                    EmotionGifImage.IsVisible = true;
                    EmotionTextLabel.IsVisible = false;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to load GIF emotion: {Emotion}", emotion);
                    // 如果加载GIF失败，回退到文字表情
                    EmotionGifImage.IsVisible = false;
                    EmotionTextLabel.IsVisible = true;
                }
            }
            else
            {
                // 显示文字表情
                EmotionGifImage.IsVisible = false;
                EmotionTextLabel.IsVisible = true;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error updating emotion display");
            // 出错时显示默认表情
            EmotionGifImage.IsVisible = false;
            EmotionTextLabel.IsVisible = true;
        }
    }

    private bool IsEmotionGifFile(string emotion)
    {
        // 检查是否为已知的表情名称
        var knownEmotions = new[] { "happy", "sad", "angry", "neutral", "thinking", "loving", "laughing", 
            "cool", "confused", "confident", "crying", "delicious", "embarrassed", "funny", "kissy", 
            "relaxed", "shocked", "silly", "sleepy", "winking" };
        
        return knownEmotions.Contains(emotion.ToLower());
    }

    #endregion
}
