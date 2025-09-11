using Microsoft.Extensions.Logging;
using Verdure.Assistant.ViewModels;
using Verdure.Assistant.MAUI.Services;

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
        
        // 订阅GIF渲染事件
        MauiGifEmotionRenderer.GifRenderRequested += OnGifRenderRequested;
        MauiGifEmotionRenderer.GifRenderStopped += OnGifRenderStopped;
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
        
        // 取消订阅GIF渲染事件
        MauiGifEmotionRenderer.GifRenderRequested -= OnGifRenderRequested;
        MauiGifEmotionRenderer.GifRenderStopped -= OnGifRenderStopped;
    }

    #region 表情显示处理

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HomePageViewModel.CurrentEmotion))
        {
            UpdateEmotionDisplay();
        }
    }

    private void OnGifRenderRequested(object? sender, MauiGifRenderEventArgs e)
    {
        try
        {
            // 在主线程上更新UI
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    _logger?.LogDebug("Displaying GIF emotion: {EmotionType} -> {GifPath}", e.EmotionType, e.GifPath);
                    
                    // 设置GIF图片源
                    EmotionGifImage.Source = ImageSource.FromFile(e.GifPath);
                    EmotionGifImage.IsVisible = true;
                    EmotionTextLabel.IsVisible = false;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to display GIF emotion: {EmotionType}", e.EmotionType);
                    // 如果GIF加载失败，回退到文字表情
                    ShowEmotionFallback(e.EmotionType);
                }
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in GIF render requested handler");
        }
    }

    private void OnGifRenderStopped(object? sender, EventArgs e)
    {
        try
        {
            // 在主线程上更新UI
            MainThread.BeginInvokeOnMainThread(() =>
            {
                EmotionGifImage.IsVisible = false;
                EmotionTextLabel.IsVisible = true;
                _logger?.LogDebug("GIF emotion display stopped");
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error in GIF render stopped handler");
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
                    // 显示GIF - 优先使用新的渲染系统通过事件处理
                    // 如果没有通过事件系统处理，则使用旧的直接设置方法
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
                    
                    _logger?.LogDebug("Updated emotion display with GIF: {GifPath}", gifPath);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to load GIF emotion: {Emotion}", emotion);
                    // 如果加载GIF失败，回退到文字表情
                    ShowEmotionFallback(emotion);
                }
            }
            else
            {
                // 显示文字表情
                EmotionGifImage.IsVisible = false;
                EmotionTextLabel.IsVisible = true;
                _logger?.LogDebug("Updated emotion display with text: {Emotion}", emotion);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error updating emotion display");
            // 出错时显示默认表情
            ShowEmotionFallback("😊");
        }
    }

    private void ShowEmotionFallback(string emotion)
    {
        EmotionGifImage.IsVisible = false;
        EmotionTextLabel.IsVisible = true;
        
        // 如果是表情名称，转换为emoji
        if (IsEmotionGifFile(emotion))
        {
            var emoji = GetEmotionEmoji(emotion);
            if (!string.IsNullOrEmpty(emoji))
            {
                EmotionTextLabel.Text = emoji;
            }
        }
        
        _logger?.LogDebug("Showing emotion fallback: {Emotion}", emotion);
    }

    private string GetEmotionEmoji(string emotionType)
    {
        return emotionType.ToLowerInvariant() switch
        {
            "neutral" => "😊",
            "happy" => "😄",
            "sad" => "😢",
            "angry" => "😠",
            "surprised" => "😲",
            "confused" => "😕",
            "thinking" => "🤔",
            "speaking" => "🗣️",
            "listening" => "👂",
            "laughing" => "😂",
            "loving" => "😍",
            "embarrassed" => "😳",
            "shocked" => "😱",
            "winking" => "😉",
            "cool" => "😎",
            "relaxed" => "😌",
            "sleepy" => "😴",
            "silly" => "🤪",
            "confident" => "😎",
            "crying" => "😭",
            "delicious" => "😋",
            "funny" => "🤣",
            "kissy" => "😘",
            _ => "😊"
        };
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
