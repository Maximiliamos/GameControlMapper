using System.Reflection;
using System.Windows.Input;
using GameControlMapper.Models;
using GameControlMapper.Services;
using GameControlMapper.Win32;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameControlMapper.Tests;

public sealed class WindowCoordinateIntegrationTests
{
    [Theory]
    [InlineData(BindingKind.Macro)]
    [InlineData(BindingKind.Sequence)]
    public async Task UnsupportedBinding_IsNotAddedToSuppressionSnapshot(BindingKind kind)
    {
        using var fixture=new Fixture(new(0,0,1000,500));fixture.StartBinding(kind);
        var q=KeyInterop.VirtualKeyFromKey(Key.Q);
        Assert.False(fixture.Mapping.CurrentInputPermission.SuppressedKeys.Contains(q));
        Assert.False(fixture.KeyboardHook.ShouldSuppressKey!(q));
        fixture.Press(Key.Q);await fixture.Scheduler.SendFrameOnceAsync();
        Assert.Empty(fixture.Contacts.ActiveContacts);Assert.Empty(fixture.Backend.Contacts(TouchState.Down));
    }

    [Fact]public async Task UnsupportedMacro_IsNotAddedToSuppressionSnapshot()=>await UnsupportedBinding_IsNotAddedToSuppressionSnapshot(BindingKind.Macro);
    [Fact]public async Task UnsupportedSequence_IsNotAddedToSuppressionSnapshot()=>await UnsupportedBinding_IsNotAddedToSuppressionSnapshot(BindingKind.Sequence);
    [Fact]public void UnsupportedInputMode_IsNotSuppressed(){using var f=new Fixture(new(0,0,1000,500));f.StartBinding(BindingKind.Tap,GameControlMapper.Models.InputMode.RawInput);Assert.False(f.Mapping.IsActive);Assert.False(f.Mapping.CurrentInputPermission.AllowSuppression);}
    [Fact]public async Task UnsupportedBinding_DoesNotCallSendInput(){using var f=new Fixture(new(0,0,1000,500));f.StartBinding(BindingKind.Macro);f.Press(Key.Q);await Task.Delay(75);Assert.Empty(f.Backend.Contacts(TouchState.Down));}
    [Fact]public async Task UnsupportedBinding_DoesNotAcquireTouchLease(){using var f=new Fixture(new(0,0,1000,500));f.StartBinding(BindingKind.Sequence);f.Press(Key.Q);await Task.Delay(75);Assert.Empty(f.Contacts.ActiveContacts);}
    [Fact]public void UnsupportedBinding_DoesNotBlockPhysicalKey(){using var f=new Fixture(new(0,0,1000,500));f.StartBinding(BindingKind.Macro);Assert.False(f.KeyboardHook.ShouldSuppressKey!(KeyInterop.VirtualKeyFromKey(Key.Q)));}
    [Fact]public void SupportedTap_IsStillSuppressedWhileMappingActive(){using var f=new Fixture(new(0,0,1000,500));f.StartBinding(BindingKind.Tap);Assert.True(f.KeyboardHook.ShouldSuppressKey!(KeyInterop.VirtualKeyFromKey(Key.Q)));}
    [Fact]public async Task SuppressionIsDeniedDuringStop(){using var f=new Fixture(new(0,0,1000,500));f.StartBinding(BindingKind.Tap);var stop=f.Mapping.StopAsync("test stop");Assert.False(f.Mapping.CurrentInputPermission.AllowSuppression);await stop;}
    [Fact]public async Task SuppressionIsDeniedImmediatelyOnFocusLoss(){using var f=new Fixture(new(0,0,1000,500));f.StartBinding(BindingKind.Tap);var stop=f.Mapping.StopAsync("focus loss");Assert.False(f.KeyboardHook.ShouldSuppressKey!(KeyInterop.VirtualKeyFromKey(Key.Q)));await stop;}
    [Fact]public void ActivationMonitorFocusLoss_ReleasesContactsAndFailsClosed()
    {
        using var fixture=new Fixture(new(0,0,1000,500));fixture.StartJoystick();Assert.True(fixture.Backend.WaitForState(TouchState.Down));
        fixture.ActivationNative.ForegroundRoot=2;fixture.ActivationNative.ForegroundProcessId=2;fixture.ActivationNative.Raise();
        Assert.True(SpinWait.SpinUntil(()=>!fixture.Mapping.IsActive,TimeSpan.FromSeconds(2)));
        Assert.True(fixture.Backend.WaitForState(TouchState.Up));Assert.False(fixture.Mapping.CurrentInputPermission.AllowSuppression);
        Assert.True(SpinWait.SpinUntil(()=>fixture.Diagnostics.Last.StopReason is not null,TimeSpan.FromSeconds(2)));
        Assert.Equal("focus loss",fixture.Diagnostics.Last.StopReason);
    }
    [Fact]public void ActivationMonitorTargetPidChange_ReleasesContactsAndFailsClosed()
    {
        using var fixture=new Fixture(new(0,0,1000,500));fixture.StartJoystick();Assert.True(fixture.Backend.WaitForState(TouchState.Down));
        fixture.ActivationNative.ForegroundProcessId=99;fixture.ActivationNative.Raise();
        Assert.True(SpinWait.SpinUntil(()=>!fixture.Mapping.IsActive,TimeSpan.FromSeconds(2)));Assert.True(fixture.Backend.WaitForState(TouchState.Up));Assert.False(fixture.Mapping.CurrentInputPermission.AllowMappedInput);
    }
    [Fact]public async Task SuppressionIsDeniedForStaleGeneration(){using var f=new Fixture(new(0,0,1000,500));f.StartBinding(BindingKind.Tap);var stale=f.Mapping.CurrentInputPermission.Generation-1;f.GeneratedPress(Key.Q,stale);await Task.Delay(75);Assert.Empty(f.Backend.Contacts(TouchState.Down));}

