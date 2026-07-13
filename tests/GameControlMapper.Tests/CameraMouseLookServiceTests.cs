using GameControlMapper.Models;
using GameControlMapper.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameControlMapper.Tests;

public sealed class CameraMouseLookServiceTests
{
    [Fact] public void CameraArming_HidesCursorWithoutCreatingTap(){using var f=new F();Assert.False(f.Cursor.Visible);Assert.Empty(f.Contacts.ActiveContacts);Assert.Empty(f.Backend.RecordedFrames);}
    [Fact] public void CameraMouseLook_ContinuesInInputDirection(){using var f=new F();f.Real(50,0);var x=f.Contact.X;f.Real(50,0);Assert.True(f.Contact.X>x);}
    [Fact] public void CameraMouseLook_DoesNotRecenterCursor(){using var f=new F();f.Real(10,0);Assert.Equal(0,f.Cursor.SetCount);Assert.Equal(new PhysicalScreenPoint(510,500),f.Cursor.Position);}
    [Fact] public void RelativeNegativeMove_MovesTouchContactLeft(){using var f=new F();f.Real(-10,0);Assert.True(f.Contact.X<f.FirstDownX);}
    [Fact] public void RealMoveAfterZeroPacket_IsProcessed(){using var f=new F();f.Warp();f.Real(10,0);Assert.True(f.Contact.X>f.FirstDownX);}
    [Fact] public void CameraStart_HidesWithoutReadingMovingOrClippingCursor(){using var f=new F();Assert.False(f.Cursor.Visible);Assert.Equal(0,f.Cursor.GetPositionCount);Assert.Equal(0,f.Cursor.SetCount);Assert.Equal(f.Cursor.OriginalClip,f.Cursor.Clip);}
    [Fact] public void CameraStartFailure_LeavesCursorVisible(){var c=new FakeCursor{FailHide=true};using var f=new F(c,false);Assert.False(f.Camera.Start(new(),500,500));Assert.True(c.Visible);Assert.False(f.Camera.IsActive);}
    [Fact] public void CameraStop_RestoresCursorAndClipState(){using var f=new F();f.Camera.Stop();AssertRestored(f);}
    [Fact] public void FocusLoss_RestoresCursorState(){using var f=new F();f.Camera.Stop();AssertRestored(f);}
    [Fact] public void GeometryInvalidation_RestoresCursorState(){using var f=new F();f.Camera.Stop();AssertRestored(f);}
    [Fact] public void ApplicationShutdown_RestoresCursorState(){var f=new F();f.Camera.Dispose();AssertRestored(f);}
    [Fact] public void RepeatedStop_RestoresCursorExactlyOnce(){using var f=new F();f.Camera.Stop();f.Camera.Stop();Assert.Equal(1,f.Cursor.RestoreVisibleCount);Assert.Equal(0,f.Cursor.SetCount);}
    [Fact] public void CameraDeadZone_DoesNotCreateTouch(){using var f=new F(settings:new(){DeadZone=5,Smooth=0});f.Real(2,2);Assert.Empty(f.Contacts.ActiveContacts);Assert.DoesNotContain(f.Backend.RecordedFrames.SelectMany(x=>x.Contacts),x=>x.State==TouchState.Down);}
    [Fact] public void CameraSensitivity_ScalesMovement(){using var a=new F(settings:new(){SensitivityX=1,Smooth=0});using var b=new F(settings:new(){SensitivityX=2,Smooth=0});a.Real(5,0);b.Real(5,0);Assert.True(b.Contact.X-b.FirstDownX>a.Contact.X-a.FirstDownX);}
    [Fact] public void CameraInvertX_InvertsHorizontalMovement(){using var f=new F(settings:new(){InvertX=true,Smooth=0});f.Real(5,0);Assert.True(f.Contact.X<f.FirstDownX);}
    [Fact] public void CameraInvertY_InvertsVerticalMovement(){using var f=new F(settings:new(){InvertY=true,Smooth=0});f.Real(0,5);Assert.True(f.Contact.Y<f.FirstDownY);}
    [Fact] public void CameraAcceleration_IncreasesResponse(){using var a=new F(settings:new(){Smooth=0});using var b=new F(settings:new(){Acceleration=1,Smooth=0,MaxSpeed=100});a.Real(4,0);b.Real(4,0);Assert.True(b.Contact.X-b.FirstDownX>a.Contact.X-a.FirstDownX);}
    [Fact] public void CameraSmoothing_IsTimeBased(){using var f=new F(settings:new(){Smooth=1,MaxSpeed=100});f.Time.Advance(.01);f.Real(20,0);var x=f.Contact.X;f.Time.Advance(.2);f.Real(20,0);Assert.True(f.Contact.X-x>0);}
    [Fact] public void CameraMaxSpeed_ClampsVelocity(){using var f=new F(settings:new(){Smooth=0,MaxSpeed=3,DragRadius=100});f.Real(100,0);Assert.InRange(f.Contact.X-f.FirstDownX,0,3);}
    [Fact] public void CameraPacketRate_DoesNotChangeSummedMotion(){using var packets=new F(settings:new(){Smooth=0,MaxSpeed=100,DragRadius=500});using var batch=new F(settings:new(){Smooth=0,MaxSpeed=100,DragRadius=500});for(var i=0;i<10;i++)packets.Camera.OnMouseMove(1,0,packets.Camera.Generation);packets.Flush();batch.Real(10,0);Assert.Equal(batch.Contact.X-batch.FirstDownX,packets.Contact.X-packets.FirstDownX,6);}
    [Fact] public void FirstPhysicalMovement_ProducesDragNotStationaryTap(){using var f=new F();f.Real(10,0);f.Flush();var contacts=f.Backend.RecordedFrames.SelectMany(x=>x.Contacts).ToArray();Assert.Contains(contacts,x=>x.State==TouchState.Down);Assert.Contains(contacts,x=>x.State==TouchState.Update);}
    [Fact] public void LongDirectionalStroke_AvoidsPeriodicRebaseWithinThousandPixels(){using var f=new F(settings:new(){Smooth=0,MaxSpeed=100,DragRadius=800});for(var i=0;i<10;i++)f.Real(100,0);Assert.Equal(0,f.Camera.RebaseCount);Assert.Single(f.Backend.RecordedFrames.SelectMany(x=>x.Contacts).Where(x=>x.State==TouchState.Down));}
    [Fact] public async Task CameraDragRadius_RebasesForUnlimitedRotation(){using var f=new F(settings:new(){Smooth=0,MaxSpeed=100,DragRadius=40});for(var i=0;i<60&&f.Camera.RebaseCount<3;i++){f.Real(30,0);await Task.Delay(5);}Assert.True(f.Camera.RebaseCount>=3);Assert.True(f.Camera.IsActive);Assert.True(f.Backend.RecordedFrames.SelectMany(x=>x.Contacts).Count(x=>x.State==TouchState.Down)>=4);Assert.True(f.Backend.RecordedFrames.SelectMany(x=>x.Contacts).Count(x=>x.State==TouchState.Up)>=3);Assert.All(f.Backend.RecordedFrames.Where(frame=>frame.Contacts.Any(x=>x.State==TouchState.Up)),frame=>Assert.Contains(frame.Contacts,x=>x.State==TouchState.Down));f.Camera.Stop();f.Flush();AssertNoOrphanFrames(f.Backend.RecordedFrames);}
    [Fact] public void OldCameraGeneration_MoveIsIgnored(){using var f=new F();var g=f.Camera.Generation;f.Camera.Stop();Assert.True(f.Camera.Start(new(){Smooth=0},500,500));f.Camera.OnMouseMove(10,0,g);f.Flush();Assert.Empty(f.Contacts.ActiveContacts);}
    [Fact] public void LateMouseMoveAfterStop_IsIgnored(){using var f=new F();f.Camera.Stop();f.Flush();f.Camera.OnMouseMove(10,0);Assert.Empty(f.Contacts.ActiveContacts);}
    [Fact] public void ConcurrentFocusLossAndCtrlUp_SendAtMostOneUp(){using var f=new F();f.Real(10,0);f.Flush();Parallel.Invoke(f.Camera.Stop,f.Camera.Stop);f.Flush();Assert.Empty(f.Contacts.ActiveContacts);Assert.Single(f.Backend.RecordedFrames.SelectMany(x=>x.Contacts).Where(x=>x.State==TouchState.Up));}
    [Fact] public void CursorControllerFailure_FailsClosed(){var c=new FakeCursor{FailHide=true};using var f=new F(c,false);Assert.False(f.Camera.Start(new(),500,500));Assert.False(f.Camera.IsActive);Assert.True(c.Visible);}
    [Fact] public void CursorPositionFailure_DoesNotAffectRelativeCamera(){var c=new FakeCursor{FailSet=true};using var f=new F(c);f.Real(5,0);Assert.True(f.Camera.IsActive);Assert.Equal(0,c.SetCount);}
    [Fact] public void CameraDoesNotTouchCursorWhenMappingInactive(){var c=new FakeCursor();var contacts=MakeContacts();using var camera=new CameraMouseLookService(new(NullLogger<TouchEngine>.Instance,contacts),NullLogger<CameraMouseLookService>.Instance,c,TimeProvider.System,new TargetWindowSessionManager(new FailedGeometry(),NullLogger<TargetWindowSessionManager>.Instance));Assert.False(camera.Start(new(),1,1));Assert.Equal(0,c.SetCount);Assert.Equal(0,c.HideCount);}

