using GameControlMapper.Models;
using Microsoft.Extensions.Logging;
using System.Windows.Input;
using GameControlMapper.Win32;

namespace GameControlMapper.Services;

public sealed class InputMappingEngine : IDisposable
{
    private readonly KeyboardHookService _keyboardHook;
    private readonly MouseHookService _mouseHook;
    private readonly CameraMouseLookService _cameraMouseLook;
    private readonly XInputGamepadMapper _gamepadMapper;
    private readonly IInputSimulator _inputSimulator;
    private readonly ITouchSimulator _touchSimulator;
    private readonly TouchEngine _touchEngine;
    private readonly TouchScheduler _touchScheduler;
    private readonly HotkeyParser _hotkeyParser;
    private readonly TargetWindowSessionManager _targetSession;
    private readonly WindowCoordinateTransformer _windowCoordinateTransformer;
    private readonly ILogger<InputMappingEngine> _logger;
    private readonly ITargetWindowActivationMonitor? _activationMonitor;
    private readonly object _gate = new();
    private readonly HashSet<int> _pressedKeys = [];
    private readonly Dictionary<Guid, JoystickRuntimeState> _joysticks = [];
    private readonly Dictionary<Guid, MouseAreaRuntimeState> _activeMouseAreas = [];
    private readonly Dictionary<Guid, int> _actionContactIds = [];
    private readonly Dictionary<Guid, SemaphoreSlim> _actionLocks = [];
    private CancellationTokenSource _gestureCancellation = new();
    private Task<TouchShutdownResult>? _stopTask;
    private bool _disposed;
    private MapperProfile? _profile;
    private int _coordinateWarningLogged;
    private InputPermissionSnapshot _inputPermission = InputPermissionSnapshot.Denied;

    public InputMappingEngine(
        KeyboardHookService keyboardHook,
        MouseHookService mouseHook,
        CameraMouseLookService cameraMouseLook,
        XInputGamepadMapper gamepadMapper,
        IInputSimulator inputSimulator,
        ITouchSimulator touchSimulator,
        TouchEngine touchEngine,
        TouchScheduler touchScheduler,
        HotkeyParser hotkeyParser,
        TargetWindowSessionManager targetSession,
        WindowCoordinateTransformer windowCoordinateTransformer,
        ILogger<InputMappingEngine> logger,
        ITargetWindowActivationMonitor? activationMonitor = null)
    {
        _keyboardHook = keyboardHook;
        _mouseHook = mouseHook;
        _cameraMouseLook = cameraMouseLook;
        _gamepadMapper = gamepadMapper;
        _inputSimulator = inputSimulator;
        _touchSimulator = touchSimulator;
        _touchEngine = touchEngine;
        _touchScheduler = touchScheduler;
        _hotkeyParser = hotkeyParser;
        _targetSession = targetSession;
        _windowCoordinateTransformer = windowCoordinateTransformer;
        _logger = logger;
        _activationMonitor = activationMonitor;

        _keyboardHook.GeneratedKeyDown += OnGeneratedKeyDown;
        _keyboardHook.GeneratedKeyUp += OnGeneratedKeyUp;
        _mouseHook.GeneratedButtonDown += OnGeneratedMouseButtonDown;
        _mouseHook.GeneratedButtonUp += OnGeneratedMouseButtonUp;
        _keyboardHook.ShouldSuppressKey = ShouldSuppressKey;
        _mouseHook.ShouldSuppressButton = ShouldSuppressButton;
        _keyboardHook.CaptureGeneration = CaptureGeneration;
        _mouseHook.CaptureGeneration = CaptureGeneration;
        if (_activationMonitor is not null) _activationMonitor.ActivationChanged += OnActivationChanged;
        _touchScheduler.TargetSessionInvalidated += OnTargetSessionInvalidated;
        _keyboardHook.Start();
        _mouseHook.Start();
    }

    public bool IsActive { get; private set; }