    [Theory]
    [InlineData(BindingKind.Tap,1)]
    [InlineData(BindingKind.DoubleTap,2)]
    [InlineData(BindingKind.Hold,1)]
    [InlineData(BindingKind.Swipe,1)]
    public async Task KeyboardAutoRepeat_ProducesOneAction(BindingKind kind,int expectedDowns)
    {
        using var f=new Fixture(new(0,0,1000,500));f.StartBinding(kind);f.Press(Key.Q);for(var i=0;i<50;i++)f.Press(Key.Q);f.Release(Key.Q);
        Assert.True(SpinWait.SpinUntil(()=>f.Backend.Contacts(TouchState.Down).Count>=expectedDowns,TimeSpan.FromSeconds(2)));
        await Task.Delay(250);Assert.Equal(expectedDowns,f.Backend.Contacts(TouchState.Down).Count);
    }
    [Fact]public Task Tap_AutoRepeatProducesOneTap()=>KeyboardAutoRepeat_ProducesOneAction(BindingKind.Tap,1);
    [Fact]public Task DoubleTap_AutoRepeatProducesOneDoubleTap()=>KeyboardAutoRepeat_ProducesOneAction(BindingKind.DoubleTap,2);
    [Fact]public Task Hold_AutoRepeatProducesOneLifecycle()=>KeyboardAutoRepeat_ProducesOneAction(BindingKind.Hold,1);
    [Fact]public Task Swipe_AutoRepeatProducesOneLifecycle()=>KeyboardAutoRepeat_ProducesOneAction(BindingKind.Swipe,1);
    [Fact]public async Task MouseArea_RepeatedDownProducesOneContact(){using var f=new Fixture(new(0,0,1000,500));f.StartBinding(BindingKind.MouseArea);f.Press(Key.Q);f.Press(Key.Q);await f.Scheduler.SendFrameOnceAsync();Assert.Single(f.Backend.Contacts(TouchState.Down));}
    [Fact]public async Task Joystick_AutoRepeatDoesNotCreateAdditionalLease(){using var f=new Fixture(new(0,0,1000,500));f.StartJoystick();f.Press(Key.W);await f.Scheduler.SendFrameOnceAsync();Assert.Single(f.Contacts.ActiveContacts);}
    [Fact]public async Task QueuedAction_DoesNotStartAfterKeyUp(){using var f=new Fixture(new(0,0,1000,500));f.StartBinding(BindingKind.Hold);f.Press(Key.Q);for(var i=0;i<100;i++)f.Press(Key.Q);f.Release(Key.Q);await Task.Delay(350);Assert.Single(f.Backend.Contacts(TouchState.Down));}
    [Fact]public async Task QueuedAction_DoesNotStartAfterStop(){using var f=new Fixture(new(0,0,1000,500));f.StartBinding(BindingKind.Hold);f.Press(Key.Q);for(var i=0;i<100;i++)f.Press(Key.Q);await f.Mapping.StopAsync();var downs=f.Backend.Contacts(TouchState.Down).Count;await Task.Delay(250);Assert.Equal(downs,f.Backend.Contacts(TouchState.Down).Count);}
    [Fact]public async Task QueuedAction_DoesNotEnterNextGeneration(){using var f=new Fixture(new(0,0,1000,500));f.StartBinding(BindingKind.Hold);f.Press(Key.Q);for(var i=0;i<100;i++)f.Press(Key.Q);await f.Mapping.StopAsync();var before=f.Backend.Contacts(TouchState.Down).Count;f.StartBinding(BindingKind.Tap);f.Press(Key.Q);await Task.Delay(200);Assert.Equal(before+1,f.Backend.Contacts(TouchState.Down).Count);}
    [Fact]public async Task RapidPressRelease_DoesNotLeaveActionLockOccupied(){using var f=new Fixture(new(0,0,1000,500));f.StartBinding(BindingKind.Tap);f.Press(Key.Q);f.Release(Key.Q);await Task.Delay(150);Assert.Equal(0,f.Mapping.RunningActionCount);}
    [Fact]public async Task RepeatedInput_DoesNotGrowPendingTaskCount(){using var f=new Fixture(new(0,0,1000,500));f.StartBinding(BindingKind.Hold);f.Press(Key.Q);for(var i=0;i<10_000;i++)f.Press(Key.Q);Assert.InRange(f.Mapping.RunningActionCount,0,1);f.Release(Key.Q);await f.Mapping.StopAsync();}

