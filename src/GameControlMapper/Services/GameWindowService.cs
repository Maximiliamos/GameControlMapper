using System.Diagnostics;
using System.Runtime.InteropServices;
using GameControlMapper.Win32;

namespace GameControlMapper.Services;

public sealed class GameWindowService
{
    public IReadOnlyList<GameWindowInfo> FindWindows()
    {
        var windows = new List<GameWindowInfo>();
        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd))
            {
                return true;
            }

            var buffer = new char[512];
            var length = NativeMethods.GetWindowText(hWnd, buffer, buffer.Length);
            var title = new string(buffer, 0, length).Trim();
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            NativeMethods.GetWindowThreadProcessId(hWnd, out var processId);
            var processName = TryGetProcessName(processId);
            if (!NativeMethods.GetWindowRect(hWnd, out var rect))
            {
                return true;
            }

            windows.Add(new GameWindowInfo(
                hWnd,
                title,
                processName,
                rect.Left,
                rect.Top,
                Math.Max(0, rect.Right - rect.Left),
                Math.Max(0, rect.Bottom - rect.Top)));
            return true;
        }, IntPtr.Zero);

        return windows.OrderBy(window => window.ProcessName).ThenBy(window => window.Title).ToArray();
    }

    private static string TryGetProcessName(uint processId)
    {
        try
        {
            return Process.GetProcessById((int)processId).ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }
}
