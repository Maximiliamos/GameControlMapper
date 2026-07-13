using System.Collections.Concurrent;
using GameControlMapper.Models;
using GameControlMapper.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameControlMapper.Tests;

public sealed class TargetWindowGeometryMonitorTests
{
    [Fact] public void SchedulerFrame_DoesNotCallGeometryProvider() { var p=new CountingProvider(); var m=new TargetWindowSessionManager(p,NullLogger<TargetWindowSessionManager>.Instance); m.TryStart(1,new(100,100)); for(var i=0;i<100;i++) Assert.True(m.ValidateActiveSession()); Assert.Equal(1,p.Count); }

    [Fact]
    public void LocationChangeEvent_TriggersImmediateValidation()
    {
        using var fixture=new Fixture();fixture.Track(Session(1));var baseline=fixture.Provider.Count;fixture.Events.Raise(10);
        Assert.True(SpinWait.SpinUntil(()=>fixture.Provider.Count>baseline,TimeSpan.FromSeconds(1)));
    }

    [Fact] public void ResizeEvent_InvalidatesActiveSession()=>AssertInvalidated(provider=>provider.Rect=new(0,0,101,100));
    [Fact] public void MinimizeEvent_InvalidatesActiveSession()=>AssertInvalidated(provider=>provider.Result=WindowGeometryResult.Failure("IsIconic",0,"minimized"));
    [Fact] public void DestroyedWindow_InvalidatesActiveSession()=>AssertInvalidated(provider=>provider.Result=WindowGeometryResult.Failure("IsWindow",0,"destroyed"));
    [Fact] public void HiddenWindow_InvalidatesActiveSession()=>AssertInvalidated(provider=>provider.Result=WindowGeometryResult.Failure("IsWindowVisible",0,"hidden"));
    [Fact] public void MissedNativeEvent_IsDetectedByFallbackPolling(){using var fixture=new Fixture();fixture.Track(Session(1));fixture.Provider.Rect=new(0,0,101,100);Assert.True(SpinWait.SpinUntil(()=>fixture.Invalidations.Count==1,TimeSpan.FromSeconds(5)));}

    [Fact]
    public void RepeatedLocationEvents_AreCoalesced()
    {
        using var fixture=new Fixture();fixture.Track(Session(1));fixture.Provider.Block=true;fixture.Events.Raise(10);Assert.True(fixture.Provider.Entered.Wait(TimeSpan.FromSeconds(1)));
        for(var i=0;i<50;i++)fixture.Events.Raise(10);Assert.Equal(1,fixture.Provider.Count);fixture.Provider.Release.Set();
    }

    [Fact]
    public void GeometryChangeConcurrentWithManualStop_SendsOneUp()
    {
        using var fixture=new Fixture();fixture.Track(Session(1));fixture.Provider.Rect=new(0,0,101,100);fixture.Provider.Block=true;fixture.Events.Raise(10);Assert.True(fixture.Provider.Entered.Wait(TimeSpan.FromSeconds(1)));fixture.Monitor.Stop(1);fixture.Provider.Release.Set();Thread.Sleep(30);Assert.Empty(fixture.Invalidations);
    }


    [Fact]
    public void LateGeometryNotification_FromOldGeneration_IsIgnored()
    {
        using var fixture=new Fixture();fixture.Track(Session(1));fixture.Provider.Rect=new(5,5,100,100);fixture.Provider.Block=true;fixture.Events.Raise(10);Assert.True(fixture.Provider.Entered.Wait(TimeSpan.FromSeconds(1)));
        fixture.Track(Session(2,new(5,5,100,100)));fixture.Provider.Release.Set();Thread.Sleep(30);Assert.Empty(fixture.Invalidations);
    }

    [Fact]
    public void GeometryMonitor_DisposeUnhooksNativeEvents()
    {
        var fixture=new Fixture();fixture.Dispose();Assert.Equal(42,fixture.Events.UninstalledHandle);
    }

    [Fact]
    public void NativeGeometryCallbackAfterDispose_IsIgnored()
    {
        var fixture=new Fixture();fixture.Track(Session(1));fixture.Dispose();var count=fixture.Provider.Count;fixture.Events.RaiseLate(10);Thread.Sleep(30);Assert.Equal(count,fixture.Provider.Count);
    }

