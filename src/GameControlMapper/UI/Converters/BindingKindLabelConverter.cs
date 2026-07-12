using System.Globalization;
using System.Windows.Data;
using GameControlMapper.Models;
namespace GameControlMapper.UI.Converters;
public sealed class BindingKindLabelConverter:IValueConverter
{
 public object Convert(object value,Type targetType,object parameter,CultureInfo culture)=>value switch{BindingKind.Tap=>"Касание",BindingKind.Hold=>"Удержание",BindingKind.DoubleTap=>"Двойное касание",BindingKind.Swipe=>"Горизонтальный свайп",BindingKind.Joystick=>"Экранный джойстик",BindingKind.Aim=>"Управление камерой",BindingKind.Macro=>"Макрос (не поддерживается)",BindingKind.Sequence=>"Последовательность (не поддерживается)",BindingKind.MouseArea=>"Удержание кнопкой мыши",_=>value?.ToString()??string.Empty};
 public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>System.Windows.Data.Binding.DoNothing;
}
