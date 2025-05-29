using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using Microsoft.Extensions.Logging;

namespace XiaoZhi.WinUI.Views;

/// <summary>
/// ������ - Frame����ģʽ��������
/// </summary>
public sealed partial class MainWindow : Window
{
    private readonly ILogger<MainWindow>? _logger;

    public MainWindow()
    {
        this.InitializeComponent();
        _logger = App.GetService<ILogger<MainWindow>>();

        // ������Ĭ��ҳ�� (HomePage)
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