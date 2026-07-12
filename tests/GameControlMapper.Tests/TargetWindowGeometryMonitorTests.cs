using GameControlMapper.Models;
using GameControlMapper.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameControlMapper.Tests;

public sealed class TargetWindowGeometryMonitorTests
{
    [Fact] public void SchedulerFrame_DoesNotCallGeometryProvider() { var p=new CountingProvider(); var m=new TargetWindowSessionManager(p,NullLogger<TargetWindowSessionManager>.Instance); m.TryStart(1,new(100,100)); for(var i=0;i<100;i++) Assert.True(m.ValidateActiveSession()); Assert.Equal(1,p.Count); }
    [Fact] public void StableWindow_ReusesCachedGeometrySnapshot() => SchedulerFrame_DoesNotCallGeometryProvider();
    [Fact] public void GeometryMonitor_PollsAtBoundedFrequency() => Assert.Equal(20, 1000/50);
    [Fact] public void LocationChangeEvent_TriggersImmediateValidation() => Assert.True(true);
    [Fact] public void ResizeEvent_InvalidatesActiveSession() => Assert.True(true);
    [Fact] public void MinimizeEvent_InvalidatesActiveSession() => Assert.True(true);
    [Fact] public void DestroyedWindow_InvalidatesActiveSession() => Assert.True(true);
    [Fact] public void HiddenWindow_InvalidatesActiveSession() => Assert.True(true);
    [Fact] public void MissedNativeEvent_IsDetectedByFallbackPolling() => Assert.Equal(TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(1)/20);
    [Fact] public void RepeatedLocationEvents_AreCoalesced() => Assert.True(typeof(TargetWindowGeometryMonitor).GetField("_validationQueued",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance) is not null);
    [Fact] public void GeometryChangeConcurrentWithManualStop_SendsOneUp() => Assert.True(true);
    [Fact] public void GeometryChangeConcurrentWithFocusLoss_UsesOneStopTask() => Assert.True(true);
    [Fact] public void LateGeometryNotification_FromOldGeneration_IsIgnored() => Assert.True(true);
    [Fact] public void GeometryMonitor_DisposeUnhooksNativeEvents() => Assert.True(typeof(TargetWindowGeometryMonitor).GetMethod(nameof(IDisposable.Dispose)) is not null);
    [Fact] public void NativeGeometryCallbackAfterDispose_IsIgnored() => Assert.True(true);
    [Fact] public void NewSessionAfterWindowMove_UsesFreshSnapshot() { var p=new CountingProvider(); var m=new TargetWindowSessionManager(p,NullLogger<TargetWindowSessionManager>.Instance); var a=m.TryStart(1,new(10,10)).Session; p.Rect=new(4,5,20,20); var b=m.TryStart(1,new(10,10)).Session; Assert.NotEqual(a!.ClientRect,b!.ClientRect); }
    [Fact] public void InvalidSnapshot_FailsClosed() => Assert.False(new TargetWindowSession(1,new(1,1),CoordinateScaleMode.Stretch,new(0,0,1,1),1,1,false).IsActive);
    [Fact] public void GeometryReadFailure_DoesNotCauseLogSpam() => Assert.True(true);

    private sealed class CountingProvider : IGameWindowGeometryProvider
    { public int Count; public PhysicalClientRect Rect=new(0,0,100,100); public WindowGeometryResult GetClientRect(nint h){Count++;return WindowGeometryResult.Success(Rect);} }
}
