using GameControlMapper.Models;
using GameControlMapper.Win32;
using System.Runtime.InteropServices;

namespace GameControlMapper.Services;

public readonly record struct CursorClip(int Left, int Top, int Right, int Bottom);

public interface IMouseCursorController
{
    bool TryGetPosition(out PhysicalScreenPoint point);
    bool TrySetPosition(PhysicalScreenPoint point);
    bool TryGetClip(out CursorClip? clip);
    bool TrySetClip(CursorClip? clip);
    bool TryGetVisible(out bool visible);
    bool TrySetVisible(bool visible);
}

public sealed class WindowsMouseCursorController : IMouseCursorController
{
    private readonly object _visibilityGate = new();
    public bool TryGetPosition(out PhysicalScreenPoint point) { var ok=NativeMethods.GetCursorPos(out var p); point=new(p.X,p.Y); return ok; }
    public bool TrySetPosition(PhysicalScreenPoint point) => NativeMethods.SetCursorPos(point.X,point.Y);
    public bool TryGetClip(out CursorClip? clip) { if(!NativeMethods.GetClipCursor(out var r)){clip=null;return false;} clip=new(r.Left,r.Top,r.Right,r.Bottom);return true; }
    public bool TrySetClip(CursorClip? clip) { if(clip is null)return NativeMethods.ClipCursor(IntPtr.Zero); var r=new NativeMethods.RECT{Left=clip.Value.Left,Top=clip.Value.Top,Right=clip.Value.Right,Bottom=clip.Value.Bottom};return NativeMethods.ClipCursor(ref r); }
    public bool TryGetVisible(out bool visible)=>TryIsCursorShowing(out visible);
    public bool TrySetVisible(bool visible)
    {
        lock (_visibilityGate)
        {
            return SetActualVisibility(visible);
        }
    }

    private static bool SetActualVisibility(bool visible)
    {
        for (var attempt = 0; attempt < 64; attempt++)
        {
            if (TryIsCursorShowing(out var showing) && showing == visible) return true;
            NativeMethods.ShowCursor(visible);
        }
        return TryIsCursorShowing(out var finalState) && finalState == visible;
    }

    private static bool TryIsCursorShowing(out bool showing)
    {
        var info = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        if (!GetCursorInfo(ref info)) { showing = false; return false; }
        showing = (info.Flags & CursorShowing) != 0;
        return true;
    }

    private const int CursorShowing = 0x00000001;
    [StructLayout(LayoutKind.Sequential)]
    private struct CursorInfo { public int Size; public int Flags; public IntPtr Cursor; public NativeMethods.POINT ScreenPosition; }
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorInfo(ref CursorInfo cursorInfo);
}
