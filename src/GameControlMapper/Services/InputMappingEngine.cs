using GameControlMapper.Models;
using Microsoft.Extensions.Logging;
using System.Windows.Input;
using GameControlMapper.Win32;
using System.Collections.Frozen;

namespace GameControlMapper.Services;

public sealed class InputMappingEngine : IDisposable
{
    private readonly KeyboardHookService _keyboardHook;
    private readonly MouseHookService _mouseHook;
    private readonly CameraMouseLookService _cameraMouseLook;
    private readonly IInputSimulator _inputSimulator;
    private readonly TouchEngine _touchEngine;
    private readonly TouchScheduler _touchScheduler;
    private readonly HotkeyParser _hotkeyParser;
    private readonly TargetWindowSessionManager _targetSession;
    private readonly WindowCoordinateTransformer _windowCoordinateTransformer;
    private readonly ITouchContactAllocator _contactAllocator;
    private readonly ILogger<InputMappingEngine> _logger;
    private readonly ITargetWindowActivationMonitor? _activationMonitor;
    private readonly object _gate = new();
    private readonly HashSet<int> _pressedKeys = [];
    private readonly Dictionary<Guid, JoystickRuntimeState> _joysticks = [];
    private readonly Dictionary<Guid, MouseAreaRuntimeState> _activeMouseAreas = [];
    private readonly HashSet<(Guid BindingId,long Generation)> _runningActions = [];
    private CancellationTokenSource _gestureCancellation = new();
    private Task<TouchShutdownResult>? _stopTask;
    private bool _disposed;
    private MapperProfile? _profile;
    private int _coordinateWarningLogged;
    private InputPermissionSnapshot _inputPermission = InputPermissionSnapshot.Denied;
    private readonly MappingSessionDiagnostics? _sessionDiagnostics;
    private readonly RuntimeInputPolicy _runtimePolicy;
    private readonly bool _requiresInstalledMouseHook;
    private string? _mappingSessionId;
    private string _stopReason="manual stop";

    public InputMappingEngine(
        KeyboardHookService keyboardHook,
        MouseHookService mouseHook,
        CameraMouseLookService cameraMouseLook,
        IInputSimulator inputSimulator,
        TouchEngine touchEngine,
        TouchScheduler touchScheduler,
        HotkeyParser hotkeyParser,
        TargetWindowSessionManager targetSession,
        WindowCoordinateTransformer windowCoordinateTransformer,
        ILogger<InputMappingEngine> logger,
        ITargetWindowActivationMonitor? activationMonitor = null, ITouchContactAllocator? contactAllocator = null,
        MappingSessionDiagnostics? sessionDiagnostics = null, RuntimeInputPolicy? runtimePolicy = null, bool startNativeHooks = true)
    {
        _keyboardHook = keyboardHook;
        _mouseHook = mouseHook;
        _cameraMouseLook = cameraMouseLook;
        _inputSimulator = inputSimulator;
        _touchEngine = touchEngine;
        _touchScheduler = touchScheduler;
        _hotkeyParser = hotkeyParser;
        _targetSession = targetSession;
        _windowCoordinateTransformer = windowCoordinateTransformer;
        _logger = logger;
        _activationMonitor = activationMonitor;
        _contactAllocator = contactAllocator ?? touchEngine.ContactAllocator;
        _sessionDiagnostics=sessionDiagnostics;
        _runtimePolicy=runtimePolicy??new RuntimeInputPolicy(ApplicationCapabilities.Beta);
        _requiresInstalledMouseHook=startNativeHooks;

        _keyboardHook.GeneratedKeyDown += OnGeneratedKeyDown;
        _keyboardHook.GeneratedKeyUp += OnGeneratedKeyUp;
        _mouseHook.GeneratedButtonDown += OnGeneratedMouseButtonDown;
        _mouseHook.GeneratedButtonUp += OnGeneratedMouseButtonUp;
        _keyboardHook.ShouldSuppressKey = ShouldSuppressKey;
        _mouseHook.ShouldSuppressButton = ShouldSuppressButton;
        _keyboardHook.CaptureGeneration = CaptureGeneration;
        _mouseHook.CaptureGeneration = CaptureGeneration;
        _cameraMouseLook.ActiveChanged += OnCameraActiveChanged;
        if (_activationMonitor is not null) _activationMonitor.ActivationChanged += OnActivationChanged;
        _touchScheduler.TargetSessionInvalidated += OnTargetSessionInvalidated;
        if(startNativeHooks){_keyboardHook.Start();_mouseHook.Start();}
    }

