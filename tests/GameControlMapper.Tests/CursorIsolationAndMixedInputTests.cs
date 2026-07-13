using GameControlMapper.Models;
using GameControlMapper.Services;
using GameControlMapper.Win32;
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
 [Fact]public void CameraPath_HidesButNeverMovesReadsOrClipsCursor(){var source=Source("src/GameControlMapper/Services/CameraMouseLookService.cs");Assert.DoesNotContain("TrySetPosition",source);Assert.DoesNotContain("TrySetClip",source);Assert.Contains("TrySetVisible(false)",source);Assert.Contains("TrySetVisible(true)",source);Assert.DoesNotContain("TryGetPosition",source);}
 [Fact]public void WasdAndMappedButtons_DoNotUseCursorCoordinates(){var source=Source("src/GameControlMapper/Services/InputMappingEngine.cs");Assert.DoesNotContain("GetCursorPosition",source);Assert.DoesNotContain("MouseMoveTo",source);Assert.DoesNotContain("MoveRelative",source);Assert.DoesNotContain("SetCursorPos",source);}
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
 private static string Source(string relative){var d=new DirectoryInfo(AppContext.BaseDirectory);while(d is not null&&!File.Exists(Path.Combine(d.FullName,"GameControlMapper.sln")))d=d.Parent;return File.ReadAllText(Path.Combine(d!.FullName,relative.Replace('/',Path.DirectorySeparatorChar)));}
}
