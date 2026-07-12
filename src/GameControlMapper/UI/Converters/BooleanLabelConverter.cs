using System.Globalization;
using System.Windows.Data;

namespace GameControlMapper.UI.Converters;

public sealed class BooleanLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (parameter is string labels)
        {
            var parts = labels.Trim('\'', '"').Split('|');
            if (parts.Length == 2) return value is true ? parts[0] : parts[1];
        }
        return value is true ? "Активно" : "Остановлено";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return System.Windows.Data.Binding.DoNothing;
    }
}