    // Compatibility constructor for existing tests only; production DI cannot resolve its legacy parameters.
    public InputMappingEngine(KeyboardHookService keyboardHook,MouseHookService mouseHook,CameraMouseLookService cameraMouseLook,XInputGamepadMapper gamepadMapper,IInputSimulator inputSimulator,ITouchSimulator touchSimulator,TouchEngine touchEngine,TouchScheduler touchScheduler,HotkeyParser hotkeyParser,TargetWindowSessionManager targetSession,WindowCoordinateTransformer transformer,ILogger<InputMappingEngine> logger,ITargetWindowActivationMonitor? activationMonitor=null,ITouchContactAllocator? allocator=null,MappingSessionDiagnostics? diagnostics=null)
        :this(keyboardHook,mouseHook,cameraMouseLook,inputSimulator,touchEngine,touchScheduler,hotkeyParser,targetSession,transformer,logger,activationMonitor,allocator,diagnostics,startNativeHooks:false){}

    public bool IsActive { get; private set; }
    public bool IsCameraControlActive => _cameraMouseLook.IsActive;
    internal InputPermissionSnapshot CurrentInputPermission => Volatile.Read(ref _inputPermission);
    internal int RunningActionCount { get { lock(_gate) return _runningActions.Count; } }

    public event EventHandler<bool>? ActiveChanged;
    public event EventHandler<bool>? CameraControlActiveChanged;
    public event EventHandler? OverlayToggleRequested;
    public event EventHandler? EditorRequested;

