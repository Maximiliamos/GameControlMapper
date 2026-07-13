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
 [Fact]public void CameraPath_DoesNotMoveClipOrHideSystemCursor(){var source=Source("src/GameControlMapper/Services/CameraMouseLookService.cs");Assert.DoesNotContain("TrySetPosition",source);Assert.DoesNotContain("TrySetClip",source);Assert.DoesNotContain("TrySetVisible",source);Assert.DoesNotContain("TryGetPosition",source);}
 [Fact]public void RawMouse_RelativeDeltaIsAccepted(){var mouse=new RawMouseInputSource.RAWMOUSE{LastX=12,LastY=-4};Assert.True(RawMouseInputSource.TryGetRelativeDelta(mouse,out var x,out var y));Assert.Equal((12,-4),(x,y));}
 [Fact]public void RawMouse_AbsolutePacketIsRejected(){var mouse=new RawMouseInputSource.RAWMOUSE{Flags=1,LastX=12};Assert.False(RawMouseInputSource.TryGetRelativeDelta(mouse,out _,out _));}
 [Fact]public void RawMouse_ZeroDeltaIsIgnored()=>Assert.False(RawMouseInputSource.TryGetRelativeDelta(new(),out _,out _));
 [Fact]public async Task WasdAndFire_ProduceIndependentConcurrentContacts()
 {using var f=new BetaIntegrationFixture();var joystick=f.Acquire("joystick:move",200,800);await f.Frame();f.Engine.MoveTouch(joystick,200,740);var fire=f.Acquire("mouse-area:fire",1650,680);await f.Frame();var mixed=f.Backend.RecordedFrames.Last().Contacts;Assert.Contains(mixed,x=>x.ContactId==joystick.ContactId&&x.State==TouchState.Update);Assert.Contains(mixed,x=>x.ContactId==fire.ContactId&&x.State==TouchState.Down);await f.End(fire);Assert.Contains(f.Allocator.ActiveLeases,x=>x.ContactId==joystick.ContactId);await f.End(joystick);f.AssertClean();}
 [Fact]public async Task TwoTapContacts_DownInSameFrameUseUniqueIds(){using var f=new BetaIntegrationFixture();var a=f.Acquire("binding:a");var b=f.Acquire("binding:b");await f.Frame();var contacts=f.Backend.RecordedFrames.Single().Contacts;Assert.Equal(2,contacts.Select(x=>x.ContactId).Distinct().Count());Assert.All(contacts,x=>Assert.Equal(TouchState.Down,x.State));await f.Stop();}
 private static string Source(string relative){var d=new DirectoryInfo(AppContext.BaseDirectory);while(d is not null&&!File.Exists(Path.Combine(d.FullName,"GameControlMapper.sln")))d=d.Parent;return File.ReadAllText(Path.Combine(d!.FullName,relative.Replace('/',Path.DirectorySeparatorChar)));}
}
