namespace GameControlMapper.Models;

/// <summary>
/// Action type used by macros and sequences.
/// </summary>
public enum MacroActionKind
{
    Delay,
    MouseDown,
    MouseUp,
    Click,
    Move,
    KeyDown,
    KeyUp,
    Text
}
