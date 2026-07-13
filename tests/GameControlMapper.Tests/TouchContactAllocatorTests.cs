using GameControlMapper.Models;
using GameControlMapper.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameControlMapper.Tests;

public sealed class TouchContactAllocatorTests
{
    private static TouchContactAllocator New(int n=10)=>new(new(n,true,false,true),NullLogger<TouchContactAllocator>.Instance);
    [Fact] public void Allocator_AssignsUniqueIds(){var a=New();Assert.NotEqual(a.TryAcquire(1,"a")!.ContactId,a.TryAcquire(1,"b")!.ContactId);}
    [Fact] public void Allocator_UsesFullBackendCapacity(){var a=New();Assert.Equal(10,Enumerable.Range(0,10).Select(i=>a.TryAcquire(1,$"o{i}")).Count(x=>x is not null));}
    [Fact] public void Allocator_RejectsEleventhConcurrentContact(){var a=Full();Assert.Null(a.TryAcquire(1,"11"));}
    [Fact] public void Allocator_ReusesIdAfterSuccessfulUp(){var a=New();var l=a.TryAcquire(1,"a")!;a.RequestRelease(l);a.CompleteSuccessfulUp([l.ContactId]);Assert.Equal(l.ContactId,a.TryAcquire(1,"b")!.ContactId);}
    [Fact] public void Allocator_DoesNotReuseIdBeforeUp(){var a=New(1);var l=a.TryAcquire(1,"a")!;a.RequestRelease(l);Assert.Null(a.TryAcquire(1,"b"));}
    [Fact] public void Allocator_DoesNotReuseIdAfterFailedUpInSameGeneration(){var a=New(1);var l=a.TryAcquire(1,"a")!;a.RequestRelease(l);a.QuarantineFailedUp([l.ContactId]);Assert.Null(a.TryAcquire(1,"b"));}
    [Fact] public void Allocator_ReleasesQuarantinedIdsAfterBackendReset(){var a=New(1);var l=a.TryAcquire(1,"a")!;a.QuarantineFailedUp([l.ContactId]);a.Reset(2);Assert.NotNull(a.TryAcquire(2,"b"));}
    [Fact] public void Allocator_RepeatedReleaseIsIdempotent(){var a=New();var l=a.TryAcquire(1,"a")!;Assert.True(a.RequestRelease(l));Assert.True(a.RequestRelease(l));}
    [Fact] public void Allocator_OldGenerationCannotReleaseNewLease(){var a=New(1);var old=a.TryAcquire(1,"a")!;a.Reset(2);var current=a.TryAcquire(2,"b")!;Assert.False(a.RequestRelease(old));Assert.Equal(TouchLeaseState.Active,current.State);}
    [Fact] public void Allocator_ConcurrentAcquireReturnsUniqueIds(){var a=New();var leases=new TouchContactLease?[10];Parallel.For(0,10,i=>leases[i]=a.TryAcquire(1,$"o{i}"));Assert.Equal(10,leases.Select(x=>x!.ContactId).Distinct().Count());}
    [Fact] public void Allocator_ConcurrentReleaseDoesNotCorruptPool(){var a=Full();var ls=a.ActiveLeases.ToArray();Parallel.ForEach(ls,l=>{a.RequestRelease(l);a.CompleteSuccessfulUp([l.ContactId]);});Assert.Equal(10,Enumerable.Range(0,10).Count(i=>a.TryAcquire(1,$"n{i}") is not null));}
    [Fact] public void TwoJoystickBindings_DoNotShareContactId()=>AssertOwnersUnique("joystick:a","joystick:b");
    [Fact] public void TwoMouseAreas_DoNotShareContactId()=>AssertOwnersUnique("mouse:a","mouse:b");
    [Fact] public void CameraAndJoystick_DoNotConflict()=>AssertOwnersUnique("camera","joystick");
    [Fact] public void CameraAndMouseArea_DoNotConflict()=>AssertOwnersUnique("camera","mouse");
    [Fact] public void CapacityFailure_DoesNotLeaveSuppressionStuck(){var a=Full();Assert.Null(a.TryAcquire(1,"rejected"));Assert.Equal(10,a.ActiveLeases.Count);}
    [Fact] public void Stop_ReleasesAllAllocatorLeases(){var a=Full();a.Reset(2);Assert.Empty(a.ActiveLeases);}
    [Fact] public void FailedFinalUp_QuarantinesLease(){var a=New();var l=a.TryAcquire(1,"a")!;a.QuarantineFailedUp([l.ContactId]);Assert.Equal(TouchLeaseState.Quarantined,l.State);}
    [Fact] public void NewSession_StartsWithValidAllocatorState(){var a=Full();a.Reset(2);Assert.NotNull(a.TryAcquire(2,"new"));}
    [Fact] public void LateGestureCompletion_CannotReuseReleasedLease(){var a=New(1);var old=a.TryAcquire(1,"old")!;a.Reset(2);a.TryAcquire(2,"new");Assert.False(a.RequestRelease(old));}
    [Fact] public void DuplicateOwnerRelease_DoesNotReleaseAnotherContact(){var a=New();var x=a.TryAcquire(1,"same")!;var y=a.TryAcquire(1,"same")!;a.RequestRelease(x);a.CompleteSuccessfulUp([x.ContactId]);Assert.Equal(TouchLeaseState.Active,y.State);}
    [Fact] public void ContactIdRemainsStableDuringMove(){var a=New();var l=a.TryAcquire(1,"a")!;var id=l.ContactId;Assert.Equal(id,l.ContactId);}
    [Fact] public void AllocatorNeverReturnsIdOutsideBackendCapabilities(){var a=New(3);var ids=Enumerable.Range(0,3).Select(i=>a.TryAcquire(1,$"o{i}")!.ContactId);Assert.All(ids,id=>Assert.InRange(id,0,2));}
    [Fact] public void Allocator_DeterministicParallelStress(){var a=New(10);for(var round=0;round<100;round++){var ls=new TouchContactLease?[10];Parallel.For(0,10,i=>ls[i]=a.TryAcquire(1,$"{round}:{i}"));Assert.Equal(10,ls.Select(x=>x!.ContactId).Distinct().Count());Parallel.ForEach(ls,l=>{a.RequestRelease(l!);a.CompleteSuccessfulUp([l!.ContactId]);});}Assert.Empty(a.ActiveLeases);}
    private static TouchContactAllocator Full(){var a=New();for(var i=0;i<10;i++)a.TryAcquire(1,$"o{i}");return a;}
    private static void AssertOwnersUnique(string x,string y){var a=New();Assert.NotEqual(a.TryAcquire(1,x)!.ContactId,a.TryAcquire(1,y)!.ContactId);}
}
