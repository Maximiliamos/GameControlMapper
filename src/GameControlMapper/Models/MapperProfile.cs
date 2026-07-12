namespace GameControlMapper.Models;

/// <summary>
/// Serializable game control profile.
/// </summary>
public sealed class MapperProfile
{
    public string Name { get; set; } = "Default";
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

    public static MapperProfile CreateDefault(string name = "Default")
    {
        return new MapperProfile
        {
            Name = name,
            Game = name,
            Camera = new CameraSettings
            {
                ActivationHotkey = "LeftCtrl",
                UseMouseDrag = false,
                SensitivityX = 0.35,
                SensitivityY = 0.30,
                DeadZone = 1.5,
                Smooth = 0.7,
                MaxSpeed = 14,
                AnchorX = 960,
                AnchorY = 540,
                DragRadius = 120
            },
            Bindings =
            [
                new ControlBinding { Name = "Move", Hotkey = "WASD", X = 210, Y = 755, Width = 220, Height = 220, Kind = BindingKind.Joystick, Color = "#4CC9F0", UseNativeInput = false, Comment = "Touch joystick" },
                new ControlBinding { Name = "Camera", Hotkey = "LeftCtrl", X = 955, Y = 480, Width = 110, Height = 110, Kind = BindingKind.Aim, Color = "#8B5CF6", Opacity = 0.25, UseNativeInput = false, Comment = "Hold Ctrl and move mouse: touch drag camera" },
                new ControlBinding { Name = "Fire", Hotkey = "MouseLeft", X = 1585, Y = 628, Width = 130, Height = 130, Kind = BindingKind.MouseArea, Color = "#FF6B6B", UseNativeInput = false, Comment = "Touch fire button" },
                new ControlBinding { Name = "Aim", Hotkey = "MouseRight", X = 1645, Y = 790, Width = 125, Height = 125, Kind = BindingKind.MouseArea, Color = "#FFD166", UseNativeInput = false, Comment = "Touch aim button" },
                new ControlBinding { Name = "Shell 1", Hotkey = "1", X = 1828, Y = 454, Width = 76, Height = 76, Kind = BindingKind.Tap, Color = "#60A5FA" },
                new ControlBinding { Name = "Repair", Hotkey = "Q", X = 80, Y = 510, Width = 82, Height = 92, Kind = BindingKind.Tap, Color = "#4CC9F0" },
                new ControlBinding { Name = "Consumable 2", Hotkey = "E", X = 18, Y = 478, Width = 76, Height = 76, Kind = BindingKind.Tap, Color = "#38BDF8" },
                new ControlBinding { Name = "Consumable 3", Hotkey = "R", X = 18, Y = 568, Width = 76, Height = 76, Kind = BindingKind.Tap, Color = "#94A3B8" }
            ]
        };
    }
}