    [Fact]public void SchedulerFatalFailure_StopsMapping(){using var f=new Fixture(new(0,0,1000,500));f.CauseSchedulerFailure();Assert.False(f.Mapping.IsActive);}
    [Fact]public void SchedulerFatalFailure_DisablesSuppression(){using var f=new Fixture(new(0,0,1000,500));f.CauseSchedulerFailure();Assert.False(f.Mapping.CurrentInputPermission.AllowSuppression);Assert.False(f.KeyboardHook.ShouldSuppressKey!(KeyInterop.VirtualKeyFromKey(Key.W)));}
    [Fact]public void SchedulerFatalFailure_StopsCamera(){using var f=new Fixture(new(0,0,1920,1080));f.StartCamera();f.Press(Key.LeftCtrl);f.Release(Key.LeftCtrl);f.Camera.OnMouseMove(10,0,f.Camera.Generation);Assert.True(f.Backend.WaitForState(TouchState.Down));f.Backend.ThrowNextFrame=true;f.Camera.OnMouseMove(10,0,f.Camera.Generation);Assert.True(SpinWait.SpinUntil(()=>!f.Mapping.IsActive,TimeSpan.FromSeconds(2)));Assert.False(f.Camera.IsActive);}
    [Fact]public void SchedulerFatalFailure_AttemptsFinalUp(){using var f=new Fixture(new(0,0,1000,500));f.CauseSchedulerFailure();Assert.True(f.Backend.WaitForState(TouchState.Up));}
    [Fact]public void SchedulerFatalFailure_ProducesStopReason(){using var f=new Fixture(new(0,0,1000,500));f.CauseSchedulerFailure();Assert.Equal("scheduler failure",f.Diagnostics.Last.StopReason);}
    [Fact]public void SchedulerFatalFailure_DoesNotDeadlock(){using var f=new Fixture(new(0,0,1000,500));var started=DateTime.UtcNow;f.CauseSchedulerFailure();Assert.True(DateTime.UtcNow-started<TimeSpan.FromSeconds(3));}
    [Fact]public async Task SchedulerFatalFailure_ConcurrentFocusLossUsesSingleStopTask(){using var f=new Fixture(new(0,0,1000,500));f.StartJoystick();Assert.True(f.Backend.WaitForState(TouchState.Down));f.Backend.ThrowNextFrame=true;f.Press(Key.D);var focusStop=f.Mapping.StopAsync("focus loss");await focusStop;Assert.False(f.Mapping.IsActive);Assert.False(f.Mapping.CurrentInputPermission.AllowSuppression);}
    [Fact]public async Task SchedulerFatalFailure_DoesNotRestartWorkerAutomatically(){using var f=new Fixture(new(0,0,1000,500));f.CauseSchedulerFailure();var frames=f.Backend.FrameCount;f.Scheduler.Resume();await Task.Delay(50);Assert.Equal(frames,f.Backend.FrameCount);Assert.True(f.Scheduler.HasFatalFailure);}
    [Fact]public async Task SchedulerFatalFailure_RejectsLateInput(){using var f=new Fixture(new(0,0,1000,500));f.CauseSchedulerFailure();var frames=f.Backend.FrameCount;f.Press(Key.W);await Task.Delay(50);Assert.Equal(frames,f.Backend.FrameCount);}
    [Fact]public async Task SchedulerFatalFailure_IsRateLimitedInLogs(){using var f=new Fixture(new(0,0,1000,500));var raised=0;f.Scheduler.FatalSchedulerFailure+=(_,_)=>Interlocked.Increment(ref raised);f.CauseSchedulerFailure();f.Backend.ThrowNextFrame=true;await f.Scheduler.SendFrameOnceAsync();Assert.Equal(1,raised);}

