using GameControlMapper.TouchTestHarness;
using Xunit;

namespace GameControlMapper.Tests;
public sealed class TouchLifecycleTrackerTests
{
 [Fact]public void Down_AddsActiveContact(){var t=N();t.Process(1,2,3,HarnessTouchState.Down);Assert.Contains(1,t.ActiveContacts.Keys);}
 [Fact]public void Move_UpdatesExistingContact(){var t=Down();t.Process(1,8,9,HarnessTouchState.Move);Assert.Equal((8d,9d),(t.ActiveContacts[1].X,t.ActiveContacts[1].Y));}
 [Fact]public void Up_RemovesActiveContact(){var t=Down();t.Process(1,2,3,HarnessTouchState.Up);Assert.Empty(t.ActiveContacts);}
 [Fact]public void RepeatedDown_IsProtocolError(){var t=Down();Assert.NotNull(t.Process(1,0,0,HarnessTouchState.Down).ProtocolError);}
 [Fact]public void MoveWithoutDown_IsProtocolError(){var t=N();Assert.NotNull(t.Process(1,0,0,HarnessTouchState.Move).ProtocolError);}
 [Fact]public void UpWithoutDown_IsProtocolError(){var t=N();Assert.NotNull(t.Process(1,0,0,HarnessTouchState.Up).ProtocolError);}
 [Fact]public void RepeatedUp_IsProtocolError(){var t=Down();t.Process(1,0,0,HarnessTouchState.Up);Assert.NotNull(t.Process(1,0,0,HarnessTouchState.Up).ProtocolError);}
 [Fact]public void MultipleContacts_AreTrackedIndependently(){var t=N();t.Process(1,0,0,HarnessTouchState.Down);t.Process(2,0,0,HarnessTouchState.Down);t.Process(1,0,0,HarnessTouchState.Up);Assert.Equal([2],t.ActiveContacts.Keys);}
 [Fact]public void MaximumConcurrentContacts_IsRecorded(){var t=N();for(var i=0;i<4;i++)t.Process(i,0,0,HarnessTouchState.Down);Assert.Equal(4,t.MaximumConcurrentContacts);}
 [Fact]public void Reset_ClearsCountersAndContacts(){var t=Down();t.Reset();Assert.Empty(t.ActiveContacts);Assert.Equal(0,t.DownCount);}
 [Fact]public void EventLog_IsBounded(){var t=new TouchLifecycleTracker(3);for(var i=0;i<10;i++)t.Process(i,0,0,HarnessTouchState.Down);Assert.Equal(3,t.Events.Count);}
 [Fact]public void ProtocolErrors_AppearInExport(){var t=N();t.Process(1,0,0,HarnessTouchState.Up);Assert.Contains("PROTOCOL ERROR",t.Export("x","w",96,1,"g").Text);}
 [Fact]public void Export_WithNoActiveContacts_IsPass(){Assert.True(N().Export("x","w",96,1,"g").Passed);}
 [Fact]public void Export_WithActiveContacts_IsFail(){Assert.False(Down().Export("x","w",96,1,"g").Passed);}
 [Fact]public void ContactCoordinates_ArePreserved(){var t=N();t.Process(1,12.5,18.25,HarnessTouchState.Down);Assert.Equal((12.5,18.25),(t.ActiveContacts[1].X,t.ActiveContacts[1].Y));}
 [Fact]public void NegativeScreenCoordinates_ArePreserved(){var tracker=N();tracker.Process(1,-1919.5,-240.25,HarnessTouchState.Down);Assert.Equal((-1919.5,-240.25),(tracker.ActiveContacts[1].X,tracker.ActiveContacts[1].Y));}
 [Fact]public void TenConcurrentContacts_AreAccepted(){var t=N();for(var i=0;i<10;i++)t.Process(i,i,i,HarnessTouchState.Down);Assert.Equal(10,t.ActiveContacts.Count);}
 [Fact]public void EleventhContact_IsRecordedWithoutCrashing(){var t=N();for(var i=0;i<11;i++)t.Process(i,i,i,HarnessTouchState.Down);Assert.Equal(11,t.ActiveContacts.Count);Assert.Equal(11,t.MaximumConcurrentContacts);}
 private static TouchLifecycleTracker N()=>new();private static TouchLifecycleTracker Down(){var t=N();t.Process(1,2,3,HarnessTouchState.Down);return t;}
}
