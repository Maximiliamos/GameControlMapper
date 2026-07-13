using System.Collections.ObjectModel;
using System.ComponentModel;
using GameControlMapper.Models;

namespace GameControlMapper.ViewModels;

public sealed record ControlPaletteItem(
    BindingKind Kind,
    string Icon,
    string Title,
    string Description);

/// <summary>
/// Isolated draft used by the in-game overlay editor. The live profile is not
/// modified until the caller explicitly exports and commits this draft.
/// </summary>
public sealed class ControlEditorSession : ObservableObject
{
    private const double MinimumSize = 24;
    private readonly string _cameraHotkey;
    private BindingViewModel? _selectedBinding;
    private double _overlayDimOpacity = 0.38;
    private bool _isDirty;

    public ControlEditorSession(MapperProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.ResolutionWidth <= 0 || profile.ResolutionHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(profile), "Размер профиля должен быть положительным.");
        }

        ProfileName = profile.Name;
        ProfileWidth = profile.ResolutionWidth;
        ProfileHeight = profile.ResolutionHeight;
        _cameraHotkey = string.IsNullOrWhiteSpace(profile.Camera.ActivationHotkey)
            ? "LeftCtrl"
            : profile.Camera.ActivationHotkey;

        foreach (var model in profile.Bindings.Select(CopyExact))
        {
            AddViewModel(model);
        }
    }

    public static IReadOnlyList<ControlPaletteItem> Palette { get; } =
    [
        new(BindingKind.Tap, "●", "Касание", "Одно короткое нажатие в выбранной точке"),
        new(BindingKind.Hold, "◎", "Удержание", "Удерживать касание заданное время"),
        new(BindingKind.DoubleTap, "◉", "Двойное касание", "Два быстрых касания подряд"),
        new(BindingKind.Swipe, "↔", "Свайп", "Горизонтальный жест слева направо"),
        new(BindingKind.Joystick, "✣", "Движение WASD", "Экранный джойстик для движения"),
        new(BindingKind.Aim, "⌖", "Обзор камерой", "Ctrl включает обзор, повторный Ctrl возвращает мышь"),
        new(BindingKind.MouseArea, "◫", "Кнопка мыши", "Удерживаемое касание по кнопке мыши")
    ];

    public string ProfileName { get; }
    public double ProfileWidth { get; }
    public double ProfileHeight { get; }
    public ObservableCollection<BindingViewModel> Bindings { get; } = [];

    public BindingViewModel? SelectedBinding
    {
        get => _selectedBinding;
        set
        {
            if (ReferenceEquals(_selectedBinding, value))
            {
                return;
            }

            if (_selectedBinding is not null)
            {
                _selectedBinding.IsSelected = false;
            }

            _selectedBinding = value;
            if (_selectedBinding is not null)
            {
                _selectedBinding.IsSelected = true;
            }

            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelection));
            OnPropertyChanged(nameof(CanDeleteSelection));
            OnPropertyChanged(nameof(CanDuplicateSelection));
        }
    }

    public bool HasSelection => SelectedBinding is not null;
    public bool CanDeleteSelection => SelectedBinding is not null &&
                                      (SelectedBinding.Kind != BindingKind.Aim ||
                                       Bindings.Count(binding => binding.Kind == BindingKind.Aim) > 1);
    public bool CanDuplicateSelection => SelectedBinding is not null &&
                                         SelectedBinding.Kind is not (
                                             BindingKind.Aim or
                                             BindingKind.Joystick or
                                             BindingKind.Macro or
                                             BindingKind.Sequence);

    public double OverlayDimOpacity
    {
        get => _overlayDimOpacity;
        set => SetProperty(ref _overlayDimOpacity, Math.Round(Math.Clamp(value, 0.05, 0.72), 2));
    }

    public bool IsDirty
    {
        get => _isDirty;
        private set => SetProperty(ref _isDirty, value);
    }

    public BindingViewModel AddBinding(BindingKind kind, double centerX, double centerY)
    {
        if (!Palette.Any(item => item.Kind == kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), "Этот тип управления нельзя создать в редакторе.");
        }

        if (kind is BindingKind.Aim or BindingKind.Joystick)
        {
            var existingSingleton = Bindings.FirstOrDefault(binding => binding.Kind == kind);
            if (existingSingleton is not null)
            {
                SelectedBinding = existingSingleton;
                return existingSingleton;
            }
        }

        var (width, height) = DefaultSize(kind);
        var paletteItem = Palette.First(item => item.Kind == kind);
        var model = new ControlBinding
        {
            Kind = kind,
            Name = paletteItem.Title,
            Hotkey = DefaultHotkey(kind),
            Width = width,
            Height = height,
            X = centerX - width / 2d,
            Y = centerY - height / 2d,
            Color = DefaultColor(kind),
            Opacity = kind == BindingKind.Aim ? 0.28 : 0.58,
            HoldMilliseconds = kind switch
            {
                BindingKind.Hold => 250,
                BindingKind.Swipe => 160,
                _ => 35
            },
            Comment = paletteItem.Description
        };

        Clamp(model);
        var binding = AddViewModel(model);
        SelectedBinding = binding;
        IsDirty = true;
        return binding;
    }

    public void Move(BindingViewModel binding, double deltaX, double deltaY)
    {
        ArgumentNullException.ThrowIfNull(binding);
        binding.X += deltaX;
        binding.Y += deltaY;
        Clamp(binding.Model);
        NotifyGeometry(binding);
        IsDirty = true;
    }

    public void Resize(BindingViewModel binding, double deltaWidth, double deltaHeight)
    {
        ArgumentNullException.ThrowIfNull(binding);
        binding.Width += deltaWidth;
        binding.Height += deltaHeight;
        Clamp(binding.Model);
        NotifyGeometry(binding);
        IsDirty = true;
    }

    public bool DeleteSelected()
    {
        if (!CanDeleteSelection || SelectedBinding is null)
        {
            return false;
        }

        var index = Bindings.IndexOf(SelectedBinding);
        SelectedBinding.PropertyChanged -= OnBindingPropertyChanged;
        Bindings.Remove(SelectedBinding);
        SelectedBinding = Bindings.Count == 0
            ? null
            : Bindings[Math.Clamp(index, 0, Bindings.Count - 1)];
        IsDirty = true;
        return true;
    }

    public BindingViewModel? DuplicateSelected()
    {
        if (!CanDuplicateSelection || SelectedBinding is null)
        {
            return null;
        }

        var model = CopyExact(SelectedBinding.Model);
        model.Id = Guid.NewGuid();
        model.Name = $"{model.Name} — копия";
        model.X += 24;
        model.Y += 24;
        Clamp(model);
        var duplicate = AddViewModel(model);
        SelectedBinding = duplicate;
        IsDirty = true;
        return duplicate;
    }

    public IReadOnlyList<ControlBinding> ExportBindings()
    {
        foreach (var binding in Bindings)
        {
            Clamp(binding.Model);
            binding.RefreshGeometry();
        }

        return Bindings.Select(binding => CopyExact(binding.Model)).ToArray();
    }

    public static ControlBinding CopyExact(ControlBinding source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new ControlBinding
        {
            Id = source.Id,
            Name = source.Name,
            Kind = source.Kind,
            Hotkey = source.Hotkey,
            X = source.X,
            Y = source.Y,
            Width = source.Width,
            Height = source.Height,
            Color = source.Color,
            Opacity = source.Opacity,
            HoldMilliseconds = source.HoldMilliseconds,
            DelayMilliseconds = source.DelayMilliseconds,
            Repeat = source.Repeat,
            Priority = source.Priority,
            Comment = source.Comment,
            IsActive = source.IsActive,
            UseNativeInput = source.UseNativeInput,
            Actions = source.Actions.Select(action => action.Clone()).ToList()
        };
    }

    public static MapperProfile CopyProfileWithBindings(
        MapperProfile source,
        IEnumerable<ControlBinding> bindings)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(bindings);

        return new MapperProfile
        {
            Name = source.Name,
            Game = source.Game,
            ResolutionWidth = source.ResolutionWidth,
            ResolutionHeight = source.ResolutionHeight,
            InputMode = source.InputMode,
            MouseSpeed = source.MouseSpeed,
            EnableHotkey = source.EnableHotkey,
            DisableHotkey = source.DisableHotkey,
            ToggleOverlayHotkey = source.ToggleOverlayHotkey,
            EditorHotkey = source.EditorHotkey,
            Camera = new CameraSettings
            {
                ActivationHotkey = source.Camera.ActivationHotkey,
                AnchorX = source.Camera.AnchorX,
                AnchorY = source.Camera.AnchorY,
                DragRadius = source.Camera.DragRadius,
                UseMouseDrag = source.Camera.UseMouseDrag,
                SensitivityX = source.Camera.SensitivityX,
                SensitivityY = source.Camera.SensitivityY,
                InvertX = source.Camera.InvertX,
                InvertY = source.Camera.InvertY,
                Acceleration = source.Camera.Acceleration,
                DeadZone = source.Camera.DeadZone,
                Smooth = source.Camera.Smooth,
                MaxSpeed = source.Camera.MaxSpeed
            },
            Gamepad = new GamepadMappingSettings
            {
                Enabled = source.Gamepad.Enabled,
                LeftStickDeadZone = source.Gamepad.LeftStickDeadZone,
                RightStickDeadZone = source.Gamepad.RightStickDeadZone,
                MouseSensitivityX = source.Gamepad.MouseSensitivityX,
                MouseSensitivityY = source.Gamepad.MouseSensitivityY,
                MoveForwardKey = source.Gamepad.MoveForwardKey,
                MoveBackKey = source.Gamepad.MoveBackKey,
                MoveLeftKey = source.Gamepad.MoveLeftKey,
                MoveRightKey = source.Gamepad.MoveRightKey,
                RepairKey = source.Gamepad.RepairKey,
                Consumable2Key = source.Gamepad.Consumable2Key,
                Consumable3Key = source.Gamepad.Consumable3Key,
                Shell1Key = source.Gamepad.Shell1Key,
                Shell2Key = source.Gamepad.Shell2Key,
                Shell3Key = source.Gamepad.Shell3Key
            },
            Window = new GameWindowBinding
            {
                ProcessName = source.Window.ProcessName,
                WindowTitle = source.Window.WindowTitle,
                WindowHandle = source.Window.WindowHandle,
                X = source.Window.X,
                Y = source.Window.Y,
                Width = source.Window.Width,
                Height = source.Window.Height,
                ScaleX = source.Window.ScaleX,
                ScaleY = source.Window.ScaleY
            },
            Bindings = bindings.Select(CopyExact).ToList()
        };
    }

    private BindingViewModel AddViewModel(ControlBinding model)
    {
        var binding = new BindingViewModel(model);
        binding.PropertyChanged += OnBindingPropertyChanged;
        Bindings.Add(binding);
        return binding;
    }

    private void OnBindingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(BindingViewModel.IsSelected))
        {
            IsDirty = true;
        }
    }

    private void Clamp(ControlBinding model)
    {
        var horizontalInset = model.Kind == BindingKind.Swipe ? 0.01 : 0;
        var maximumProfileWidth = Math.Max(0.01, ProfileWidth - horizontalInset);
        var minimumWidth = Math.Min(MinimumSize, maximumProfileWidth);
        var minimumHeight = Math.Min(MinimumSize, ProfileHeight);
        model.Width = Math.Clamp(FiniteOr(model.Width, minimumWidth), minimumWidth, maximumProfileWidth);
        model.Height = Math.Clamp(FiniteOr(model.Height, minimumHeight), minimumHeight, ProfileHeight);

        var maxX = Math.Max(0, ProfileWidth - model.Width - horizontalInset);
        var maxY = Math.Max(0, ProfileHeight - model.Height);
        model.X = Math.Clamp(FiniteOr(model.X, 0), 0, maxX);
        model.Y = Math.Clamp(FiniteOr(model.Y, 0), 0, maxY);

        model.X = Math.Round(model.X, 2);
        model.Y = Math.Round(model.Y, 2);
        model.Width = Math.Round(model.Width, 2);
        model.Height = Math.Round(model.Height, 2);

        if (model.Kind == BindingKind.Swipe && model.X + model.Width >= ProfileWidth)
        {
            model.Width = Math.Max(
                0.01,
                Math.Floor((ProfileWidth - model.X - 0.01) * 100) / 100d);
        }
    }

    private static double FiniteOr(double value, double fallback) => double.IsFinite(value) ? value : fallback;

    private static (double Width, double Height) DefaultSize(BindingKind kind) => kind switch
    {
        BindingKind.Joystick => (176, 176),
        BindingKind.Swipe => (190, 72),
        BindingKind.Aim => (112, 112),
        BindingKind.MouseArea => (104, 104),
        _ => (84, 84)
    };

    private string DefaultHotkey(BindingKind kind) => kind switch
    {
        BindingKind.Joystick => "WASD",
        BindingKind.Aim => _cameraHotkey,
        BindingKind.MouseArea => "MouseLeft",
        _ => "Q"
    };

    private static string DefaultColor(BindingKind kind) => kind switch
    {
        BindingKind.Joystick => "#38BDF8",
        BindingKind.Aim => "#A78BFA",
        BindingKind.MouseArea => "#FB7185",
        BindingKind.Swipe => "#34D399",
        BindingKind.Hold => "#FBBF24",
        BindingKind.DoubleTap => "#22D3EE",
        _ => "#4CC9F0"
    };

    private static void NotifyGeometry(BindingViewModel binding)
    {
        binding.RefreshGeometry();
    }
}
