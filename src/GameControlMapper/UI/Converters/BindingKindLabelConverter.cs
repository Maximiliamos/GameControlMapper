using System.Globalization;
using System.Windows.Data;
using GameControlMapper.Models;

namespace GameControlMapper.UI.Converters;

public sealed class BindingKindLabelConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        BindingKind.Tap => "Нажатие",
        BindingKind.Hold => "Удержание",
        BindingKind.DoubleTap => "Двойное нажатие",
        BindingKind.Swipe => "Свайп",
        BindingKind.Joystick => "Джойстик",
        BindingKind.Aim => "Управление камерой",
        BindingKind.Macro => "Макрос",
        BindingKind.Sequence => "Последовательность",
        BindingKind.MouseArea => "Кнопка мыши",
        _ => value?.ToString() ?? string.Empty
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => System.Windows.Data.Binding.DoNothing;
}
