using System.Globalization;
using System.Windows.Data;

namespace GameControlMapper.UI.Converters;

public sealed class FlexibleDoubleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is double number
            ? number.ToString("0.##", CultureInfo.GetCultureInfo("ru-RU"))
            : string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string text) return System.Windows.Data.Binding.DoNothing;
        var normalized = text.Trim().Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
               double.IsFinite(number)
            ? number
            : System.Windows.Data.Binding.DoNothing;
    }
}