    [Fact]
    public async Task CtrlPress_TogglesCameraAndCtrlReleaseKeepsItActive()
    {
        using var fixture = new Fixture(new(0, 0, 1920, 1080));
        fixture.StartCamera();

        fixture.Press(Key.LeftCtrl);
        Assert.True(fixture.Camera.IsActive);
        Assert.True(fixture.MouseHook.CaptureMovement);
        Assert.Empty(fixture.Contacts.ActiveContacts); // Ctrl arms camera without a stationary tap.
        fixture.Camera.OnMouseMove(10, 0, fixture.Camera.Generation);
        await fixture.Scheduler.SendFrameOnceAsync();
        Assert.True(fixture.Backend.WaitForState(TouchState.Down));
        fixture.Press(Key.LeftCtrl); // low-level auto-repeat must not toggle the mode
        Assert.True(fixture.Camera.IsActive);
        fixture.Release(Key.LeftCtrl);
        Assert.True(fixture.Camera.IsActive);
        Assert.True(fixture.MouseHook.CaptureMovement);
        fixture.Press(Key.LeftCtrl);
        Assert.False(fixture.Camera.IsActive);
        Assert.False(fixture.MouseHook.CaptureMovement);
        Assert.True(fixture.Backend.WaitForState(TouchState.Up));

        await fixture.Mapping.StopAsync();
    }

    [Fact]
    public async Task LeftMouseFire_IsEnabledOnlyWhileCameraCombatModeIsActive()
    {
        using var fixture = new Fixture(new(0, 0, 1920, 1080));
        fixture.StartCameraWithFire();

        Assert.False(fixture.MouseHook.ShouldSuppressButton!(NativeMethods.VK_LBUTTON));
        fixture.PressMouse(NativeMethods.VK_LBUTTON);
        Assert.Empty(fixture.Contacts.ActiveContacts);
        fixture.ReleaseMouse(NativeMethods.VK_LBUTTON);

        fixture.Press(Key.LeftCtrl);
        fixture.Release(Key.LeftCtrl);
        Assert.True(fixture.MouseHook.ShouldSuppressButton!(NativeMethods.VK_LBUTTON));
        fixture.Camera.OnMouseMove(10, 0, fixture.Camera.Generation);
        await fixture.Scheduler.SendFrameOnceAsync();
        fixture.PressMouse(NativeMethods.VK_LBUTTON);
        Assert.Equal(2, fixture.Contacts.ActiveContacts.Count);

        fixture.Press(Key.LeftCtrl);
        fixture.Release(Key.LeftCtrl);
        Assert.False(fixture.MouseHook.ShouldSuppressButton!(NativeMethods.VK_LBUTTON));
        await fixture.Scheduler.SendFrameOnceAsync();
        Assert.Empty(fixture.Contacts.ActiveContacts);
        await fixture.Mapping.StopAsync();
    }

    [Fact]
    public async Task SelectedWindowOrigin_IsAddedToMappedTouch()
    {
        using var fixture = new Fixture(new(100, 200, 2000, 1000));
        fixture.Start(new ProfilePoint(100, 50));

        fixture.Press(Key.Q);
        Assert.True(fixture.Backend.WaitForState(TouchState.Down));

        Assert.Equal(new PhysicalScreenPoint(300, 300), fixture.Backend.FirstPoint(TouchState.Down));
        await fixture.Mapping.StopAsync();
    }

    [Fact]
    public async Task NegativeMonitorOrigin_IsSentUnchangedToTouchEngine()
    {
        using var fixture = new Fixture(new(-1000, -500, 1000, 500));
        fixture.Start(new ProfilePoint(100, 50));

        fixture.Press(Key.Q);
        await fixture.Scheduler.SendFrameOnceAsync();

        Assert.Equal(new PhysicalScreenPoint(-900, -450), fixture.Backend.FirstPoint(TouchState.Down));
        await fixture.Mapping.StopAsync();
    }