    public event EventHandler<bool>? ActiveChanged;
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
            _gamepadMapper.SetProfile(profile);
            _actionContactIds.Clear();
            var nextContactId = 4;
            foreach (var binding in profile.Bindings.Where(b => b.Kind is not BindingKind.Joystick and not BindingKind.Aim && b.Kind is not BindingKind.MouseArea))
            {
                if (nextContactId >= 10)
                {
                    _logger.LogWarning("Binding {Binding} cannot receive a touch contact: the 10-contact limit was reached", binding.Name);
                    continue;
                }
                _actionContactIds[binding.Id] = nextContactId++;
                _actionLocks[binding.Id] = new SemaphoreSlim(1, 1);
            }
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
            PublishInputPermission(targetStart.Session!.Generation);
            _touchEngine.StartAcceptingContacts();
            _touchScheduler.Resume();
            if (_gestureCancellation.IsCancellationRequested)
            {
                _gestureCancellation.Dispose();
                _gestureCancellation = new CancellationTokenSource();
            }
            if (_profile?.Gamepad.Enabled == true)
            {
                _gamepadMapper.Start();
            }
        }

        ActiveChanged?.Invoke(this, true);
        _logger.LogInformation("Input mapping active.");
    }

    public void Stop() => StopAsync().GetAwaiter().GetResult();

    public Task<TouchShutdownResult> StopAsync()
    {
        Volatile.Write(ref _inputPermission, InputPermissionSnapshot.Denied);
        lock (_gate)
        {
            if (_stopTask is not null) return _stopTask;

            IsActive = false;
            _targetSession.Stop();
            _touchEngine.StopAcceptingContacts();
            _gestureCancellation.Cancel();
            _cameraMouseLook.Stop();
            _gamepadMapper.Stop();
            ReleaseAllJoysticks();

            // Release all active mouse areas
            foreach (var bindingId in _activeMouseAreas.Keys.ToArray())
            {
                if (_activeMouseAreas.TryGetValue(bindingId, out var state))
                {
                    _touchEngine.EndTouch(state.ContactId);
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
        ActiveChanged?.Invoke(this, false);
        _logger.LogInformation("Input mapping inactive. Touch release succeeded: {Succeeded}", result.Succeeded);
        return result;
    }

    private void OnTargetSessionInvalidated(object? sender, EventArgs e)
    {
        _ = StopAsync();
    }

    private long CaptureGeneration() => Volatile.Read(ref _inputPermission).Generation;

    private void OnActivationChanged(object? sender, EventArgs e)
    {
        if (!IsActive || _targetSession.IsForegroundActive()) return;
        Volatile.Write(ref _inputPermission, InputPermissionSnapshot.Denied);
        _ = StopAsync();
    }

    private void PublishInputPermission(long generation)
    {
        var keys = new HashSet<int>();
        var buttons = new HashSet<int>();
        if (_profile is not null)
        {
            foreach (var binding in _profile.Bindings.Where(b => b.IsActive && !b.UseNativeInput))
            {
                var token = binding.Hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
                var vk = token is null ? 0 : _hotkeyParser.ToVirtualKey(token);
                if (vk is NativeMethods.VK_LBUTTON or NativeMethods.VK_RBUTTON or NativeMethods.VK_MBUTTON) buttons.Add(vk); else if (vk != 0) keys.Add(vk);
            }
            if (_profile.Bindings.Any(IsWasdJoystick))
                foreach (var key in new[] { Key.W, Key.A, Key.S, Key.D }) keys.Add(KeyInterop.VirtualKeyFromKey(key));
            var camera = _hotkeyParser.ToVirtualKey(_profile.Camera.ActivationHotkey); if (camera != 0) keys.Add(camera);
        }
        Volatile.Write(ref _inputPermission, new InputPermissionSnapshot(true, true, generation, keys, buttons));
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

            _pressedKeys.Add(virtualKey);
            HandlePressedInput(virtualKey);
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

            _pressedKeys.Add(virtualKey);
            HandlePressedInput(virtualKey);
        }
    }

    private void HandlePressedInput(int virtualKey)
    {
        if (_profile is null)
        {
            return;
        }

        if (_hotkeyParser.Matches(_profile.EnableHotkey, virtualKey, _pressedKeys))
        {
            Start();
            return;
        }

        if (_hotkeyParser.Matches(_profile.DisableHotkey, virtualKey, _pressedKeys))
        {
            Stop();
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
                var cameraBinding = FindCameraBinding(_profile);
                var rawAnchorX = cameraBinding?.CenterX ?? _profile.Camera.AnchorX;
                var rawAnchorY = cameraBinding?.CenterY ?? _profile.Camera.AnchorY;
                if (!TryScalePointToTarget(rawAnchorX, rawAnchorY, out var scaledAnchor)) return;
                _logger.LogInformation(
                    "Camera: RawAnchor={RX},{RY} (profile {PW}x{PH}) → PhysicalAnchor={SX},{SY}",
                    rawAnchorX, rawAnchorY, _profile.ResolutionWidth, _profile.ResolutionHeight,
                    scaledAnchor.X, scaledAnchor.Y);
                _cameraMouseLook.Start(_profile.Camera, scaledAnchor.X, scaledAnchor.Y);
                return;
            }

        var matches = _profile.Bindings
            .Where(binding => binding.IsActive && !binding.UseNativeInput && _hotkeyParser.Matches(binding.Hotkey, virtualKey, _pressedKeys))
            .OrderByDescending(binding => binding.Priority)
            .ToArray();

        _logger.LogInformation("HandlePressedInput: Found {Count} bindings for virtual key {VirtualKey}", matches.Length, virtualKey);

        foreach (var binding in matches)
        {
            if (binding.Kind == BindingKind.MouseArea)
            {
                // Handle MouseArea as hold: TouchDown now, TouchUp later on button release
                _logger.LogInformation("HandlePressedInput: Processing MouseArea binding '{Name}'", binding.Name);
                if (!_activeMouseAreas.ContainsKey(binding.Id))
                {
                    var contactId = GetActionContactId(binding);
                    if (!TryScalePointToTarget(binding.CenterX, binding.CenterY, out var scaled)) continue;
                    _logger.LogInformation("HandlePressedInput: Calling TouchDown({ContactId}, {ScaledX}, {ScaledY}) for MouseArea '{Name}'", contactId, scaled.X, scaled.Y, binding.Name);
                    _touchEngine.StartTouch(contactId, scaled.X, scaled.Y);
                    _activeMouseAreas[binding.Id] = new MouseAreaRuntimeState(contactId);
                }
            }
            else
            {
                // Other bindings (Tap, DoubleTap, etc.) still use ExecuteTouchBindingAsync
                _ = RunTouchBindingAsync(binding, _gestureCancellation.Token);
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
        _logger.LogInformation("HandleReleasedInput: Called for virtual key {VirtualKey}", virtualKey);
        
        if (IsWasdKey(virtualKey))
        {
            UpdateJoystickBindings();
        }
        
        if (_profile is not null && _cameraMouseLook.IsActive)
        {
            var cameraKey = _hotkeyParser.ToVirtualKey(_profile.Camera.ActivationHotkey);
            if (cameraKey == virtualKey)
            {
                _cameraMouseLook.Stop();
            }
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
            
            _logger.LogInformation("HandleReleasedInput: Found {Count} MouseArea bindings to release", releasedBindings.Length);
            
            foreach (var binding in releasedBindings)
            {
                if (_activeMouseAreas.TryGetValue(binding.Id, out var state))
                {
                    _logger.LogInformation("HandleReleasedInput: Calling TouchUp({ContactId}) for MouseArea '{Name}'", state.ContactId, binding.Name);
                    _touchEngine.EndTouch(state.ContactId);
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
        _logger.LogInformation(
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

            _logger.LogInformation(
                "Joystick: BindingCenter={BCX},{BCY} → ScaledCenter={SCX},{SCY}, " +
                "BindingTarget={BTX},{BTY} → ScaledTarget={STX},{STY}",
                binding.CenterX, binding.CenterY, scaledCenter.X, scaledCenter.Y,
                targetX, targetY, scaledTarget.X, scaledTarget.Y);

            if (!_joysticks.TryGetValue(binding.Id, out var state))
            {
                state = new JoystickRuntimeState(GetJoystickContactId(binding));
                _joysticks[binding.Id] = state;
                _touchEngine.StartTouch(state.ContactId, scaledCenter.X, scaledCenter.Y);
            }

            _touchEngine.MoveTouch(state.ContactId, scaledTarget.X, scaledTarget.Y);
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

        _touchEngine.EndTouch(state.ContactId);
    }

    private async Task RunTouchBindingAsync(ControlBinding binding, CancellationToken cancellationToken)
    {
        if (!_actionLocks.TryGetValue(binding.Id, out var actionLock)) return;
        try
        {
            await actionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try { await ExecuteTouchBindingAsync(binding, cancellationToken).ConfigureAwait(false); }
            finally { actionLock.Release(); }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { _logger.LogError(ex, "Touch binding {Binding} failed", binding.Name); }
    }

    private async Task ExecuteTouchBindingAsync(ControlBinding binding, CancellationToken cancellationToken)
    {
        if (Models.InputModeGuard.TouchInjectionMode)
        {
            // Use new TouchEngine path for Touch Injection mode
            var contactId = GetActionContactId(binding);
            if (contactId < 0) return;
            switch (binding.Kind)
            {
                case BindingKind.Tap:
                case BindingKind.MouseArea:
                case BindingKind.Aim:
                    {
                        if (!TryScalePointToTarget(binding.CenterX, binding.CenterY, out var scaled)) return;
                        _touchEngine.StartTouch(contactId, scaled.X, scaled.Y);
                        await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                        _touchEngine.EndTouch(contactId);
                        break;
                    }
                case BindingKind.DoubleTap:
                    {
                        if (!TryScalePointToTarget(binding.CenterX, binding.CenterY, out var scaled)) return;
                        _touchEngine.StartTouch(contactId, scaled.X, scaled.Y);
                        await Task.Delay(30, cancellationToken).ConfigureAwait(false);
                        _touchEngine.EndTouch(contactId);
                        await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                        _touchEngine.StartTouch(contactId, scaled.X, scaled.Y);
                        await Task.Delay(30, cancellationToken).ConfigureAwait(false);
                        _touchEngine.EndTouch(contactId);
                        break;
                    }
                case BindingKind.Hold:
                    {
                        if (!TryScalePointToTarget(binding.CenterX, binding.CenterY, out var scaled)) return;
                        _touchEngine.StartTouch(contactId, scaled.X, scaled.Y);
                        await Task.Delay(Math.Max(35, binding.HoldMilliseconds), cancellationToken).ConfigureAwait(false);
                        _touchEngine.EndTouch(contactId);
                        break;
                    }
                case BindingKind.Swipe:
                    {
                        if (!TryScalePointToTarget(binding.X, binding.CenterY, out var scaledStart) ||
                            !TryScalePointToTarget(binding.X + binding.Width, binding.CenterY, out var scaledEnd)) return;
                        const int steps = 10;
                        _touchEngine.StartTouch(contactId, scaledStart.X, scaledStart.Y);
                        for (int i = 1; i <= steps; i++)
                        {
                            double t = (double)i / steps;
                            double x = scaledStart.X + (scaledEnd.X - scaledStart.X) * t;
                            _touchEngine.MoveTouch(contactId, x, scaledStart.Y);
                            await Task.Delay(Math.Max(Math.Max(120, binding.HoldMilliseconds) / steps, 10), cancellationToken).ConfigureAwait(false);
                        }
                        _touchEngine.EndTouch(contactId);
                        break;
                    }
                case BindingKind.Macro:
                case BindingKind.Sequence:
                    // Macros/Sequences still use native input even in touch mode?
                    await _inputSimulator.ExecuteBindingAsync(binding, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
        else
        {
            // Use old path for SendInput mode
            switch (binding.Kind)
            {
                case BindingKind.Tap:
                case BindingKind.MouseArea:
                case BindingKind.Aim:
                    {
                        if (!TryScalePointToTarget(binding.CenterX, binding.CenterY, out var scaled)) return;
                        await _touchSimulator.TapAsync(scaled.X, scaled.Y, cancellationToken: CancellationToken.None).ConfigureAwait(false);
                        break;
                    }
                case BindingKind.DoubleTap:
                    {
                        if (!TryScalePointToTarget(binding.CenterX, binding.CenterY, out var scaled)) return;
                        await _touchSimulator.DoubleTapAsync(scaled.X, scaled.Y, CancellationToken.None).ConfigureAwait(false);
                        break;
                    }
                case BindingKind.Hold:
                    {
                        if (!TryScalePointToTarget(binding.CenterX, binding.CenterY, out var scaled)) return;
                        await _touchSimulator.HoldAsync(GetActionContactId(binding), scaled.X, scaled.Y, Math.Max(35, binding.HoldMilliseconds), CancellationToken.None).ConfigureAwait(false);
                        break;
                    }
                case BindingKind.Swipe:
                    {
                        if (!TryScalePointToTarget(binding.X, binding.CenterY, out var scaledStart) ||
                            !TryScalePointToTarget(binding.X + binding.Width, binding.CenterY, out var scaledEnd)) return;
                        await _touchSimulator.SwipeAsync(
                            GetActionContactId(binding),
                            scaledStart.X,
                            scaledStart.Y,
                            scaledEnd.X,
                            scaledEnd.Y,
                            Math.Max(120, binding.HoldMilliseconds),
                            CancellationToken.None).ConfigureAwait(false);
                        break;
                    }
                case BindingKind.Macro:
                case BindingKind.Sequence:
                    await _inputSimulator.ExecuteBindingAsync(binding, CancellationToken.None).ConfigureAwait(false);
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

    private ControlBinding? FindCameraBinding(MapperProfile profile)
    {
        return profile.Bindings
            .Where(binding => binding.IsActive && binding.Kind == BindingKind.Aim)
            .FirstOrDefault(binding =>
                _hotkeyParser.Matches(binding.Hotkey, _hotkeyParser.ToVirtualKey(profile.Camera.ActivationHotkey), new HashSet<int> { _hotkeyParser.ToVirtualKey(profile.Camera.ActivationHotkey) }) ||
                binding.Name.Equals("Camera", StringComparison.OrdinalIgnoreCase));
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

    private static int GetJoystickContactId(ControlBinding binding)
    {
        return (int)Models.FixedContacts.Joystick;
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

    private int GetActionContactId(ControlBinding binding)
    {
        // Проверяем, это Fire (ЛКМ) или Aim (ПКМ)?
        var hotkey = binding.Hotkey.ToLowerInvariant();
        if (hotkey.Contains("leftbutton") || hotkey.Contains("mouseleft"))
        {
            return (int)Models.FixedContacts.Fire;
        }
        if (hotkey.Contains("rightbutton") || hotkey.Contains("mouseright"))
        {
            return (int)Models.FixedContacts.Aim;
        }
        // Для других используем существующий метод (для совместимости)
        return _actionContactIds.TryGetValue(binding.Id, out var id) ? id : -1;
    }

    private sealed record JoystickRuntimeState(int ContactId);

    private sealed record MouseAreaRuntimeState(int ContactId);

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
            _touchSimulator.ReleaseAll();
            _pressedKeys.Clear();
        }

        _keyboardHook.GeneratedKeyDown -= OnGeneratedKeyDown;
        _keyboardHook.GeneratedKeyUp -= OnGeneratedKeyUp;
        _mouseHook.GeneratedButtonDown -= OnGeneratedMouseButtonDown;
        _mouseHook.GeneratedButtonUp -= OnGeneratedMouseButtonUp;
        _keyboardHook.ShouldSuppressKey = null;
        _mouseHook.ShouldSuppressButton = null;
        _keyboardHook.CaptureGeneration = null;
        _mouseHook.CaptureGeneration = null;
        if (_activationMonitor is not null) _activationMonitor.ActivationChanged -= OnActivationChanged;
        _touchScheduler.TargetSessionInvalidated -= OnTargetSessionInvalidated;
        _keyboardHook.Stop();
        _mouseHook.Stop();
        _gestureCancellation.Dispose();
        foreach (var actionLock in _actionLocks.Values) actionLock.Dispose();
    }
}