    [Fact]
    public void GeometryCallbackException_IsContained()
    {
        using var fixture=new Fixture();fixture.Track(Session(1));fixture.Provider.Throw=true;fixture.Events.Raise(10);Assert.True(SpinWait.SpinUntil(()=>fixture.Provider.Count>0,TimeSpan.FromSeconds(1)));Thread.Sleep(20);Assert.Empty(fixture.Invalidations);
    }

    [Fact]
    public void RepeatedTrackStop_DoesNotLoseHook()
    {
        using var fixture=new Fixture();for(var generation=1;generation<=10;generation++){fixture.Track(Session(generation));fixture.Monitor.Stop(generation);}Assert.Equal(1,fixture.Events.InstallCount);
    }

    [Fact] public void NewSessionAfterWindowMove_UsesFreshSnapshot() { var p=new CountingProvider(); var m=new TargetWindowSessionManager(p,NullLogger<TargetWindowSessionManager>.Instance); var a=m.TryStart(1,new(10,10)).Session; p.Rect=new(4,5,20,20); var b=m.TryStart(1,new(10,10)).Session; Assert.NotEqual(a!.ClientRect,b!.ClientRect); }
    [Fact] public void InvalidSnapshot_FailsClosed()=>Assert.False(new TargetWindowSession(1,new(1,1),CoordinateScaleMode.Stretch,new(0,0,1,1),1,1,false).IsActive);

    private static void AssertInvalidated(Action<CountingProvider> change)
    {
        using var fixture=new Fixture();fixture.Track(Session(1));change(fixture.Provider);fixture.Events.Raise(10);Assert.True(SpinWait.SpinUntil(()=>fixture.Invalidations.Count==1,TimeSpan.FromSeconds(1)));Assert.Equal(1,fixture.Invalidations.Single());
    }
    private static TargetWindowSession Session(long generation,PhysicalClientRect? rect=null)=>new(10,new(100,100),CoordinateScaleMode.Stretch,rect??new(0,0,100,100),7,generation,true);

    private sealed class Fixture:IDisposable
    {
        public CountingProvider Provider{get;}=new();public FakeNative Native{get;}=new();public FakeEvents Events{get;}=new();public ConcurrentQueue<long> Invalidations{get;}=[];public TargetWindowGeometryMonitor Monitor{get;}
        public Fixture(){Monitor=new(Provider,Native,Events,TimeProvider.System,NullLogger<TargetWindowGeometryMonitor>.Instance);Monitor.Invalidated+=(_,generation)=>Invalidations.Enqueue(generation);}
        public void Track(TargetWindowSession session)=>Monitor.Track(session);public void Dispose()=>Monitor.Dispose();
    }
    private sealed class CountingProvider:IGameWindowGeometryProvider
    {
        public int Count;public PhysicalClientRect Rect=new(0,0,100,100);public WindowGeometryResult? Result;public bool Throw;public bool Block;public ManualResetEventSlim Entered{get;}=new(false);public ManualResetEventSlim Release{get;}=new(false);
        public WindowGeometryResult GetClientRect(nint h){Interlocked.Increment(ref Count);Entered.Set();if(Block)Release.Wait(TimeSpan.FromSeconds(2));if(Throw)throw new InvalidOperationException("expected");return Result??WindowGeometryResult.Success(Rect);}
    }
    private sealed class FakeNative:IGameWindowNativeAdapter
    {
        public bool IsWindow(nint h)=>true;public bool IsWindowVisible(nint h)=>true;public bool IsIconic(nint h)=>false;public bool GetClientRect(nint h,out NativeClientRect r){r=new(0,0,100,100);return true;}public bool ClientToScreen(nint h,ref PhysicalScreenPoint p)=>true;public int GetLastError()=>0;public uint GetWindowProcessId(nint h)=>7;
    }
    private sealed class FakeEvents:ITargetWindowGeometryNativeAdapter
    {
        private Action<nint>? _callback;private Action<nint>? _late;public int InstallCount;public nint UninstalledHandle;
        public nint Install(Action<nint> callback){InstallCount++;_callback=_late=callback;return 42;}public void Uninstall(nint hook){UninstalledHandle=hook;_callback=null;}public void Raise(nint h)=>_callback?.Invoke(h);public void RaiseLate(nint h)=>_late?.Invoke(h);
    }
}
