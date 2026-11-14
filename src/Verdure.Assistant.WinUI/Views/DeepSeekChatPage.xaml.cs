using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml.Input;
using Verdure.Assistant.ViewModels;
using Verdure.Assistant.WinUI.Services;
using Windows.System;

namespace Verdure.Assistant.WinUI.Views;

/// <summary>
/// DeepSeek 对话页：流式展示思维链/回答/工具调用，并提供 API 设置
/// </summary>
public sealed partial class DeepSeekChatPage : Page
{
    private readonly ILogger<DeepSeekChatPage>? _logger;
    private readonly WinUISpeechService? _speech;

    public DeepSeekChatViewModel ViewModel { get; }

    public DeepSeekChatPage()
    {
        ViewModel = App.GetService<DeepSeekChatViewModel>()
            ?? throw new InvalidOperationException("DeepSeekChatViewModel not registered");
        _logger = App.GetService<ILogger<DeepSeekChatPage>>();
        _speech = App.GetService<WinUISpeechService>();

        InitializeComponent();

        ViewModel.ScrollToBottomRequested += OnScrollToBottomRequested;
        ViewModel.SpeakRequested += OnSpeakRequested;
        ViewModel.StopSpeakingRequested += OnStopSpeakingRequested;

        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded; // 页面带导航缓存，只初始化一次
        try
        {
            await ViewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "DeepSeek 页面初始化失败");
            ViewModel.StatusText = $"初始化失败: {ex.Message}";
        }
    }

    /// <summary>
    /// 对话列最大 980 宽并水平居中（长文本更易阅读）；显式设置宽度以避免导航缓存页面里的偏移
    /// </summary>
    private void PageRoot_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width > 0)
        {
            Column.Width = Math.Min(980, e.NewSize.Width);
        }
    }

    private void InputBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        var shiftDown = Microsoft.UI.Input.InputKeyboardSource
            .GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        if (shiftDown)
        {
            return; // Shift+Enter 换行
        }

        e.Handled = true;
        if (ViewModel.SendCommand.CanExecute(null))
        {
            ViewModel.SendCommand.Execute(null);
        }
    }

    private void OnScrollToBottomRequested(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (ViewModel.Messages.Count > 0)
            {
                MessagesList.ScrollIntoView(ViewModel.Messages[^1]);
            }
        });
    }

    private async void OnSpeakRequested(object? sender, string text)
    {
        if (_speech != null)
        {
            await _speech.SpeakAsync(text);
        }
    }

    private void OnStopSpeakingRequested(object? sender, EventArgs e) => _speech?.Stop();

    private void RemoveAttachment_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: AttachedImageItem item })
        {
            ViewModel.RemoveAttachmentCommand.Execute(item);
        }
    }

    #region 图片附件：选择文件 / 粘贴

    private async void AttachButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileOpenPicker
            {
                ViewMode = Windows.Storage.Pickers.PickerViewMode.Thumbnail,
                SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.PicturesLibrary
            };
            foreach (var ext in new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp" })
            {
                picker.FileTypeFilter.Add(ext);
            }

            // 未打包/桌面应用必须把选择器关联到窗口句柄
            if (App.MainWindow != null)
            {
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow));
            }

            var files = await picker.PickMultipleFilesAsync();
            if (files == null)
            {
                return;
            }

            foreach (var file in files)
            {
                using var stream = await file.OpenReadAsync();
                var image = await ImageEncoding.FromStreamAsync(stream);
                ViewModel.AddImage(file.Name, image.Data);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "添加图片失败");
            ViewModel.StatusText = $"添加图片失败：{ex.Message}";
        }
    }

    private async void InputBox_Paste(object sender, TextControlPasteEventArgs e)
    {
        try
        {
            var content = Windows.ApplicationModel.DataTransfer.Clipboard.GetContent();
            if (!content.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.Bitmap))
            {
                return; // 普通文本粘贴走默认处理
            }

            e.Handled = true;
            var reference = await content.GetBitmapAsync();
            using var stream = await reference.OpenReadAsync();
            var image = await ImageEncoding.FromStreamAsync(stream);
            ViewModel.AddImage($"粘贴-{DateTime.Now:HHmmss}.jpg", image.Data);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "粘贴图片失败");
        }
    }

    #endregion
}