    [Theory]
    [InlineData("MouseX1", NativeMethods.VK_XBUTTON1)]
    [InlineData("MouseX2", NativeMethods.VK_XBUTTON2)]
    public async Task SideMouseButton_ExecutesMappedTouch(string hotkey, int virtualKey)
    {
        using var fixture = new Fixture(new(100, 200, 1000, 500));
        fixture.Start(new ProfilePoint(100, 50), hotkey);

        fixture.PressMouse(virtualKey);
        await fixture.Scheduler.SendFrameOnceAsync();

        Assert.Equal(new PhysicalScreenPoint(200, 250), fixture.Backend.FirstPoint(TouchState.Down));
        await fixture.Mapping.StopAsync();
    }

    [Fact]
    public async Task StretchDifferentAspectRatio_UsesIndependentAxes()
    {
        using var fixture = new Fixture(new(0, 0, 1600, 1200));
        fixture.Start(new ProfilePoint(250, 100));

        fixture.Press(Key.Q);
        Assert.True(fixture.Backend.WaitForState(TouchState.Down));

        Assert.Equal(new PhysicalScreenPoint(400, 240), fixture.Backend.FirstPoint(TouchState.Down));
        await fixture.Mapping.StopAsync();
    }

    [Fact]
    public void StartWithoutSelectedWindow_IsRejected()
    {
        using var fixture = new Fixture(new(0, 0, 1000, 500));
        fixture.Profile.Window.WindowHandle = 0;

        fixture.Mapping.Start();

        Assert.False(fixture.Mapping.IsActive);
        Assert.Null(fixture.Session.Current);
    }

    [Fact]
    public void StartWithMinimizedWindow_IsRejected()
    {
        using var fixture = new Fixture(new(0, 0, 1000, 500));
        fixture.Geometry.Result = WindowGeometryResult.Failure("IsIconic", 0, "Window is minimized.");

        fixture.Mapping.Start();

        Assert.False(fixture.Mapping.IsActive);
        Assert.Null(fixture.Session.Current);
    }

    [Fact]
    public async Task OutsideProfilePoint_DoesNotCreateTouchContact()
    {
        using var fixture = new Fixture(new(0, 0, 1000, 500));
        fixture.Start(new ProfilePoint(1000, 100));

        fixture.Press(Key.Q);
        await Task.Delay(100);

        Assert.Empty(fixture.Backend.Contacts(TouchState.Down));
        Assert.Empty(fixture.Contacts.GetActiveContacts());
        await fixture.Mapping.StopAsync();
    }

    [Fact]
    public async Task WindowMovesDuringActiveContact_ReleasesAndStopsSession()
    {
        using var fixture = new Fixture(new(0, 0, 1000, 500));
        fixture.StartJoystick();
        Assert.True(fixture.Backend.WaitForState(TouchState.Down));

        fixture.Geometry.Result = WindowGeometryResult.Success(new(100, 100, 1000, 500));
        fixture.GeometryEvents.Raise(1);
        Assert.True(fixture.Backend.WaitForState(TouchState.Up));

        Assert.False(fixture.Mapping.IsActive);
        Assert.False(fixture.Session.Current!.IsActive);
        Assert.Single(fixture.Backend.Contacts(TouchState.Up));
        Assert.Empty(fixture.Backend.ContactsAfterFirstUp(TouchState.Update));
        await fixture.Mapping.StopAsync();
    }

    [Fact]
    public Task DestroyedWindowDuringContact_ReleasesAndStops() =>
        AssertInvalidWindowStops(WindowGeometryResult.Failure("IsWindow", 1400, "destroyed"));

    [Fact]
    public Task MinimizedWindowDuringContact_ReleasesAndStops() =>
        AssertInvalidWindowStops(WindowGeometryResult.Failure("IsIconic", 0, "minimized"));

    [Fact]
    public Task GeometryReadFailureDuringContact_ReleasesAndStops() =>
        AssertInvalidWindowStops(WindowGeometryResult.Failure("GetClientRect", 5, "failed"));

    private static async Task AssertInvalidWindowStops(WindowGeometryResult failure)
    {
        using var fixture = new Fixture(new(0, 0, 1000, 500));
        fixture.StartJoystick();
        Assert.True(fixture.Backend.WaitForState(TouchState.Down));
        fixture.Geometry.Result = failure;
        fixture.GeometryEvents.Raise(1);

        Assert.True(fixture.Backend.WaitForState(TouchState.Up));

        Assert.False(fixture.Mapping.IsActive);
        Assert.Single(fixture.Backend.Contacts(TouchState.Up));
        await fixture.Mapping.StopAsync();
    }

