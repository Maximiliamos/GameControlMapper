using System.Windows.Input;
using GameControlMapper.Win32;

namespace GameControlMapper.Services;

public sealed class HotkeyParser
{
    public bool Matches(string hotkey, int currentVirtualKey, IReadOnlySet<int> pressedKeys)
    {
        if (string.IsNullOrWhiteSpace(hotkey))
        {
            return false;
        }

        if (hotkey.Equals("WASD", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var required = hotkey
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ToVirtualKey)
            .Where(vk => vk != 0)
            .ToArray();

        if (required.Length == 0 || !required.Contains(currentVirtualKey))
        {
            return false;
        }

        return required.All(pressedKeys.Contains);
    }

    public int ToVirtualKey(string token)
    {
        return token.ToUpperInvariant() switch
        {
            "CTRL" or "CONTROL" => KeyInterop.VirtualKeyFromKey(Key.LeftCtrl),
            "SHIFT" => KeyInterop.VirtualKeyFromKey(Key.LeftShift),
            "ALT" => KeyInterop.VirtualKeyFromKey(Key.LeftAlt),
            "WIN" or "WINDOWS" => KeyInterop.VirtualKeyFromKey(Key.LWin),
            "MOUSELEFT" or "MOUSE1" => NativeMethods.VK_LBUTTON,
            "MOUSERIGHT" or "MOUSE2" => NativeMethods.VK_RBUTTON,
            "MOUSEMIDDLE" or "MOUSE3" => NativeMethods.VK_MBUTTON,
            "MOUSEX1" or "MOUSE4" or "XBUTTON1" => NativeMethods.VK_XBUTTON1,
            "MOUSEX2" or "MOUSE5" or "XBUTTON2" => NativeMethods.VK_XBUTTON2,
            _ => TryParseKey(token)
        };
    }

    private static int TryParseKey(string token)
    {
        try
        {
            var converted = new KeyConverter().ConvertFromInvariantString(token);
            if (converted is not Key key)
            {
                return 0;
            }

            return KeyInterop.VirtualKeyFromKey(key);
        }
        catch
        {
            return 0;
        }
    }
}
