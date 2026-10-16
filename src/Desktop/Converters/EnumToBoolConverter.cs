using System;
using System.Globalization;
using Avalonia.Data.Converters;
using OmniConnect.Shared.Enums;

namespace OmniConnect.Desktop.Converters;

public class EnumToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        // Only show file tile when Type is explicitly File or Image
        if (value is MessageType msgType)
        {
            return msgType == MessageType.File || msgType == MessageType.Image;
        }
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value;
    }
}