    [Fact]
    public async Task ManualStopConcurrentWithGeometryInvalidation_SendsOneFinalUp()
    {
        using var fixture = new Fixture(new(0, 0, 1000, 500));
        fixture.StartJoystick();
        Assert.True(fixture.Backend.WaitForState(TouchState.Down));
        fixture.Geometry.Result = WindowGeometryResult.Failure("IsWindow", 1400, "destroyed");
        fixture.GeometryEvents.Raise(1);

        var validation = fixture.Scheduler.SendFrameOnceAsync();
        var manualStop = fixture.Mapping.StopAsync();
        await Task.WhenAll(validation, manualStop);

        Assert.Single(fixture.Backend.Contacts(TouchState.Up));
        Assert.False(fixture.Mapping.IsActive);
    }

    [Fact]
    public async Task SecondStartAfterWindowMove_UsesNewGeometry()
    {
        using var fixture = new Fixture(new(0, 0, 1000, 500));
        fixture.Start(new ProfilePoint(100, 50));
        fixture.Press(Key.Q);
        Assert.True(fixture.Backend.WaitForFrameCount(1));
        await fixture.Mapping.StopAsync();
        var previousFrames = fixture.Backend.FrameCount;
        fixture.Geometry.Result = WindowGeometryResult.Success(new(500, 300, 1000, 500));

        fixture.Mapping.Start();
        fixture.Press(Key.Q);
        Assert.True(fixture.Backend.WaitForFrameCount(previousFrames + 1));

        Assert.Equal(new PhysicalScreenPoint(600, 350), fixture.Backend.LastPoint(TouchState.Down));
        await fixture.Mapping.StopAsync();
    }

    [Fact]
    public async Task LegacyPrimaryScreenScaling_IsNotCalledByProductionTouchPath()
    {
        using var fixture = new Fixture(new(321, 123, 1000, 500));
        fixture.Start(new ProfilePoint(0, 0));

        fixture.Press(Key.Q);
        Assert.True(fixture.Backend.WaitForState(TouchState.Down));

        Assert.Equal(new PhysicalScreenPoint(321, 123), fixture.Backend.FirstPoint(TouchState.Down));
        Assert.DoesNotContain("CoordinateScaler", typeof(InputMappingEngine).GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Select(field => field.FieldType.Name));
        await fixture.Mapping.StopAsync();
    }

    private sealed class Fixture : IDisposable
    {
        public MutableGeometryProvider Geometry { get; }
        public TargetWindowSessionManager Session { get; }
        public FakeGeometryEvents GeometryEvents { get; } = new();
        public ContactManager Contacts { get; }
        public TouchEngine TouchEngine { get; }
        public RecordingBackend Backend { get; } = new();
        public TouchScheduler Scheduler { get; }
        public MouseHookService MouseHook { get; }
        public KeyboardHookService KeyboardHook { get; }
        public CameraMouseLookService Camera { get; }
        public InputMappingEngine Mapping { get; }
        public MapperProfile Profile { get; }
        public MappingSessionDiagnostics Diagnostics { get; }=new();
        public FakeActivationNative ActivationNative { get; }=new();
        public TargetWindowActivationMonitor ActivationMonitor { get; }
        private readonly TargetWindowGeometryMonitor _geometryMonitor;

        public Fixture(PhysicalClientRect rect)
        {
            Geometry = new MutableGeometryProvider(rect);
            var native = new FakeWindowNative();
            _geometryMonitor = new TargetWindowGeometryMonitor(Geometry, native, GeometryEvents, TimeProvider.System);
            ActivationMonitor=new TargetWindowActivationMonitor(ActivationNative,NullLogger<TargetWindowActivationMonitor>.Instance);
            Session = new TargetWindowSessionManager(Geometry, NullLogger<TargetWindowSessionManager>.Instance,native,ActivationMonitor,_geometryMonitor);
            Contacts = new ContactManager(NullLogger<ContactManager>.Instance, new TouchCapabilities(10, true, false, true));
            TouchEngine = new TouchEngine(NullLogger<TouchEngine>.Instance, Contacts);
            Scheduler = new TouchScheduler(NullLogger<TouchScheduler>.Instance, Contacts, Backend, new FrameContext(), Session);
            Scheduler.Start();
            Camera = new CameraMouseLookService(TouchEngine, NullLogger<CameraMouseLookService>.Instance, scheduler: Scheduler);
            MouseHook = new MouseHookService(NullLogger<MouseHookService>.Instance);
            KeyboardHook = new KeyboardHookService(NullLogger<KeyboardHookService>.Instance);
            Mapping = new InputMappingEngine(
                KeyboardHook,
                MouseHook,
                Camera,
                TouchEngine, Scheduler, new HotkeyParser(), Session,
                new WindowCoordinateTransformer(), NullLogger<InputMappingEngine>.Instance,ActivationMonitor,sessionDiagnostics:Diagnostics,startNativeHooks:false);
            Profile = MapperProfile.CreateDefault();
            Profile.ResolutionWidth = 1000;
            Profile.ResolutionHeight = 500;
            Profile.Gamepad.Enabled = false;
            Profile.Window.WindowHandle = 1;
            Mapping.SetProfile(Profile);
        }

