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
            Game = "Tanks Blitz",
            Window = new GameWindowBinding
            {
                ProcessName = "tanksblitz",
                WindowTitle = "Tanks Blitz"
            },
            Camera = new CameraSettings
            {
                ActivationHotkey = "LeftCtrl",
                UseMouseDrag = false,
                SensitivityX = 1.0,
                SensitivityY = 1.0,
                DeadZone = 0,
                Smooth = 0,
                MaxSpeed = 64,
                AnchorX = 960,
                AnchorY = 540,
                DragRadius = 220
            },
            Bindings =
            [
                new ControlBinding { Name = "Движение", Hotkey = "WASD", X = 42, Y = 864, Width = 190, Height = 190, Kind = BindingKind.Joystick, Color = "#4CC9F0", Comment = "Экранный джойстик: W — вперёд, A/D — поворот, S — назад." },
                new ControlBinding { Name = "Камера", Hotkey = "LeftCtrl", X = 910, Y = 490, Width = 100, Height = 100, Kind = BindingKind.Aim, Color = "#8B5CF6", Opacity = 0.28, Comment = "Нажмите Ctrl для обзора камерой; нажмите Ctrl ещё раз, чтобы вернуть мышь." },
                new ControlBinding { Name = "Огонь", Hotkey = "MouseLeft", X = 1684, Y = 882, Width = 112, Height = 112, Kind = BindingKind.MouseArea, Color = "#FF6B6B", Comment = "Левая кнопка мыши удерживает правую экранную кнопку огня." },
                new ControlBinding { Name = "Прицел", Hotkey = "MouseRight", X = 1537, Y = 988, Width = 100, Height = 92, Kind = BindingKind.MouseArea, Color = "#FFD166", Comment = "Правая кнопка мыши удерживает экранную кнопку снайперского прицела." },
                new ControlBinding { Name = "Снаряд 1", Hotkey = "1", X = 1627, Y = 987, Width = 56, Height = 56, Kind = BindingKind.Tap, Color = "#60A5FA", Comment = "Первый тип снаряда в нижнем ряду." },
                new ControlBinding { Name = "Снаряд 2", Hotkey = "2", X = 1689, Y = 987, Width = 56, Height = 56, Kind = BindingKind.Tap, Color = "#60A5FA", Comment = "Второй тип снаряда в нижнем ряду." },
                new ControlBinding { Name = "Снаряд 3", Hotkey = "3", X = 1751, Y = 987, Width = 56, Height = 56, Kind = BindingKind.Tap, Color = "#60A5FA", Comment = "Третий тип снаряда в нижнем ряду." },
                new ControlBinding { Name = "Расходник Q", Hotkey = "Q", X = 1854, Y = 738, Width = 64, Height = 64, Kind = BindingKind.Tap, Color = "#4CC9F0", Comment = "Верхний расходник в правой колонке." },
                new ControlBinding { Name = "Расходник E", Hotkey = "E", X = 1854, Y = 804, Width = 64, Height = 64, Kind = BindingKind.Tap, Color = "#38BDF8", Comment = "Средний расходник в правой колонке." },
                new ControlBinding { Name = "Расходник R", Hotkey = "R", X = 1854, Y = 869, Width = 64, Height = 64, Kind = BindingKind.Tap, Color = "#94A3B8", Comment = "Нижний расходник в правой колонке." },
                new ControlBinding { Name = "Меню", Hotkey = "Escape", X = 1685, Y = 0, Width = 64, Height = 64, Kind = BindingKind.Tap, Color = "#94A3B8", Comment = "Шестерёнка меню в правом верхнем углу." },
                new ControlBinding { Name = "Действие", Hotkey = "Space", X = 1263, Y = 986, Width = 86, Height = 86, Kind = BindingKind.Tap, Color = "#34D399", Comment = "Контекстная кнопка действия рядом с нижней панелью." }
            ]
        };
    }
}
