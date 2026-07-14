using GameControlMapper.Models;
using GameControlMapper.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameControlMapper.Tests;

public sealed class TargetWindowFocusSafetyTests
{
    [Fact]
    public void Start_WhenTargetIsForeground_StartsRealSession()
    {
        using var fixture=new Fixture();var result=fixture.Session.TryStart(11,new(1920,1080));
        Assert.True(result.Succeeded,result.Error);Assert.Equal((nint)10,result.Session!.WindowHandle);Assert.Equal((uint)4,result.Session.ProcessId);
    }

    [Fact]
    public void Start_WhenTargetIsNotForeground_IsRejected()
    {
        using var fixture=new Fixture();fixture.Activation.ForegroundRoot=20;
        Assert.False(fixture.Session.TryStart(11,new(1920,1080)).Succeeded);
    }

    [Fact]
    public void Start_WhenForegroundReadFails_IsRejected()
    {
        using var fixture=new Fixture(installHook:false);
        Assert.False(fixture.Session.TryStart(11,new(1920,1080)).Succeeded);
    }

    [Fact]
    public void Start_WhenTargetPidChanged_IsRejected()
    {
        using var fixture=new Fixture();fixture.Activation.ForegroundProcessId=5;
        Assert.False(fixture.Session.TryStart(11,new(1920,1080)).Succeeded);
    }

    [Fact]
    public void Start_WhenTargetIsMinimized_IsRejected()
    {
        using var fixture=new Fixture();fixture.Window.Minimized=true;fixture.Activation.Minimized=true;
        Assert.False(fixture.Session.TryStart(11,new(1920,1080)).Succeeded);
    }

    [Fact]
    public void ChildTarget_IsNormalizedToRootWindow()
    {
        using var fixture=new Fixture();var result=fixture.Session.TryStart(11,new(1920,1080));Assert.Equal((nint)10,result.Session!.WindowHandle);
    }

    [Fact]
    public void ReusedHwndWithDifferentPid_InvalidatesForegroundIdentity()
    {
        using var fixture=new Fixture();Assert.True(fixture.Session.TryStart(11,new(1920,1080)).Succeeded);fixture.Activation.ForegroundProcessId=5;Assert.False(fixture.Session.IsForegroundActive());
    }

    [Fact]
    public void DifferentRootWindowFromSameProcess_IsNotAccepted()
    {
        using var fixture=new Fixture();Assert.True(fixture.Session.TryStart(11,new(1920,1080)).Succeeded);fixture.Activation.ForegroundRoot=12;Assert.False(fixture.Session.IsForegroundActive());
    }

    [Fact]
    public void ActivationMonitor_DisposeUnhooksNativeEvent()
    {
        var native=new FakeActivationNative();var monitor=new TargetWindowActivationMonitor(native,NullLogger<TargetWindowActivationMonitor>.Instance);monitor.Dispose();Assert.Equal(1,native.UnhookCount);
    }

    [Fact]
    public void NativeCallbackAfterDispose_DoesNotReachManagedHandler()
    {
        var native=new FakeActivationNative();var monitor=new TargetWindowActivationMonitor(native,NullLogger<TargetWindowActivationMonitor>.Instance);var count=0;monitor.ActivationChanged+=(_,_)=>count++;monitor.Dispose();native.Raise();Thread.Sleep(30);Assert.Equal(0,count);
    }

    [Fact]
    public void RepeatedForegroundNotifications_AreCoalescedAndContained()
    {
        using var fixture=new Fixture();using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();using var completed=new ManualResetEventSlim();var count=0;
        fixture.Monitor.ActivationChanged+=(_,_)=>{Interlocked.Increment(ref count);entered.Set();release.Wait(TimeSpan.FromSeconds(5));completed.Set();};
        fixture.Activation.Raise();Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        for(var i=1;i<100;i++)fixture.Activation.Raise();
        Assert.Equal(1,Volatile.Read(ref count));release.Set();Assert.True(completed.Wait(TimeSpan.FromSeconds(5)));
    }

    private sealed class Fixture:IDisposable
    {
        public FakeActivationNative Activation{get;} public FakeWindowNative Window{get;}=new();public TargetWindowActivationMonitor Monitor{get;} public TargetWindowSessionManager Session{get;}
        public Fixture(bool installHook=true){Activation=new(){InstallHook=installHook};Monitor=new(Activation,NullLogger<TargetWindowActivationMonitor>.Instance);var geometry=new GameWindowGeometryProvider(Window,NullLogger<GameWindowGeometryProvider>.Instance);Session=new(geometry,NullLogger<TargetWindowSessionManager>.Instance,Window,Monitor);}
        public void Dispose()=>Monitor.Dispose();
    }

    private sealed class FakeActivationNative:ITargetWindowActivationNativeAdapter
    {
        private Action<nint>? _callback;public bool InstallHook=true;public nint ForegroundRoot=10;public uint ForegroundProcessId=4;public bool Minimized;public int UnhookCount;
        public nint GetForegroundWindow()=>ForegroundRoot;public nint GetRootWindow(nint hwnd)=>ForegroundRoot;public uint GetProcessId(nint hwnd)=>ForegroundProcessId;public bool IsWindow(nint hwnd)=>hwnd!=0;public bool IsIconic(nint hwnd)=>Minimized;
        public nint InstallForegroundHook(Action<nint> callback){_callback=callback;return InstallHook?1:0;}public void UninstallForegroundHook(nint hook){UnhookCount++;_callback=null;}public void Raise()=>_callback?.Invoke(ForegroundRoot);
    }

    private sealed class FakeWindowNative:IGameWindowNativeAdapter
    {
        public bool Minimized;public bool IsWindow(nint hwnd)=>hwnd!=0;public bool IsWindowVisible(nint hwnd)=>true;public bool IsIconic(nint hwnd)=>Minimized;
        public bool GetClientRect(nint hwnd,out NativeClientRect rect){rect=new(0,0,1920,1080);return true;}public bool ClientToScreen(nint hwnd,ref PhysicalScreenPoint point)=>true;public int GetLastError()=>0;
        public nint GetAncestor(nint hwnd,uint flags)=>10;public uint GetWindowProcessId(nint hwnd)=>4;
    }
}
