using GameControlMapper.Models;
using GameControlMapper.Services;
using GameControlMapper.Win32;
using Microsoft.Extensions.Logging.Abstractions;
using System.Reflection;
using Xunit;
namespace GameControlMapper.Tests;
public sealed class CursorIsolationAndMixedInputTests
{
 [Fact]public void TouchPromotedMouseSignature_IsRecognized(){var info=new IntPtr(unchecked((long)NativeMethods.MI_WP_SIGNATURE));Assert.True(MouseHookService.IsTouchPromotedMouseEvent(info));}
 [Fact]public void PhysicalMouseExtraInfo_IsNotTouchPromotion()=>Assert.False(MouseHookService.IsTouchPromotedMouseEvent(IntPtr.Zero));
 [Fact]public void TouchPromotionSignature_AllowsLowByteFlags(){var info=new IntPtr(unchecked((long)(NativeMethods.MI_WP_SIGNATURE|0x80)));Assert.True(MouseHookService.IsTouchPromotedMouseEvent(info));}
 [Theory]
 [InlineData(0,0)]
 [InlineData(1920,1080)]
 [InlineData(-1920,250)]
 public void CursorIsolation_PreservesSignedPhysicalCoordinates(int x,int y){var point=new NativeMethods.POINT{X=x,Y=y};var restored=MouseHookService.Unpack(MouseHookService.Pack(point));Assert.Equal((x,y),(restored.X,restored.Y));}
 [Fact]public async Task CameraPath_CapturesOwnershipAndChangesOnlyVisibility()
 {
  using var touch=new BetaIntegrationFixture();var cursor=new RecordingCursor();using var camera=new CameraMouseLookService(touch.Engine,NullLogger<CameraMouseLookService>.Instance,cursor,scheduler:touch.Scheduler);
  Assert.True(camera.Start(new CameraSettings{Smooth=0,MaxSpeed=100,DragRadius=500},500,500));camera.OnMouseMove(10,0,camera.Generation);await touch.Frame();camera.Stop();
  Assert.Equal(1,cursor.PositionReads);Assert.Equal(1,cursor.ClipReads);Assert.Equal(0,cursor.PositionWrites);Assert.Equal(0,cursor.ClipWrites);Assert.Equal([false,true],cursor.VisibilityWrites);
 }
 [Fact]public void WasdAndMappedButtons_HaveNoCursorControllerDependency()
 {
  var fields=typeof(InputMappingEngine).GetFields(BindingFlags.Instance|BindingFlags.NonPublic);
  Assert.DoesNotContain(fields,field=>typeof(IMouseCursorController).IsAssignableFrom(field.FieldType));
 }
 [Fact]public void RawMouse_RelativeDeltaIsAccepted(){var mouse=new RawMouseInputSource.RAWMOUSE{LastX=12,LastY=-4};Assert.True(RawMouseInputSource.TryGetRelativeDelta(mouse,out var x,out var y));Assert.Equal((12,-4),(x,y));}
 [Fact]public void RawMouse_AbsolutePacketIsRejected(){var mouse=new RawMouseInputSource.RAWMOUSE{Flags=1,LastX=12};Assert.False(RawMouseInputSource.TryGetRelativeDelta(mouse,out _,out _));}
 [Fact]public void RawMouse_ZeroDeltaIsIgnored()=>Assert.False(RawMouseInputSource.TryGetRelativeDelta(new(),out _,out _));
 [Theory]
 [InlineData(NativeMethods.WM_XBUTTONDOWN,NativeMethods.XBUTTON1,NativeMethods.VK_XBUTTON1,true)]
 [InlineData(NativeMethods.WM_XBUTTONUP,NativeMethods.XBUTTON1,NativeMethods.VK_XBUTTON1,false)]
 [InlineData(NativeMethods.WM_XBUTTONDOWN,NativeMethods.XBUTTON2,NativeMethods.VK_XBUTTON2,true)]
 [InlineData(NativeMethods.WM_XBUTTONUP,NativeMethods.XBUTTON2,NativeMethods.VK_XBUTTON2,false)]
 public void SideMouseHook_DecodesButtonFromHighWord(int message,int button,int expectedVirtualKey,bool expectedDown){Assert.True(MouseHookService.TryGetMouseVirtualKey(message,button<<16,out var virtualKey,out var isDown));Assert.Equal(expectedVirtualKey,virtualKey);Assert.Equal(expectedDown,isDown);}
 [Fact]public async Task WasdAndFire_ProduceIndependentConcurrentContacts()
 {using var f=new BetaIntegrationFixture();var joystick=f.Acquire("joystick:move",200,800);await f.Frame();f.Engine.MoveTouch(joystick,200,740);var fire=f.Acquire("mouse-area:fire",1650,680);await f.Frame();var mixed=f.Backend.RecordedFrames.Last().Contacts;Assert.Contains(mixed,x=>x.ContactId==joystick.ContactId&&x.State==TouchState.Update);Assert.Contains(mixed,x=>x.ContactId==fire.ContactId&&x.State==TouchState.Down);await f.End(fire);Assert.Contains(f.Allocator.ActiveLeases,x=>x.ContactId==joystick.ContactId);await f.End(joystick);f.AssertClean();}
 [Fact]public async Task TwoTapContacts_DownInSameFrameUseUniqueIds(){using var f=new BetaIntegrationFixture();var a=f.Acquire("binding:a");var b=f.Acquire("binding:b");await f.Frame();var contacts=f.Backend.RecordedFrames.Single().Contacts;Assert.Equal(2,contacts.Select(x=>x.ContactId).Distinct().Count());Assert.All(contacts,x=>Assert.Equal(TouchState.Down,x.State));await f.Stop();}
 [Fact]public async Task CameraWasdFireAndAim_RemainConcurrentInOneFrame(){using var f=new BetaIntegrationFixture();var camera=f.Acquire("camera",960,540);var move=f.Acquire("joystick:move",137,959);var fire=f.Acquire("mouse-area:fire",1740,938);var aim=f.Acquire("mouse-area:aim",1587,1034);await f.Frame();var contacts=f.Backend.RecordedFrames.Single().Contacts;Assert.Equal(4,contacts.Count);Assert.Equal(4,contacts.Select(x=>x.ContactId).Distinct().Count());Assert.All(contacts,x=>Assert.Equal(TouchState.Down,x.State));await f.Stop();}
 [Fact]public async Task RapidMoveAndRelease_UsesLastSuccessfullyInjectedPositionForUp(){using var f=new BetaIntegrationFixture();var touch=f.Acquire("joystick:move",100,200);await f.Frame();f.Engine.MoveTouch(touch,300,400);f.Engine.EndTouch(touch);await f.Frame();var up=Assert.Single(f.Backend.RecordedFrames.Last().Contacts);Assert.Equal(TouchState.Up,up.State);Assert.Equal((100d,200d),(up.X,up.Y));f.AssertClean();}
 private sealed class RecordingCursor:IMouseCursorController
 {
  public int PositionReads,ClipReads,PositionWrites,ClipWrites;public List<bool> VisibilityWrites{get;}=[];
  public bool TryGetPosition(out PhysicalScreenPoint point){PositionReads++;point=new(20,30);return true;}
  public bool TrySetPosition(PhysicalScreenPoint point){PositionWrites++;return true;}
  public bool TryGetClip(out CursorClip? clip){ClipReads++;clip=new(0,0,1000,1000);return true;}
  public bool TrySetClip(CursorClip? clip){ClipWrites++;return true;}
  public bool TryGetVisible(out bool visible){visible=VisibilityWrites.Count==0;return true;}
  public bool TrySetVisible(bool visible){VisibilityWrites.Add(visible);return true;}
 }
}
