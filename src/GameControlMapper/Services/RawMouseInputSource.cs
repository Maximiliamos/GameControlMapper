using System.Runtime.InteropServices;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;
namespace GameControlMapper.Services;
public interface IRelativeMouseInputSource{event Action<int,int>? Moved;}
public sealed class RawMouseInputSource:IRelativeMouseInputSource,IDisposable
{
 private const int WmInput=0x00FF;private const uint RidInput=0x10000003,RimTypeMouse=0,RidevInputSink=0x00000100,RidevRemove=0x00000001,MouseMoveAbsolute=0x0001;private readonly HwndSource _source;private readonly ILogger<RawMouseInputSource> _logger;private int _disposed;
 public RawMouseInputSource(ILogger<RawMouseInputSource> logger){_logger=logger;var p=new HwndSourceParameters("GameControlMapper.RawMouseInput"){ParentWindow=new IntPtr(-3),WindowStyle=0,Width=0,Height=0};_source=new HwndSource(p);_source.AddHook(WndProc);var device=new RAWINPUTDEVICE{UsagePage=1,Usage=2,Flags=RidevInputSink,Target=_source.Handle};if(!RegisterRawInputDevices([device],1,(uint)Marshal.SizeOf<RAWINPUTDEVICE>()))throw new InvalidOperationException($"RegisterRawInputDevices failed: {Marshal.GetLastWin32Error()}");}
 public event Action<int,int>? Moved;
 private IntPtr WndProc(IntPtr hwnd,int msg,IntPtr wParam,IntPtr lParam,ref bool handled){if(msg!=WmInput||Volatile.Read(ref _disposed)!=0)return IntPtr.Zero;uint size=0;var header=(uint)Marshal.SizeOf<RAWINPUTHEADER>();if(GetRawInputData(lParam,RidInput,IntPtr.Zero,ref size,header)==uint.MaxValue||size==0)return IntPtr.Zero;var buffer=Marshal.AllocHGlobal((int)size);try{if(GetRawInputData(lParam,RidInput,buffer,ref size,header)!=size)return IntPtr.Zero;var raw=Marshal.PtrToStructure<RAWINPUT>(buffer);if(raw.Header.Type==RimTypeMouse&&TryGetRelativeDelta(raw.Mouse,out var dx,out var dy))Moved?.Invoke(dx,dy);}catch(Exception ex){_logger.LogError(ex,"Raw mouse input processing failed");}finally{Marshal.FreeHGlobal(buffer);}return IntPtr.Zero;}
 internal static bool TryGetRelativeDelta(RAWMOUSE mouse,out int dx,out int dy){dx=mouse.LastX;dy=mouse.LastY;return(mouse.Flags&MouseMoveAbsolute)==0&&(dx!=0||dy!=0);}
 public void Dispose(){if(Interlocked.Exchange(ref _disposed,1)!=0)return;try{RegisterRawInputDevices([new RAWINPUTDEVICE{UsagePage=1,Usage=2,Flags=RidevRemove,Target=IntPtr.Zero}],1,(uint)Marshal.SizeOf<RAWINPUTDEVICE>());}catch{}Moved=null;_source.RemoveHook(WndProc);_source.Dispose();}
 [StructLayout(LayoutKind.Sequential)]internal struct RAWINPUTDEVICE{public ushort UsagePage,Usage;public uint Flags;public IntPtr Target;}
 [StructLayout(LayoutKind.Sequential)]internal struct RAWINPUTHEADER{public uint Type,Size;public IntPtr Device,WParam;}
 [StructLayout(LayoutKind.Sequential)]internal struct RAWMOUSE{public ushort Flags;public uint Buttons;public uint RawButtons;public int LastX,LastY;public uint ExtraInformation;}
 [StructLayout(LayoutKind.Sequential)]internal struct RAWINPUT{public RAWINPUTHEADER Header;public RAWMOUSE Mouse;}
 [DllImport("user32.dll",SetLastError=true)]private static extern bool RegisterRawInputDevices([In]RAWINPUTDEVICE[] devices,uint count,uint size);
 [DllImport("user32.dll",SetLastError=true)]private static extern uint GetRawInputData(IntPtr rawInput,uint command,IntPtr data,ref uint size,uint headerSize);
}
