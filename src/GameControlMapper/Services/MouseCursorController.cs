using GameControlMapper.Models;
using GameControlMapper.Win32;

namespace GameControlMapper.Services;

public readonly record struct CursorClip(int Left, int Top, int Right, int Bottom);

public interface IMouseCursorController
{
    bool TryGetPosition(out PhysicalScreenPoint point);
    bool TrySetPosition(PhysicalScreenPoint point);
    bool TryGetClip(out CursorClip? clip);
    bool TrySetClip(CursorClip? clip);
    bool TrySetVisible(bool visible);
}

public sealed class WindowsMouseCursorController : IMouseCursorController
{
    public bool TryGetPosition(out PhysicalScreenPoint point) { var ok=NativeMethods.GetCursorPos(out var p); point=new(p.X,p.Y); return ok; }
    public bool TrySetPosition(PhysicalScreenPoint point) => NativeMethods.SetCursorPos(point.X,point.Y);
    public bool TryGetClip(out CursorClip? clip) { if(!NativeMethods.GetClipCursor(out var r)){clip=null;return false;} clip=new(r.Left,r.Top,r.Right,r.Bottom);return true; }
    public bool TrySetClip(CursorClip? clip) { if(clip is null)return NativeMethods.ClipCursor(IntPtr.Zero); var r=new NativeMethods.RECT{Left=clip.Value.Left,Top=clip.Value.Top,Right=clip.Value.Right,Bottom=clip.Value.Bottom};return NativeMethods.ClipCursor(ref r); }
    public bool TrySetVisible(bool visible) { NativeMethods.ShowCursor(visible); return true; }
}
