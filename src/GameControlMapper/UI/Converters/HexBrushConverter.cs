using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace GameControlMapper.UI.Converters;

public sealed class HexBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value as string;
        try
        {
            return new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(text ?? "#4CC9F0"));
        }
        catch
        {
            return new SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 201, 240));
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SolidColorBrush brush)
        {
            return brush.Color.ToString();
        }

        return "#4CC9F0";
    }
}
