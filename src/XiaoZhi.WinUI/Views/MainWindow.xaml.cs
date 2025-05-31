using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using Microsoft.Extensions.Logging;
using Microsoft.Windows.ApplicationModel.Resources;

namespace XiaoZhi.WinUI.Views;

/// <summary>
/// 主窗口 - Frame导航模式的主窗口
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly ILogger<MainWindow>? _logger;

    private readonly ResourceLoader _resourceLoader =new();

    public MainWindow()
    {
        this.InitializeComponent();
        _logger = App.GetService<ILogger<MainWindow>>();
        Title = _resourceLoader.GetString("AppDisplayName");
        // 导航到默认页面 (HomePage)
        ContentFrame.Navigate(typeof(HomePage));
    }

    private void MainNavigationView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem selectedItem)
        {
            var tag = selectedItem.Tag?.ToString();

            _logger?.LogInformation($"Navigating to: {tag}");

            Type? pageType = tag switch
            {
                "HomePage" => typeof(HomePage),
                "SettingsPage" => typeof(SettingsPage),
                _ => null
            };

            if (pageType != null && ContentFrame.CurrentSourcePageType != pageType)
            {
                ContentFrame.Navigate(pageType);
            }
        }
    }
}