        public void Start(ProfilePoint point, string hotkey = "Q")
        {
            Profile.Bindings = [new ControlBinding { Name = "Test", Hotkey = hotkey, Kind = BindingKind.Tap, X = point.X, Y = point.Y, Width = 0, Height = 0 }];
            Mapping.SetProfile(Profile);
            Mapping.Start();
        }

        public void StartBinding(BindingKind kind,GameControlMapper.Models.InputMode mode=GameControlMapper.Models.InputMode.SendInput)
        {
            Profile.InputMode=mode;
            Profile.Bindings=[new ControlBinding{Name="Audit action",Hotkey="Q",Kind=kind,X=100,Y=100,Width=100,Height=100,HoldMilliseconds=120}];
            Mapping.SetProfile(Profile);Mapping.Start();
        }

        public void StartJoystick()
        {
            Profile.Bindings = [new ControlBinding { Name = "Move", Hotkey = "WASD", Kind = BindingKind.Joystick, X = 100, Y = 100, Width = 100, Height = 100 }];
            Mapping.SetProfile(Profile);
            Mapping.Start();
            Press(Key.W);
        }

        public void StartCamera()
        {
            Profile.ResolutionWidth = 1920;
            Profile.ResolutionHeight = 1080;
            Profile.Bindings = [new ControlBinding { Name = "Камера", Hotkey = "LeftCtrl", Kind = BindingKind.Aim, X = 910, Y = 490, Width = 100, Height = 100 }];
            Mapping.SetProfile(Profile);
            Mapping.Start();
        }

        public void StartCameraWithFire()
        {
            Profile.ResolutionWidth = 1920;
            Profile.ResolutionHeight = 1080;
            Profile.Bindings =
            [
                new ControlBinding { Name = "Камера", Hotkey = "LeftCtrl", Kind = BindingKind.Aim, X = 910, Y = 490, Width = 100, Height = 100 },
                new ControlBinding { Name = "Огонь", Hotkey = "MouseLeft", Kind = BindingKind.MouseArea, X = 1600, Y = 800, Width = 100, Height = 100 }
            ];
            Mapping.SetProfile(Profile);
            Mapping.Start();
        }

        public void Press(Key key)
        {
            var method = typeof(InputMappingEngine).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!;
            method.Invoke(Mapping, [null, KeyInterop.VirtualKeyFromKey(key)]);
        }

        public void Release(Key key)
        {
            var method = typeof(InputMappingEngine).GetMethod("OnKeyUp", BindingFlags.Instance | BindingFlags.NonPublic)!;
            method.Invoke(Mapping, [null, KeyInterop.VirtualKeyFromKey(key)]);
        }

        public void GeneratedPress(Key key,long generation)
        {
            var method=typeof(InputMappingEngine).GetMethod("OnGeneratedKeyDown",BindingFlags.Instance|BindingFlags.NonPublic)!;
            method.Invoke(Mapping,[null,new GeneratedInputEvent(KeyInterop.VirtualKeyFromKey(key),generation)]);
        }

        public void CauseSchedulerFailure()
        {
            StartJoystick();
            if(!Backend.WaitForState(TouchState.Down))throw new TimeoutException("Initial joystick Down was not sent.");
            Backend.ThrowNextFrame=true;
            Press(Key.D);
            if(!SpinWait.SpinUntil(()=>!Mapping.IsActive,TimeSpan.FromSeconds(2)))throw new TimeoutException("Mapping did not stop after scheduler failure.");
            SpinWait.SpinUntil(()=>Diagnostics.Last.StopReason is not null,TimeSpan.FromSeconds(2));
        }

        public void PressMouse(int virtualKey)
        {
            var method = typeof(InputMappingEngine).GetMethod("OnMouseButtonDown", BindingFlags.Instance | BindingFlags.NonPublic)!;
            method.Invoke(Mapping, [null, virtualKey]);
        }

