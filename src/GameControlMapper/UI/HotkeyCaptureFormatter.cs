using System.Windows.Input;

namespace GameControlMapper.UI;

internal static class HotkeyCaptureFormatter
{
    internal static bool IsModifier(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or Key.LeftShift or Key.RightShift or
        Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin;

    internal static string FormatKey(Key key, ModifierKeys modifiers)
    {
        var parts = ModifierParts(modifiers);
        parts.Add(key.ToString());
        return string.Join('+', parts);
    }

    internal static string? FormatMouse(MouseButton button, ModifierKeys modifiers)
    {
        var key = button switch
        {
            MouseButton.Left => "MouseLeft",
            MouseButton.Right => "MouseRight",
            MouseButton.Middle => "MouseMiddle",
            _ => null
        };
        if (key is null) return null;
        var parts = ModifierParts(modifiers);
        parts.Add(key);
        return string.Join('+', parts);
    }

    internal static string ModifierPrompt(ModifierKeys modifiers)
    {
        var parts = ModifierParts(modifiers);
        return parts.Count == 0 ? "Нажмите клавишу…" : string.Join(" + ", parts) + " + …";
    }

    private static List<string> ModifierParts(ModifierKeys modifiers)
    {
        var parts = new List<string>();
        if ((modifiers & ModifierKeys.Control) != 0) parts.Add("Ctrl");
        if ((modifiers & ModifierKeys.Shift) != 0) parts.Add("Shift");
        if ((modifiers & ModifierKeys.Alt) != 0) parts.Add("Alt");
        if ((modifiers & ModifierKeys.Windows) != 0) parts.Add("Win");
        return parts;
    }
}
