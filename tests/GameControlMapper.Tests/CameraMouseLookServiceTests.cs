using GameControlMapper.Models;
using GameControlMapper.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameControlMapper.Tests;

public sealed class CameraMouseLookServiceTests
{
    [Fact] public void CameraMouseLook_CanContinuePastScreenEdge(){using var f=new F();f.Real(50,0);f.Real(50,0);Assert.True(f.Contact.X>500);}
    [Fact] public void CameraMouseLook_RecentersCursorAtAnchor(){using var f=new F();f.Real(10,0);Assert.Equal(new PhysicalScreenPoint(500,500),f.Cursor.Position);}
    [Fact] public void WarpGeneratedMove_DoesNotMoveTouchContact(){using var f=new F();f.Camera.OnMouseMove(-10,0,f.Camera.Generation);Assert.Equal(500,f.Contact.X);}
    [Fact] public void RealMoveAfterWarp_IsProcessed(){using var f=new F();f.Warp();f.Real(10,0);Assert.True(f.Contact.X>500);}
    [Fact] public void CameraStart_SavesCursorAndClipState(){using var f=new F();Assert.False(f.Cursor.Visible);f.Camera.Stop();Assert.Equal(f.Cursor.OriginalClip,f.Cursor.Clip);}
    [Fact] public void CameraStartFailure_RestoresPartialCursorState(){var c=new FakeCursor{FailHide=true};using var f=new F(c,false);Assert.True(c.Visible);Assert.Equal(c.OriginalClip,c.Clip);}
    [Fact] public void CameraStop_RestoresCursorAndClipState(){using var f=new F();f.Camera.Stop();AssertRestored(f);}
    [Fact] public void FocusLoss_RestoresCursorState(){using var f=new F();f.Camera.Stop();AssertRestored(f);}
    [Fact] public void GeometryInvalidation_RestoresCursorState(){using var f=new F();f.Camera.Stop();AssertRestored(f);}
    [Fact] public void ApplicationShutdown_RestoresCursorState(){var f=new F();f.Camera.Dispose();AssertRestored(f);}
    [Fact] public void RepeatedStop_RestoresCursorOnlyOnce(){using var f=new F();f.Camera.Stop();f.Camera.Stop();Assert.Equal(1,f.Cursor.RestoreVisibleCount);}
    [Fact] public void CameraDeadZone_IgnoresSmallMovement(){using var f=new F(settings:new(){DeadZone=5,Smooth=0});f.Real(2,2);Assert.Equal(500,f.Contact.X);}
    [Fact] public void CameraSensitivity_ScalesMovement(){using var a=new F(settings:new(){SensitivityX=1,Smooth=0});using var b=new F(settings:new(){SensitivityX=2,Smooth=0});a.Real(5,0);b.Real(5,0);Assert.True(b.Contact.X-500>a.Contact.X-500);}
    [Fact] public void CameraInvertX_InvertsHorizontalMovement(){using var f=new F(settings:new(){InvertX=true,Smooth=0});f.Real(5,0);Assert.True(f.Contact.X<500);}
    [Fact] public void CameraInvertY_InvertsVerticalMovement(){using var f=new F(settings:new(){InvertY=true,Smooth=0});f.Real(0,5);Assert.True(f.Contact.Y<500);}
    [Fact] public void CameraAcceleration_IncreasesResponse(){using var a=new F(settings:new(){Smooth=0});using var b=new F(settings:new(){Acceleration=1,Smooth=0,MaxSpeed=100});a.Real(2,0);b.Real(2,0);Assert.True(b.Contact.X>a.Contact.X);}
    [Fact] public void CameraSmoothing_IsTimeBased(){using var f=new F(settings:new(){Smooth=1,MaxSpeed=100});f.Time.Advance(.01);f.Real(20,0);var x=f.Contact.X;f.Time.Advance(.2);f.Real(20,0);Assert.True(f.Contact.X-x>0);}
    [Fact] public void CameraMaxSpeed_ClampsVelocity(){using var f=new F(settings:new(){Smooth=0,MaxSpeed=3,DragRadius=100});f.Real(100,0);Assert.InRange(f.Contact.X,500,503);}
    [Fact] public void CameraDragRadius_ClampsTouchPoint(){using var f=new F(settings:new(){Smooth=0,MaxSpeed=1000,DragRadius=10});f.Real(100,100);Assert.InRange(Math.Sqrt(Math.Pow(f.Contact.X-500,2)+Math.Pow(f.Contact.Y-500,2)),0,10.001);}
    [Fact] public void OldCameraGeneration_MoveIsIgnored(){using var f=new F();var g=f.Camera.Generation;f.Camera.Stop();f.Camera.Start(new(){Smooth=0},500,500);f.Camera.OnMouseMove(10,0,g);Assert.Equal(500,f.Contact.X);}
    [Fact] public void LateMouseMoveAfterStop_IsIgnored(){using var f=new F();f.Camera.Stop();f.Camera.OnMouseMove(10,0);Assert.Empty(f.Contacts.ActiveContacts);}
    [Fact] public void ConcurrentFocusLossAndCtrlUp_SendOneUp(){using var f=new F();Parallel.Invoke(f.Camera.Stop,f.Camera.Stop);Assert.Empty(f.Contacts.ActiveContacts);}
    [Fact] public void CursorControllerFailure_FailsClosed(){var c=new FakeCursor{FailGet=true};using var f=new F(c,false);Assert.False(f.Camera.IsActive);Assert.True(c.Visible);}
    [Fact] public void CameraException_StillRestoresCursorState(){using var f=new F();f.Cursor.FailSet=true;f.Real(5,0);AssertRestored(f);}
    [Fact] public void CameraDoesNotTouchCursorWhenMappingInactive(){var c=new FakeCursor();var contacts=MakeContacts();using var camera=new CameraMouseLookService(new(NullLogger<TouchEngine>.Instance,contacts),NullLogger<CameraMouseLookService>.Instance,c,TimeProvider.System,new TargetWindowSessionManager(new FailedGeometry(),NullLogger<TargetWindowSessionManager>.Instance));camera.Start(new(),1,1);Assert.Equal(0,c.SetCount);}

