namespace GameControlMapper.Models;

/// <summary>
/// Serializable game control profile.
/// </summary>
public sealed class MapperProfile
{
    public string Name { get; set; } = "Основной";
    public string Game { get; set; } = string.Empty;
    public int ResolutionWidth { get; set; } = 1920;
    public int ResolutionHeight { get; set; } = 1080;
    public InputMode InputMode { get; set; } = InputMode.SendInput;
    public double MouseSpeed { get; set; } = 1;
    public string EnableHotkey { get; set; } = "F8";
    public string DisableHotkey { get; set; } = "F9";
    public string ToggleOverlayHotkey { get; set; } = "F10";
    public string EditorHotkey { get; set; } = "F11";
    public CameraSettings Camera { get; set; } = new();
    public GamepadMappingSettings Gamepad { get; set; } = new();
    public GameWindowBinding Window { get; set; } = new();
    public List<ControlBinding> Bindings { get; set; } = [];

    public static MapperProfile CreateDefault(string name = "Основной")
    {
        return new MapperProfile
        {
            Name = name,
            Game = name,
            Camera = new CameraSettings
            {
                ActivationHotkey = "LeftCtrl",
                UseMouseDrag = false,
                SensitivityX = 0.50,
                SensitivityY = 0.50,
                DeadZone = 1.5,
                Smooth = 0.7,
                MaxSpeed = 14,
                AnchorX = 1011,
                AnchorY = 527,
                DragRadius = 140
            },
            Bindings =
            [
                new ControlBinding { Name = "Движение", Hotkey = "WASD", X = 179, Y = 778, Width = 175, Height = 175, Kind = BindingKind.Joystick, Color = "#4CC9F0", Comment = "W — вперёд, A/D — поворот, S — назад." },
                new ControlBinding { Name = "Камера", Hotkey = "LeftCtrl", X = 961, Y = 477, Width = 100, Height = 100, Kind = BindingKind.Aim, Color = "#8B5CF6", Opacity = 0.28, Comment = "Удерживайте Ctrl и двигайте мышь для обзора." },
                new ControlBinding { Name = "Огонь", Hotkey = "MouseLeft", X = 1610, Y = 632, Width = 100, Height = 100, Kind = BindingKind.MouseArea, Color = "#FF6B6B", Comment = "Левая кнопка мыши удерживает кнопку огня." },
                new ControlBinding { Name = "Прицел", Hotkey = "MouseRight", X = 1649, Y = 800, Width = 100, Height = 100, Kind = BindingKind.MouseArea, Color = "#FFD166", Comment = "Правая кнопка мыши удерживает кнопку прицела." },
                new ControlBinding { Name = "Снаряд 1", Hotkey = "1", X = 16, Y = 362, Width = 80, Height = 80, Kind = BindingKind.Tap, Color = "#60A5FA" },
                new ControlBinding { Name = "Снаряд 2", Hotkey = "2", X = 20, Y = 443, Width = 80, Height = 80, Kind = BindingKind.Tap, Color = "#60A5FA" },
                new ControlBinding { Name = "Снаряд 3", Hotkey = "3", X = 22, Y = 535, Width = 80, Height = 80, Kind = BindingKind.Tap, Color = "#60A5FA" },
                new ControlBinding { Name = "Ремкомплект", Hotkey = "Q", X = 1396, Y = 965, Width = 80, Height = 80, Kind = BindingKind.Tap, Color = "#4CC9F0" },
                new ControlBinding { Name = "Расходник E", Hotkey = "E", X = 15, Y = 606, Width = 80, Height = 80, Kind = BindingKind.Tap, Color = "#38BDF8" },
                new ControlBinding { Name = "Расходник R", Hotkey = "R", X = 1729, Y = 454, Width = 80, Height = 80, Kind = BindingKind.Tap, Color = "#94A3B8" },
                new ControlBinding { Name = "Меню", Hotkey = "Escape", X = 23, Y = 17, Width = 80, Height = 80, Kind = BindingKind.Tap, Color = "#94A3B8" },
                new ControlBinding { Name = "Действие", Hotkey = "Space", X = 1830, Y = 450, Width = 80, Height = 80, Kind = BindingKind.Tap, Color = "#34D399" }
            ]
        };
    }
}
