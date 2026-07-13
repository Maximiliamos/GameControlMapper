using System.Runtime.CompilerServices;
using GameControlMapper.Models;

namespace GameControlMapper.ViewModels;

public sealed class BindingViewModel : ObservableObject
{
    private readonly ControlBinding _model;
    private bool _isSelected;

    public BindingViewModel(ControlBinding model)
    {
        _model = model;
    }

    public ControlBinding Model => _model;

    /// <summary>
    /// Visual selection state used by the interactive profile editors. It is not persisted.
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    internal void RefreshGeometry()
    {
        OnPropertyChanged(nameof(X));
        OnPropertyChanged(nameof(Y));
        OnPropertyChanged(nameof(Width));
        OnPropertyChanged(nameof(Height));
    }

    public string Name
    {
        get => _model.Name;
        set
        {
            if (_model.Name != value)
            {
                _model.Name = value;
                OnPropertyChanged();
            }
        }
    }

    public BindingKind Kind
    {
        get => _model.Kind;
        set
        {
            if (_model.Kind != value)
            {
                _model.Kind = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsHotkeyReadOnly));
                OnPropertyChanged(nameof(CanEditHotkey));
                OnPropertyChanged(nameof(UsesDuration));
                OnPropertyChanged(nameof(CanToggleActive));
            }
        }
    }

    public bool IsHotkeyReadOnly => Kind == BindingKind.Joystick;
    public bool CanEditHotkey => !IsHotkeyReadOnly;
    public bool UsesDuration => Kind is BindingKind.Hold or BindingKind.Swipe;
    public bool CanToggleActive => Kind != BindingKind.Aim;

    public string Hotkey
    {
        get => _model.Hotkey;
        set
        {
            if (_model.Hotkey != value)
            {
                _model.Hotkey = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayHotkey));
            }
        }
    }

    public string DisplayHotkey => Hotkey.ToUpperInvariant() switch
    {
        "MOUSELEFT" or "MOUSE1" => "ЛКМ",
        "MOUSERIGHT" or "MOUSE2" => "ПКМ",
        "MOUSEMIDDLE" or "MOUSE3" => "СКМ",
        "MOUSEX1" or "MOUSE4" or "XBUTTON1" => "Бок. 1",
        "MOUSEX2" or "MOUSE5" or "XBUTTON2" => "Бок. 2",
        "LEFTCTRL" or "RIGHTCTRL" or "CTRL" or "CONTROL" => "Ctrl",
        "ESCAPE" => "Esc",
        _ => Hotkey
    };

    public double X
    {
        get => _model.X;
        set => SetNumber(value, _model.X, newValue => _model.X = newValue);
    }

    public double Y
    {
        get => _model.Y;
        set => SetNumber(value, _model.Y, newValue => _model.Y = newValue);
    }

    public double Width
    {
        get => _model.Width;
        set => SetNumber(Math.Max(16, value), _model.Width, newValue => _model.Width = newValue);
    }

    public double Height
    {
        get => _model.Height;
        set => SetNumber(Math.Max(16, value), _model.Height, newValue => _model.Height = newValue);
    }

    public string Color
    {
        get => _model.Color;
        set
        {
            if (_model.Color != value)
            {
                _model.Color = value;
                OnPropertyChanged();
            }
        }
    }

    public double Opacity
    {
        get => _model.Opacity;
        set => SetNumber(Math.Clamp(value, 0.05, 1), _model.Opacity, newValue => _model.Opacity = newValue);
    }

    public int HoldMilliseconds
    {
        get => _model.HoldMilliseconds;
        set
        {
            if (_model.HoldMilliseconds != value)
            {
                _model.HoldMilliseconds = Math.Max(0, value);
                OnPropertyChanged();
            }
        }
    }

    public int DelayMilliseconds
    {
        get => _model.DelayMilliseconds;
        set
        {
            if (_model.DelayMilliseconds != value)
            {
                _model.DelayMilliseconds = Math.Max(0, value);
                OnPropertyChanged();
            }
        }
    }

    public bool Repeat
    {
        get => _model.Repeat;
        set
        {
            if (_model.Repeat != value)
            {
                _model.Repeat = value;
                OnPropertyChanged();
            }
        }
    }

    public int Priority
    {
        get => _model.Priority;
        set
        {
            if (_model.Priority != value)
            {
                _model.Priority = value;
                OnPropertyChanged();
            }
        }
    }

    public string Comment
    {
        get => _model.Comment;
        set
        {
            if (_model.Comment != value)
            {
                _model.Comment = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsActive
    {
        get => _model.IsActive;
        set
        {
            if (_model.IsActive != value)
            {
                _model.IsActive = value;
                OnPropertyChanged();
            }
        }
    }

    public bool UseNativeInput
    {
        get => _model.UseNativeInput;
        set
        {
            if (_model.UseNativeInput != value)
            {
                _model.UseNativeInput = value;
                OnPropertyChanged();
            }
        }
    }

    private void SetNumber(double value, double current, Action<double> assign, [CallerMemberName] string? propertyName = null)
    {
        value = Math.Round(value, 2);
        if (Math.Abs(value - current) < 0.01)
        {
            return;
        }

        assign(value);
        OnPropertyChanged(propertyName);
    }
}