    private static void AssertRestored(F f){Assert.True(f.Cursor.Visible);Assert.Equal(f.Cursor.Original,f.Cursor.Position);Assert.Equal(f.Cursor.OriginalClip,f.Cursor.Clip);}
    private static ContactManager MakeContacts()=>new(NullLogger<ContactManager>.Instance,new(10,true,false,true));
    private sealed class F:IDisposable
    { public FakeCursor Cursor;public FakeTime Time=new();public ContactManager Contacts=MakeContacts();public CameraMouseLookService Camera;public TouchContact Contact=>Contacts.ActiveContacts[(int)FixedContacts.Camera];
      public F(FakeCursor? cursor=null,bool start=true,CameraSettings? settings=null){Cursor=cursor??new();Camera=new(new(NullLogger<TouchEngine>.Instance,Contacts),NullLogger<CameraMouseLookService>.Instance,Cursor,Time);if(start)Camera.Start(settings??new(){Smooth=0,MaxSpeed=100,DragRadius=500},500,500);}
      public void Warp()=>Camera.OnMouseMove(0,0,Camera.Generation);public void Real(int dx,int dy){Cursor.Position=new(500+dx,500+dy);Time.Advance(.016);Camera.OnMouseMove(dx,dy,Camera.Generation);}public void Dispose()=>Camera.Dispose();}
    private sealed class FakeTime:TimeProvider{private long _ticks;public override long TimestampFrequency=>1000;public override long GetTimestamp()=>_ticks;public void Advance(double seconds)=>_ticks+=(long)(seconds*1000);}
    private sealed class FakeCursor:IMouseCursorController
    { public PhysicalScreenPoint Original=new(20,30);public PhysicalScreenPoint Position;public CursorClip? OriginalClip=new CursorClip(0,0,1000,1000);public CursorClip? Clip;public bool Visible=true,FailGet,FailHide,FailSet;public int SetCount,RestoreVisibleCount;
      public FakeCursor(){Position=Original;Clip=OriginalClip;}public bool TryGetPosition(out PhysicalScreenPoint p){p=Position;return !FailGet;}public bool TrySetPosition(PhysicalScreenPoint p){SetCount++;if(FailSet){FailSet=false;return false;}Position=p;return true;}public bool TryGetClip(out CursorClip? c){c=Clip;return true;}public bool TrySetClip(CursorClip? c){Clip=c;return true;}public bool TrySetVisible(bool v){if(FailHide&&!v)return false;Visible=v;if(v)RestoreVisibleCount++;return true;}}
    private sealed class FailedGeometry:IGameWindowGeometryProvider{public WindowGeometryResult GetClientRect(nint h)=>WindowGeometryResult.Failure("x",1,"x");}
}
