using System.Text.Json.Serialization;

namespace GameControlMapper.Models;

/// <summary>
/// One visual control zone in a profile.
/// </summary>
public sealed class ControlBinding
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New Tap";
    public BindingKind Kind { get; set; } = BindingKind.Tap;
    public string Hotkey { get; set; } = "Q";
    public double X { get; set; } = 400;
    public double Y { get; set; } = 300;
    public double Width { get; set; } = 90;
    public double Height { get; set; } = 90;
    public string Color { get; set; } = "#4CC9F0";
    public double Opacity { get; set; } = 0.38;
    public int HoldMilliseconds { get; set; } = 35;
    public int DelayMilliseconds { get; set; }
    public bool Repeat { get; set; }
    public int Priority { get; set; }
    public string Comment { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public bool UseNativeInput { get; set; }
    public List<MacroAction> Actions { get; set; } = [];

    [JsonIgnore]
    public double CenterX => X + Width / 2d;

    [JsonIgnore]
    public double CenterY => Y + Height / 2d;

    public ControlBinding Clone()
    {
        return new ControlBinding
        {
            Id = Guid.NewGuid(),
            Name = $"{Name} Copy",
            Kind = Kind,
            Hotkey = Hotkey,
            X = X + 24,
            Y = Y + 24,
            Width = Width,
            Height = Height,
            Color = Color,
            Opacity = Opacity,
            HoldMilliseconds = HoldMilliseconds,
            DelayMilliseconds = DelayMilliseconds,
            Repeat = Repeat,
            Priority = Priority,
            Comment = Comment,
            IsActive = IsActive,
            UseNativeInput = UseNativeInput,
            Actions = Actions.Select(action => action.Clone()).ToList()
        };
    }
}