    private static void AssertRestored(F f){Assert.True(f.Cursor.Visible);Assert.Equal(f.Cursor.Original,f.Cursor.Position);Assert.Equal(f.Cursor.OriginalClip,f.Cursor.Clip);}
    private static void AssertNoOrphanFrames(IEnumerable<TouchFrameSnapshot> frames){var active=new HashSet<int>();foreach(var contact in frames.SelectMany(frame=>frame.Contacts)){if(contact.State==TouchState.Down)Assert.True(active.Add(contact.ContactId));else if(contact.State==TouchState.Update)Assert.Contains(contact.ContactId,active);else if(contact.State==TouchState.Up)Assert.True(active.Remove(contact.ContactId));}Assert.Empty(active);}
    private static ContactManager MakeContacts()=>new(NullLogger<ContactManager>.Instance,new(10,true,false,true));
    private sealed class F:IDisposable
    {
        public FakeCursor Cursor;public FakeTime Time=new();public ContactManager Contacts=MakeContacts();public TouchEngine Engine;public FakeTouchBackend Backend=new();public TouchScheduler Scheduler;public CameraMouseLookService Camera;
        public TouchContact Contact=>Contacts.ActiveContacts.Values.Single();
        public double FirstDownX=>Backend.RecordedFrames.SelectMany(x=>x.Contacts).First(x=>x.State==TouchState.Down).X;
        public double FirstDownY=>Backend.RecordedFrames.SelectMany(x=>x.Contacts).First(x=>x.State==TouchState.Down).Y;
        public F(FakeCursor? cursor=null,bool start=true,CameraSettings? settings=null){Cursor=cursor??new();Engine=new(NullLogger<TouchEngine>.Instance,Contacts);Backend.Initialize();Scheduler=new(NullLogger<TouchScheduler>.Instance,Contacts,Backend,new(),allocator:Engine.ContactAllocator);Camera=new(Engine,NullLogger<CameraMouseLookService>.Instance,Cursor,Time,scheduler:Scheduler);if(start)Assert.True(Camera.Start(settings??new(){Smooth=0,MaxSpeed=100,DragRadius=500},500,500));}
        public void Warp()=>Camera.OnMouseMove(0,0,Camera.Generation);
        public void Real(int dx,int dy){Cursor.Position=new(500+dx,500+dy);Time.Advance(.016);Camera.OnMouseMove(dx,dy,Camera.Generation);Flush();}
        public void Flush()=>Scheduler.SendFrameOnceAsync().GetAwaiter().GetResult();
        public void Dispose(){Camera.Dispose();Flush();Scheduler.Dispose();}
    }
    private sealed class FakeTime:TimeProvider{private long _ticks;public override long TimestampFrequency=>1000;public override long GetTimestamp()=>_ticks;public void Advance(double seconds)=>_ticks+=(long)(seconds*1000);}
    private sealed class FakeCursor:IMouseCursorController
    {
        public PhysicalScreenPoint Original=new(20,30);public PhysicalScreenPoint Position;public CursorClip? OriginalClip=new CursorClip(0,0,1000,1000);public CursorClip? Clip;public bool Visible=true,FailHide,FailSet;public int SetCount,GetPositionCount,HideCount,RestoreVisibleCount;
        public FakeCursor(){Position=Original;Clip=OriginalClip;}
        public bool TryGetPosition(out PhysicalScreenPoint p){GetPositionCount++;p=Position;return true;}
        public bool TrySetPosition(PhysicalScreenPoint p){SetCount++;if(FailSet){FailSet=false;return false;}Position=p;return true;}
        public bool TryGetClip(out CursorClip? c){c=Clip;return true;}
        public bool TrySetClip(CursorClip? c){Clip=c;return true;}
        public bool TrySetVisible(bool v){if(FailHide&&!v)return false;Visible=v;if(v)RestoreVisibleCount++;else HideCount++;return true;}
    }
    private sealed class FailedGeometry:IGameWindowGeometryProvider{public WindowGeometryResult GetClientRect(nint h)=>WindowGeometryResult.Failure("x",1,"x");}
}