    public void SetProfile(MapperProfile profile)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _profile = profile;
            // Always use TouchInjection mode for now
            Models.InputModeGuard.TouchInjectionMode = true;
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_stopTask is { IsCompleted: false }) return;
            if (_profile is null) return;
            if(!_runtimePolicy.IsProfileModeSupported(_profile.InputMode))
            {
                Volatile.Write(ref _inputPermission,InputPermissionSnapshot.Denied);
                _logger.LogWarning("UnsupportedInBeta: selected input mode is unavailable; mapping was not started.");
                return;
            }
            var unsupportedCount=_profile.Bindings.Count(binding=>binding.IsActive&&!binding.UseNativeInput&&!_runtimePolicy.IsBindingKindSupported(binding.Kind));
            if(unsupportedCount>0)_logger.LogWarning("UnsupportedInBeta: {Count} configured actions are disabled by the current runtime.",unsupportedCount);
            var targetStart = _targetSession.TryStart(
                new IntPtr(_profile.Window.WindowHandle),
                new ProfileSize(_profile.ResolutionWidth, _profile.ResolutionHeight));
            if (!targetStart.Succeeded)
            {
                _logger.LogWarning("Input mapping start rejected: {Reason}", targetStart.Error);
                return;
            }
            _stopTask = null;
            Interlocked.Exchange(ref _coordinateWarningLogged, 0);
            IsActive = true;
            _mappingSessionId=_sessionDiagnostics?.Start()??Guid.NewGuid().ToString("N")[..8];_stopReason="manual stop";
            _logger.LogInformation("Mapping session {SessionId} started",_mappingSessionId);
            _contactAllocator.Reset(targetStart.Session!.Generation);
            PublishInputPermission(targetStart.Session!.Generation);
            _mouseHook.SuppressTouchPromotedMouseEvents = true;
            _mouseHook.SetCursorIsolation(true);
            _touchEngine.StartAcceptingContacts();
            _touchScheduler.Resume();
            if (_gestureCancellation.IsCancellationRequested)
            {
                _gestureCancellation.Dispose();
                _gestureCancellation = new CancellationTokenSource();
            }
            if (_profile?.Gamepad.Enabled == true)
                _logger.LogWarning("UnsupportedInBeta: XInput polling was not started.");
        }

        ActiveChanged?.Invoke(this, true);
        _logger.LogInformation("Input mapping active.");
    }

    public void Stop() => StopAsync("manual stop").GetAwaiter().GetResult();

    public Task<TouchShutdownResult> StopAsync(string reason="manual stop")
    {
        Volatile.Write(ref _inputPermission, InputPermissionSnapshot.Denied);
        _mouseHook.CaptureMovement = false;
        _mouseHook.ResetMovementTracking();
        lock (_gate)
        {
            if (_stopTask is not null) return _stopTask;

            _stopReason=reason;
            _logger.LogInformation("Mapping session {SessionId} stopping. Reason={Reason}; active contacts={Count}",_mappingSessionId,_stopReason,_contactAllocator.ActiveLeases.Count);

            IsActive = false;
            _targetSession.Stop();
            _touchEngine.StopAcceptingContacts();
            _gestureCancellation.Cancel();
            _cameraMouseLook.Stop();
            ReleaseAllJoysticks();

            // Release all active mouse areas
            foreach (var bindingId in _activeMouseAreas.Keys.ToArray())
            {
                if (_activeMouseAreas.TryGetValue(bindingId, out var state))
                {
                    _touchEngine.EndTouch(state.Lease);
                }
            }
            _activeMouseAreas.Clear();

            _touchEngine.ReleaseAll();
            _pressedKeys.Clear();
            _stopTask = CompleteStopAsync();
            return _stopTask;
        }
    }

    private async Task<TouchShutdownResult> CompleteStopAsync()
    {
        TouchShutdownResult result;
        try
        {
            result = await _touchScheduler.PauseAndFlushAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Touch shutdown failed");
            result = new TouchShutdownResult(false, false, [], []);
        }
        _mouseHook.SuppressTouchPromotedMouseEvents = false;
        _mouseHook.SetCursorIsolation(false);
        ActiveChanged?.Invoke(this, false);
        _sessionDiagnostics?.Stop(_stopReason,result.Succeeded,_contactAllocator.ActiveLeases.Count);
        _logger.LogInformation("Mapping session {SessionId} stopped. Reason={Reason}; release succeeded={Succeeded}",_mappingSessionId,_stopReason,result.Succeeded);
        _logger.LogInformation("Input mapping inactive. Touch release succeeded: {Succeeded}", result.Succeeded);
        return result;
    }

    private void OnTargetSessionInvalidated(object? sender, EventArgs e)
    {
        _ = StopAsync("geometry invalidation");
    }

    private long CaptureGeneration() => Volatile.Read(ref _inputPermission).Generation;

    private void OnActivationChanged(object? sender, EventArgs e)
    {
        if (!IsActive || _targetSession.IsForegroundActive()) return;
        Volatile.Write(ref _inputPermission, InputPermissionSnapshot.Denied);
        _ = StopAsync("focus loss");
    }

    private void PublishInputPermission(long generation)
    {
        var keys = new HashSet<int>();
        var buttons = new HashSet<int>();
        if (_profile is not null && _runtimePolicy.IsProfileModeSupported(_profile.InputMode))
        {
            foreach (var binding in _profile.Bindings.Where(b =>
                         _runtimePolicy.CanExecute(_profile,b) &&
                         (_cameraMouseLook.IsActive || !IsLeftMouseArea(b))))
            {
                var token = binding.Hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
                var vk = token is null ? 0 : _hotkeyParser.ToVirtualKey(token);
                if (vk is NativeMethods.VK_LBUTTON or NativeMethods.VK_RBUTTON or NativeMethods.VK_MBUTTON or
                    NativeMethods.VK_XBUTTON1 or NativeMethods.VK_XBUTTON2) buttons.Add(vk); else if (vk != 0) keys.Add(vk);
            }
            if (_profile.Bindings.Any(IsWasdJoystick))
                foreach (var key in new[] { Key.W, Key.A, Key.S, Key.D }) keys.Add(KeyInterop.VirtualKeyFromKey(key));
            var camera = _hotkeyParser.ToVirtualKey(_profile.Camera.ActivationHotkey); if (camera != 0) keys.Add(camera);
        }
        Volatile.Write(ref _inputPermission, new InputPermissionSnapshot(true, true, generation, keys.ToFrozenSet(), buttons.ToFrozenSet()));
    }

    private bool IsCurrentGeneration(long generation)
    {
        var snapshot = Volatile.Read(ref _inputPermission);
        return snapshot.AllowMappedInput && snapshot.Generation == generation;
    }

    private bool IsLifecycleHotkey(int virtualKey) => _profile is not null &&
        new[] { _profile.EnableHotkey, _profile.DisableHotkey, _profile.ToggleOverlayHotkey, _profile.EditorHotkey }
            .Any(h => _hotkeyParser.ToVirtualKey(h) == virtualKey);

    private void OnGeneratedKeyDown(object? sender, GeneratedInputEvent input)
    { if (IsCurrentGeneration(input.Generation) || IsLifecycleHotkey(input.VirtualKey)) OnKeyDown(sender, input.VirtualKey); }
    private void OnGeneratedKeyUp(object? sender, GeneratedInputEvent input)
    { if (IsCurrentGeneration(input.Generation)) OnKeyUp(sender, input.VirtualKey); }
    private void OnGeneratedMouseButtonDown(object? sender, GeneratedInputEvent input)
    { if (IsCurrentGeneration(input.Generation)) OnMouseButtonDown(sender, input.VirtualKey); }
    private void OnGeneratedMouseButtonUp(object? sender, GeneratedInputEvent input)
    { if (IsCurrentGeneration(input.Generation)) OnMouseButtonUp(sender, input.VirtualKey); }

    private void OnKeyDown(object? sender, int virtualKey)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var isInitialPress = _pressedKeys.Add(virtualKey);
            HandlePressedInput(virtualKey, isInitialPress);
        }
    }

    private void OnMouseButtonDown(object? sender, int virtualKey)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            var isInitialPress = _pressedKeys.Add(virtualKey);
            HandlePressedInput(virtualKey, isInitialPress);
        }
    }

    private void HandlePressedInput(int virtualKey, bool isInitialPress)
    {
        if (_profile is null)
        {
            return;
        }

        // Windows emits repeated Down messages while a physical key is held.
        // All supported actions are edge-triggered until the matching Up.
        if(!isInitialPress)return;

        if (_hotkeyParser.Matches(_profile.EnableHotkey, virtualKey, _pressedKeys))
        {
            Start();
            return;
        }

        if (_hotkeyParser.Matches(_profile.DisableHotkey, virtualKey, _pressedKeys))
        {
            StopAsync("F9").GetAwaiter().GetResult();
            return;
        }

        if (_hotkeyParser.Matches(_profile.ToggleOverlayHotkey, virtualKey, _pressedKeys))
        {
            OverlayToggleRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_hotkeyParser.Matches(_profile.EditorHotkey, virtualKey, _pressedKeys))
        {
            EditorRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (!IsActive)
        {
            return;
        }

        if (IsWasdKey(virtualKey) && UpdateJoystickBindings())
        {
            return;
        }

        if (_hotkeyParser.Matches(_profile.Camera.ActivationHotkey, virtualKey, _pressedKeys))
        {
            if (_cameraMouseLook.IsActive)
            {
                _cameraMouseLook.Stop();
                _logger.LogInformation("Camera control toggled off; normal mouse movement restored.");
                return;
            }

            // Relative mouse-look uses a safe symmetric stroke around the target
            // centre. The editor marker is a visual binding, not a click target.
            var rawAnchorX = _profile.ResolutionWidth / 2d;
            var rawAnchorY = _profile.ResolutionHeight / 2d;
            if (!TryScalePointToTarget(rawAnchorX, rawAnchorY, out var scaledAnchor)) return;
            if (_requiresInstalledMouseHook && !_mouseHook.IsInstalled)
            {
                _logger.LogError("Camera start rejected: mouse movement suppression hook is unavailable");
                return;
            }
            _logger.LogDebug(
                "Camera: RawAnchor={RX},{RY} (profile {PW}x{PH}) → PhysicalAnchor={SX},{SY}",
                rawAnchorX, rawAnchorY, _profile.ResolutionWidth, _profile.ResolutionHeight,
                scaledAnchor.X, scaledAnchor.Y);
            _mouseHook.ResetMovementTracking();
            _mouseHook.CaptureMovement = true;
            if (!_cameraMouseLook.Start(_profile.Camera, scaledAnchor.X, scaledAnchor.Y))
            {
                _mouseHook.CaptureMovement = false;
                _mouseHook.ResetMovementTracking();
                return;
            }

            _logger.LogInformation("Camera control toggled on; press Ctrl again to restore the mouse.");
            return;
        }

        var matches = _profile.Bindings
            .Where(binding => _runtimePolicy.CanExecute(_profile,binding) &&
                              (_cameraMouseLook.IsActive || !IsLeftMouseArea(binding)) &&
                              _hotkeyParser.Matches(binding.Hotkey, virtualKey, _pressedKeys))
            .OrderByDescending(binding => binding.Priority)
            .ToArray();

        _logger.LogDebug("Mapped input matched {Count} actions", matches.Length);

        foreach (var binding in matches)
        {
            if (binding.Kind == BindingKind.MouseArea)
            {
                // Handle MouseArea as hold: TouchDown now, TouchUp later on button release
                _logger.LogDebug("Starting mapped mouse-area action");
                if (!_activeMouseAreas.ContainsKey(binding.Id))
                {
                    if (!TryScalePointToTarget(binding.CenterX, binding.CenterY, out var scaled)) continue;
                    var lease = _touchEngine.StartTouch(CurrentGeneration, $"mouse-area:{binding.Id}", scaled.X, scaled.Y);
                    if (lease is not null) _activeMouseAreas[binding.Id] = new MouseAreaRuntimeState(lease);
                }
            }
            else
            {
                // Other bindings (Tap, DoubleTap, etc.) still use ExecuteTouchBindingAsync
                _ = RunTouchBindingAsync(binding, CurrentGeneration, _gestureCancellation.Token);
            }
        }
    }

    private void OnKeyUp(object? sender, int virtualKey)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _pressedKeys.Remove(virtualKey);
            HandleReleasedInput(virtualKey);
        }
    }

    private void OnMouseButtonUp(object? sender, int virtualKey)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _pressedKeys.Remove(virtualKey);
            HandleReleasedInput(virtualKey);
        }
    }

    private void HandleReleasedInput(int virtualKey)
    {
        _logger.LogDebug("Mapped input release received");
        
        if (IsWasdKey(virtualKey))
        {
            UpdateJoystickBindings();
        }
        
        // Check if any active MouseArea bindings are released by this virtual key
        if (_profile is not null)
        {
            var releasedBindings = _profile.Bindings
                .Where(binding => 
                    binding.Kind == BindingKind.MouseArea && 
                    binding.IsActive && 
                    !binding.UseNativeInput && 
                    _activeMouseAreas.ContainsKey(binding.Id) &&
                    binding.Hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Select(_hotkeyParser.ToVirtualKey)
                        .Contains(virtualKey))
                .ToArray();
            
            _logger.LogDebug("Releasing {Count} mapped mouse-area actions", releasedBindings.Length);
            
            foreach (var binding in releasedBindings)
            {
                if (_activeMouseAreas.TryGetValue(binding.Id, out var state))
                {
                    _touchEngine.EndTouch(state.Lease);
                    _activeMouseAreas.Remove(binding.Id);
                }
            }
        }
    }

    private bool UpdateJoystickBindings()
    {
        if (_profile is null || !IsActive)
        {
            return false;
        }

        var targetRect = _targetSession.Current?.ClientRect;
        _logger.LogDebug(
            "UpdateJoystickBindings: ProfileResolution={PW}x{PH}, TargetClient={TargetClient}",
            _profile.ResolutionWidth, _profile.ResolutionHeight, targetRect);

        var handled = false;
        foreach (var binding in _profile.Bindings.Where(IsWasdJoystick))
        {
            handled = true;
            var vector = GetWasdVector();
            if (vector.X == 0 && vector.Y == 0)
            {
                ReleaseJoystick(binding.Id);
                continue;
            }

            var radius = Math.Max(12, Math.Min(binding.Width, binding.Height) * 0.36);
            var length = Math.Sqrt(vector.X * vector.X + vector.Y * vector.Y);
            var normalizedX = vector.X / length;
            var normalizedY = vector.Y / length;
            var targetX = binding.CenterX + normalizedX * radius;
            var targetY = binding.CenterY + normalizedY * radius;

            if (!TryScalePointToTarget(binding.CenterX, binding.CenterY, out var scaledCenter) ||
                !TryScalePointToTarget(targetX, targetY, out var scaledTarget)) continue;

            _logger.LogDebug(
                "Joystick: BindingCenter={BCX},{BCY} → ScaledCenter={SCX},{SCY}, " +
                "BindingTarget={BTX},{BTY} → ScaledTarget={STX},{STY}",
                binding.CenterX, binding.CenterY, scaledCenter.X, scaledCenter.Y,
                targetX, targetY, scaledTarget.X, scaledTarget.Y);

            if (!_joysticks.TryGetValue(binding.Id, out var state))
            {
                var lease = _touchEngine.StartTouch(CurrentGeneration, $"joystick:{binding.Id}", scaledCenter.X, scaledCenter.Y);
                if (lease is null) continue;
                state = new JoystickRuntimeState(lease);
                _joysticks[binding.Id] = state;
            }

            _touchEngine.MoveTouch(state.Lease, scaledTarget.X, scaledTarget.Y);
        }

        return handled;
    }

    private void ReleaseAllJoysticks()
    {
        foreach (var bindingId in _joysticks.Keys.ToArray())
        {
            ReleaseJoystick(bindingId);
        }
    }

    private void ReleaseJoystick(Guid bindingId)
    {
        if (!_joysticks.Remove(bindingId, out var state))
        {
            return;
        }

        _touchEngine.EndTouch(state.Lease);
    }

    private async Task RunTouchBindingAsync(ControlBinding binding,long generation,CancellationToken cancellationToken)
    {
        lock(_gate)
        {
            if(cancellationToken.IsCancellationRequested||!IsCurrentGeneration(generation)||!_runningActions.Add((binding.Id,generation)))return;
        }
        try
        {
            if(!IsCurrentGeneration(generation))return;
            await ExecuteTouchBindingAsync(binding, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { _logger.LogError(ex, "Mapped touch action failed"); }
        finally{lock(_gate)_runningActions.Remove((binding.Id,generation));}
    }

    private async Task ExecuteTouchBindingAsync(ControlBinding binding, CancellationToken cancellationToken)
    {
        if (Models.InputModeGuard.TouchInjectionMode)
        {
            // Use new TouchEngine path for Touch Injection mode
            TouchContactLease? lease = null;
            TouchContactLease? Acquire(double x, double y) => _touchEngine.StartTouch(CurrentGeneration, $"binding:{binding.Id}", x, y);
            switch (binding.Kind)
            {
                case BindingKind.Tap:
                case BindingKind.MouseArea:
                case BindingKind.Aim:
                    {
                        if (!TryScalePointToTarget(binding.CenterX, binding.CenterY, out var scaled)) return;
                        lease = Acquire(scaled.X, scaled.Y); if (lease is null) return;
                        await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                        _touchEngine.EndTouch(lease);
                        break;
                    }
                case BindingKind.DoubleTap:
                    {
                        if (!TryScalePointToTarget(binding.CenterX, binding.CenterY, out var scaled)) return;
                        lease = Acquire(scaled.X, scaled.Y); if (lease is null) return;
                        await Task.Delay(30, cancellationToken).ConfigureAwait(false);
                        _touchEngine.EndTouch(lease);
                        await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                        lease = Acquire(scaled.X, scaled.Y); if (lease is null) return;
                        await Task.Delay(30, cancellationToken).ConfigureAwait(false);
                        _touchEngine.EndTouch(lease);
                        break;
                    }
                case BindingKind.Hold:
                    {
                        if (!TryScalePointToTarget(binding.CenterX, binding.CenterY, out var scaled)) return;
                        lease = Acquire(scaled.X, scaled.Y); if (lease is null) return;
                        await Task.Delay(Math.Max(35, binding.HoldMilliseconds), cancellationToken).ConfigureAwait(false);
                        _touchEngine.EndTouch(lease);
                        break;
                    }
                case BindingKind.Swipe:
                    {
                        if (!TryScalePointToTarget(binding.X, binding.CenterY, out var scaledStart) ||
                            !TryScalePointToTarget(binding.X + binding.Width, binding.CenterY, out var scaledEnd)) return;
                        const int steps = 10;
                        lease = Acquire(scaledStart.X, scaledStart.Y); if (lease is null) return;
                        for (int i = 1; i <= steps; i++)
                        {
                            double t = (double)i / steps;
                            double x = scaledStart.X + (scaledEnd.X - scaledStart.X) * t;
                            _touchEngine.MoveTouch(lease, x, scaledStart.Y);
                            await Task.Delay(Math.Max(Math.Max(120, binding.HoldMilliseconds) / steps, 10), cancellationToken).ConfigureAwait(false);
                        }
                        _touchEngine.EndTouch(lease);
                        break;
                    }
                case BindingKind.Macro:
                case BindingKind.Sequence:
                    _logger.LogWarning("UnsupportedInBeta: action kind {BindingKind} was rejected without creating touch contacts.",binding.Kind);
                    break;
            }
        }
    }

    private static bool IsWasdJoystick(ControlBinding binding)
    {
        return binding.IsActive &&
               binding.Kind == BindingKind.Joystick &&
               !binding.UseNativeInput &&
               binding.Hotkey.Equals("WASD", StringComparison.OrdinalIgnoreCase);
    }

    private (int X, int Y) GetWasdVector()
    {
        var w = KeyInterop.VirtualKeyFromKey(Key.W);
        var a = KeyInterop.VirtualKeyFromKey(Key.A);
        var s = KeyInterop.VirtualKeyFromKey(Key.S);
        var d = KeyInterop.VirtualKeyFromKey(Key.D);

        var x = (_pressedKeys.Contains(d) ? 1 : 0) - (_pressedKeys.Contains(a) ? 1 : 0);
        var y = (_pressedKeys.Contains(s) ? 1 : 0) - (_pressedKeys.Contains(w) ? 1 : 0);
        return (x, y);
    }

    private static bool IsWasdKey(int virtualKey)
    {
        return virtualKey == KeyInterop.VirtualKeyFromKey(Key.W) ||
               virtualKey == KeyInterop.VirtualKeyFromKey(Key.A) ||
               virtualKey == KeyInterop.VirtualKeyFromKey(Key.S) ||
               virtualKey == KeyInterop.VirtualKeyFromKey(Key.D);
    }

    private bool ShouldSuppressKey(int virtualKey)
    {
        var snapshot = Volatile.Read(ref _inputPermission);
        return snapshot.AllowSuppression && snapshot.SuppressedKeys.Contains(virtualKey);
    }

    private bool ShouldSuppressButton(int virtualKey)
    {
        var snapshot = Volatile.Read(ref _inputPermission);
        return snapshot.AllowSuppression && snapshot.SuppressedButtons.Contains(virtualKey);
    }

    private long CurrentGeneration => _targetSession.Current?.Generation ?? 0;

    private void OnCameraActiveChanged(object? sender, bool active)
    {
        lock (_gate)
        {
            if (!active)
            {
                _mouseHook.CaptureMovement = false;
                _mouseHook.ResetMovementTracking();
                ReleaseLeftMouseAreasLocked();
            }

            var session = _targetSession.Current;
            if (IsActive && session is { IsActive: true }) PublishInputPermission(session.Generation);
        }

        CameraControlActiveChanged?.Invoke(this, active);
    }

    private bool IsLeftMouseArea(ControlBinding binding) =>
        binding.Kind == BindingKind.MouseArea &&
        binding.Hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(_hotkeyParser.ToVirtualKey)
            .Contains(NativeMethods.VK_LBUTTON);

    private void ReleaseLeftMouseAreasLocked()
    {
        if (_profile is null) return;
        foreach (var binding in _profile.Bindings.Where(IsLeftMouseArea).ToArray())
        {
            if (!_activeMouseAreas.Remove(binding.Id, out var state)) continue;
            _touchEngine.EndTouch(state.Lease);
            _logger.LogDebug("Mapped mouse-area action released because camera combat mode was disabled");
        }
    }

    private bool TryScalePointToTarget(double x, double y, out PhysicalScreenPoint point)
    {
        point = default;
        var session = _targetSession.Current;
        if (session is null || !session.IsActive)
        {
            LogCoordinateWarningOnce("Target window session is not active.");
            return false;
        }

        var result = _windowCoordinateTransformer.TryTransform(
            new ProfilePoint(x, y), session.ProfileSize, session.ClientRect, session.ScaleMode);
        if (!result.Succeeded || result.IsOutsideProfile)
        {
            LogCoordinateWarningOnce(result.IsOutsideProfile
                ? $"Profile point ({x}, {y}) is outside the half-open profile bounds."
                : result.Error ?? "Coordinate transformation failed.");
            return false;
        }

        point = result.Point;
        return true;
    }

    private void LogCoordinateWarningOnce(string message)
    {
        if (Interlocked.CompareExchange(ref _coordinateWarningLogged, 1, 0) == 0)
            _logger.LogWarning("Touch coordinate rejected: {Reason}", message);
    }

    private sealed record JoystickRuntimeState(TouchContactLease Lease);

    private sealed record MouseAreaRuntimeState(TouchContactLease Lease);

    public void Dispose()
    {
        Stop();
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            IsActive = false;
            _touchEngine.ReleaseAll();
            _pressedKeys.Clear();
            _runningActions.Clear();
        }

        _keyboardHook.GeneratedKeyDown -= OnGeneratedKeyDown;
        _keyboardHook.GeneratedKeyUp -= OnGeneratedKeyUp;
        _mouseHook.GeneratedButtonDown -= OnGeneratedMouseButtonDown;
        _mouseHook.GeneratedButtonUp -= OnGeneratedMouseButtonUp;
        _keyboardHook.ShouldSuppressKey = null;
        _mouseHook.ShouldSuppressButton = null;
        _keyboardHook.CaptureGeneration = null;
        _mouseHook.CaptureGeneration = null;
        _cameraMouseLook.ActiveChanged -= OnCameraActiveChanged;
        if (_activationMonitor is not null) _activationMonitor.ActivationChanged -= OnActivationChanged;
        _touchScheduler.TargetSessionInvalidated -= OnTargetSessionInvalidated;
        _keyboardHook.Stop();
        _mouseHook.Stop();
        _gestureCancellation.Dispose();
    }
}