        public void ReleaseMouse(int virtualKey)
        {
            var method = typeof(InputMappingEngine).GetMethod("OnMouseButtonUp", BindingFlags.Instance | BindingFlags.NonPublic)!;
            method.Invoke(Mapping, [null, virtualKey]);
        }

        public void Dispose()
        {
            Mapping.Dispose();
            Scheduler.Dispose();
            ActivationMonitor.Dispose();
            _geometryMonitor.Dispose();
        }
    }

    private sealed class FakeGeometryEvents : ITargetWindowGeometryNativeAdapter
    {
        private Action<nint>? _callback;
        public nint Install(Action<nint> callback) { _callback = callback; return 1; }
        public void Uninstall(nint hook) => _callback = null;
        public void Raise(nint hwnd) => _callback?.Invoke(hwnd);
    }

    private sealed class FakeWindowNative : IGameWindowNativeAdapter
    {
        public bool IsWindow(nint h) => true; public bool IsWindowVisible(nint h) => true; public bool IsIconic(nint h) => false;
        public bool GetClientRect(nint h, out NativeClientRect r) { r = new(0,0,1,1); return true; }
        public bool ClientToScreen(nint h, ref PhysicalScreenPoint p) => true; public int GetLastError() => 0;
        public uint GetWindowProcessId(nint h) => 1;
        public nint GetAncestor(nint h,uint flags)=>1;
    }

    private sealed class FakeActivationNative:ITargetWindowActivationNativeAdapter
    {
        private Action<nint>? _callback;public nint ForegroundRoot{get;set;}=1;public uint ForegroundProcessId{get;set;}=1;
        public nint GetForegroundWindow()=>ForegroundRoot;public nint GetRootWindow(nint hwnd)=>ForegroundRoot;public uint GetProcessId(nint hwnd)=>ForegroundProcessId;
        public bool IsWindow(nint hwnd)=>hwnd!=0;public bool IsIconic(nint hwnd)=>false;
        public nint InstallForegroundHook(Action<nint> callback){_callback=callback;return 1;}public void UninstallForegroundHook(nint hook){_callback=null;}public void Raise()=>_callback?.Invoke(ForegroundRoot);
    }

    private sealed class MutableGeometryProvider : IGameWindowGeometryProvider
    {
        public WindowGeometryResult Result { get; set; }
        public MutableGeometryProvider(PhysicalClientRect rect) => Result = WindowGeometryResult.Success(rect);
        public WindowGeometryResult GetClientRect(nint windowHandle) => Result;
    }

    private sealed class RecordingBackend : ITouchBackend
    {
        private readonly object _gate = new();
        private readonly List<TouchFrameSnapshot> _frames = [];
        public TouchCapabilities Capabilities { get; } = new(10, true, false, true);
        public int FrameCount { get { lock (_gate) return _frames.Count; } }
        public bool ThrowNextFrame { get; set; }
        public bool Initialize() => true;
        public bool SendFrame(TouchFrame frame)
        {
            if(ThrowNextFrame){ThrowNextFrame=false;throw new InvalidOperationException("Injected backend failure");}
            var contacts = frame.GetContacts().ToArray().Select(c => new TouchContactSnapshot(c.ContactId, c.X, c.Y, c.State)).ToArray();
            lock (_gate) _frames.Add(new(frame.FrameId, frame.Timestamp, contacts));
            return true;
        }
        public List<TouchContactSnapshot> Contacts(TouchState state) { lock (_gate) return _frames.SelectMany(f => f.Contacts).Where(c => c.State == state).ToList(); }
        public List<TouchContactSnapshot> ContactsAfterFirstUp(TouchState state) { lock (_gate) { var index = _frames.FindIndex(f => f.Contacts.Any(c => c.State == TouchState.Up)); return index < 0 ? [] : _frames.Skip(index + 1).SelectMany(f => f.Contacts).Where(c => c.State == state).ToList(); } }
        public PhysicalScreenPoint FirstPoint(TouchState state) { var c = Contacts(state).First(); return new((int)c.X, (int)c.Y); }
        public PhysicalScreenPoint LastPoint(TouchState state) { var c = Contacts(state).Last(); return new((int)c.X, (int)c.Y); }
        public bool WaitForState(TouchState state) => SpinWait.SpinUntil(() => Contacts(state).Count > 0, TimeSpan.FromSeconds(5));
        public bool WaitForFrameCount(int count) => SpinWait.SpinUntil(() => FrameCount >= count, TimeSpan.FromSeconds(5));
        public void Shutdown() { }
    }

}
