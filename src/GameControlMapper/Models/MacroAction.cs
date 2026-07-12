namespace GameControlMapper.Models;

/// <summary>
/// One step of a macro or sequence.
/// </summary>
public sealed class MacroAction
{
    public MacroActionKind Kind { get; set; } = MacroActionKind.Delay;
    public int DelayMilliseconds { get; set; } = 20;
    public string Key { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public string Text { get; set; } = string.Empty;

    public MacroAction Clone()
    {
        return new MacroAction
        {
            Kind = Kind,
            DelayMilliseconds = DelayMilliseconds,
            Key = Key,
            X = X,
            Y = Y,
            Text = Text
        };
    }
}
