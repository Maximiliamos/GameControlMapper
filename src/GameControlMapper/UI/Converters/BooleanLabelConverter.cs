using System.Globalization;
using System.Windows.Data;
namespace GameControlMapper.UI.Converters;
public sealed class BooleanLabelConverter:IValueConverter
{
 public object Convert(object value,Type targetType,object parameter,CultureInfo culture){if(parameter is string labels){var p=labels.Trim('\'','"').Split('|');if(p.Length==2)return value is true?p[0]:p[1];}return value is true?"Управление включено":"Управление остановлено";}
 public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>System.Windows.Data.Binding.DoNothing;
}
