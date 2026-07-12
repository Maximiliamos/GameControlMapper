namespace GameControlMapper.Models;

/// <summary>
/// Describes the action performed by a mapped control zone.
/// </summary>
public enum BindingKind
{
    Tap,
    Hold,
    DoubleTap,
    Swipe,
    Joystick,
    Aim,
    Macro,
    Sequence,
    MouseArea
}
