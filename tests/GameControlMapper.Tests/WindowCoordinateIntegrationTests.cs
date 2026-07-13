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
    [Fact]
    public async Task CtrlPress_TogglesCameraAndCtrlReleaseKeepsItActive()
    {
        using var fixture = new Fixture(new(0, 0, 1920, 1080));
        fixture.StartCamera();

        fixture.Press(Key.LeftCtrl);
        Assert.True(fixture.Camera.IsActive);
        Assert.True(fixture.MouseHook.CaptureMovement);
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
        fixture.PressMouse(NativeMethods.VK_LBUTTON);
        Assert.Equal(2, fixture.Contacts.ActiveContacts.Count);

        fixture.Press(Key.LeftCtrl);
        fixture.Release(Key.LeftCtrl);
        Assert.False(fixture.MouseHook.ShouldSuppressButton!(NativeMethods.VK_LBUTTON));
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
        public CameraMouseLookService Camera { get; }
        public InputMappingEngine Mapping { get; }
        public MapperProfile Profile { get; }

        public Fixture(PhysicalClientRect rect)
        {
            Geometry = new MutableGeometryProvider(rect);
            var native = new FakeWindowNative();
            var monitor = new TargetWindowGeometryMonitor(Geometry, native, GeometryEvents, TimeProvider.System);
            Session = new TargetWindowSessionManager(Geometry, NullLogger<TargetWindowSessionManager>.Instance, geometryMonitor: monitor);
            Contacts = new ContactManager(NullLogger<ContactManager>.Instance, new TouchCapabilities(10, true, false, true));
            TouchEngine = new TouchEngine(NullLogger<TouchEngine>.Instance, Contacts);
            Scheduler = new TouchScheduler(NullLogger<TouchScheduler>.Instance, Contacts, Backend, new FrameContext(), Session);
            Scheduler.Start();
            var input = new NullInputSimulator();
            Camera = new CameraMouseLookService(TouchEngine, NullLogger<CameraMouseLookService>.Instance, scheduler: Scheduler);
            MouseHook = new MouseHookService(NullLogger<MouseHookService>.Instance);
            Mapping = new InputMappingEngine(
                new KeyboardHookService(NullLogger<KeyboardHookService>.Instance),
                MouseHook,
                Camera,
                new XInputGamepadMapper(input, NullLogger<XInputGamepadMapper>.Instance),
                input, new NullTouchSimulator(), TouchEngine, Scheduler, new HotkeyParser(), Session,
                new WindowCoordinateTransformer(), NullLogger<InputMappingEngine>.Instance);
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
        public bool Initialize() => true;
        public bool SendFrame(TouchFrame frame)
        {
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

    private sealed class NullTouchSimulator : ITouchSimulator
    {
        public Task TapAsync(double x, double y, int milliseconds = 35, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DoubleTapAsync(double x, double y, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task HoldAsync(int contactId, double x, double y, int milliseconds, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SwipeAsync(int contactId, double startX, double startY, double endX, double endY, int milliseconds, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void TouchDown(int contactId, double x, double y) { }
        public void TouchMove(int contactId, double x, double y) { }
        public void TouchUp(int contactId) { }
        public void ReleaseAll() { }
    }

    private sealed class NullInputSimulator : IInputSimulator
    {
        public Task ExecuteBindingAsync(ControlBinding binding, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ClickAsync(double x, double y, SimulatedMouseButton button = SimulatedMouseButton.Left, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DoubleClickAsync(double x, double y, SimulatedMouseButton button = SimulatedMouseButton.Left, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SwipeAsync(double startX, double startY, double endX, double endY, int durationMilliseconds, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void MouseDownAt(double x, double y, SimulatedMouseButton button = SimulatedMouseButton.Left) { }
        public void MouseMoveTo(double x, double y) { }
        public void MouseUp(SimulatedMouseButton button = SimulatedMouseButton.Left) { }
        public void MouseDown(SimulatedMouseButton button = SimulatedMouseButton.Left) { }
        public void KeyDown(string key) { }
        public void KeyUp(string key) { }
        public (int X, int Y) GetCursorPosition() => (0, 0);
        public void RestoreCursor(int x, int y) { }
        public void MoveRelative(int dx, int dy) { }
    }
}
