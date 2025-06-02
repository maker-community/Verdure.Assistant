using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Verdure.Assistant.WinUI.Converters;

/// <summary>
/// 布尔值到可见性转换器
/// 支持反向转换参数
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is bool boolValue)
        {
            // 如果参数是"True"，则反转逻辑
            bool isInverted = parameter?.ToString() == "True";
            bool shouldShow = isInverted ? !boolValue : boolValue;
            return shouldShow ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        if (value is Visibility visibility)
        {
            bool isVisible = visibility == Visibility.Visible;
            // 如果参数是"True"，则反转逻辑
            bool isInverted = parameter?.ToString() == "True";
            return isInverted ? !isVisible : isVisible;
        }
        return false;
    }
}
