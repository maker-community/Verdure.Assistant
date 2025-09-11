using Microsoft.Extensions.Logging;
using Verdure.Assistant.ViewModels;
using Verdure.Assistant.MAUI.Services;

namespace Verdure.Assistant.MAUI.Views;

    public partial class HomePage : ContentPage
    {
        private readonly HomePageViewModel _viewModel;
        private readonly ILogger<HomePage>? _logger;

        // 表情映射字典
        private readonly Dictionary<string, string> _emotionGifMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["neutral"] = "Emotions/neutral.gif",
            ["happy"] = "Emotions/happy.gif",
            ["sad"] = "Emotions/sad.gif",
            ["angry"] = "Emotions/angry.gif",
            ["surprised"] = "Emotions/surprised.gif",
            ["confused"] = "Emotions/confused.gif",
            ["thinking"] = "Emotions/thinking.gif",
            ["speaking"] = "Emotions/happy.gif", // 映射到happy
            ["listening"] = "Emotions/thinking.gif", // 映射到thinking
            ["laughing"] = "Emotions/laughing.gif",
            ["loving"] = "Emotions/loving.gif",
            ["embarrassed"] = "Emotions/embarrassed.gif",
            ["shocked"] = "Emotions/shocked.gif",
            ["winking"] = "Emotions/winking.gif",
            ["cool"] = "Emotions/cool.gif",
            ["relaxed"] = "Emotions/relaxed.gif",
            ["sleepy"] = "Emotions/sleepy.gif",
            ["silly"] = "Emotions/silly.gif",
            ["confident"] = "Emotions/confident.gif",
            ["crying"] = "Emotions/crying.gif",
            ["delicious"] = "Emotions/delicious.gif",
            ["funny"] = "Emotions/funny.gif",
            ["kissy"] = "Emotions/kissy.gif"
        };
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
                    
                    // 直接使用GIF路径
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
            
            if (string.IsNullOrEmpty(emotion))
            {
                emotion = "neutral";
            }

            // 查找表情对应的GIF路径
            if (_emotionGifMapping.TryGetValue(emotion, out var gifPath))
            {
                try
                {
                    // 显示GIF
                    EmotionGifImage.Source = ImageSource.FromFile(gifPath);
                    EmotionGifImage.IsVisible = true;
                    EmotionTextLabel.IsVisible = false;
                    
                    _logger?.LogDebug("Updated emotion display with GIF: {Emotion} -> {GifPath}", emotion, gifPath);
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
                // 没有找到对应的GIF，显示文字表情
                ShowEmotionFallback(emotion);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Error updating emotion display");
            // 出错时显示默认表情
            ShowEmotionFallback("neutral");
        }
    }

    private void ShowEmotionFallback(string emotion)
    {
        EmotionGifImage.IsVisible = false;
        EmotionTextLabel.IsVisible = true;
        
        // 转换为emoji
        var emoji = GetEmotionEmoji(emotion);
        EmotionTextLabel.Text = emoji;
        
        _logger?.LogDebug("Showing emotion fallback: {Emotion} -> {Emoji}", emotion, emoji);
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

    #endregion
}
