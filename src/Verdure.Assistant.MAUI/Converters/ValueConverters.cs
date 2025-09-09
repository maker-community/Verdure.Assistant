using System.Globalization;

namespace Verdure.Assistant.MAUI.Converters;

/// <summary>
/// 布尔值转字符串转换器
/// </summary>
public class BoolToStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue && parameter is string paramStr)
        {
            var options = paramStr.Split('|');
            if (options.Length >= 2)
            {
                return boolValue ? options[0] : options[1];
            }
        }
        return value?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string stringValue && parameter is string paramStr)
        {
            var options = paramStr.Split('|');
            if (options.Length >= 2)
            {
                return stringValue == options[0];
            }
        }
        return false;
    }
}

/// <summary>
/// 字符串转布尔值转换器
/// </summary>
public class StringToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return !string.IsNullOrWhiteSpace(value?.ToString());
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 整数转布尔值反向转换器
/// </summary>
public class IntToBoolInverseConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intValue)
        {
            return intValue == 0;
        }
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 时间跨度转字符串转换器
/// </summary>
public class TimeSpanToStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double doubleValue)
        {
            var timeSpan = TimeSpan.FromSeconds(doubleValue);
            return timeSpan.ToString(@"mm\:ss");
        }
        return "00:00";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 音乐状态转播放/暂停按钮转换器
/// </summary>
public class MusicStatusToPlayPauseConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string status)
        {
            return status == "播放中" ? "⏸️" : "▶️";
        }
        return "▶️";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 布尔值转录音按钮文本转换器
/// </summary>
public class BoolToRecordingTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isRecording)
        {
            return isRecording ? "🔴" : "🎤";
        }
        return "🎤";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 布尔值转录音按钮背景转换器
/// </summary>
public class BoolToRecordingBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isRecording)
        {
            return isRecording ? Colors.Red : Colors.Gray;
        }
        return Colors.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 布尔值转录音按钮边框转换器
/// </summary>
public class BoolToRecordingBorderConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isRecording)
        {
            return isRecording ? Colors.DarkRed : Colors.DarkGray;
        }
        return Colors.DarkGray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 布尔值转服务状态文本转换器
/// </summary>
public class BoolToServiceStatusConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isConnected)
        {
            return isConnected ? "🔌 断开" : "🔌 连接";
        }
        return "🔌 连接";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 布尔值转服务按钮背景转换器
/// </summary>
public class BoolToServiceBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isConnected)
        {
            return isConnected ? Colors.Orange : Colors.Green;
        }
        return Colors.Green;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 布尔值转消息背景转换器
/// </summary>
public class BoolToMessageBackgroundConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isError)
        {
            return isError ? Color.FromArgb("#F8D7DA") : Color.FromArgb("#E3F2FD");
        }
        return Color.FromArgb("#E3F2FD");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 布尔值转消息文本颜色转换器
/// </summary>
public class BoolToMessageTextColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isError)
        {
            return isError ? Color.FromArgb("#721C24") : Color.FromArgb("#0D47A1");
        }
        return Color.FromArgb("#0D47A1");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
