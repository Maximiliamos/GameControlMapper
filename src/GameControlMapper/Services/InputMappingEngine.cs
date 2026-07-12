using GameControlMapper.Models;
using Microsoft.Extensions.Logging;
using System.Windows.Input;

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
    private readonly HotkeyParser _hotkeyParser;
    private readonly CoordinateScaler _coordinateScaler;
    private readonly ILogger<InputMappingEngine> _logger;
    private readonly object _gate = new();
    private readonly HashSet<int> _pressedKeys = [];
    private readonly Dictionary<Guid, JoystickRuntimeState> _joysticks = [];
    private readonly Dictionary<Guid, MouseAreaRuntimeState> _activeMouseAreas = [];
    private readonly Dictionary<Guid, int> _actionContactIds = [];
    private readonly Dictionary<Guid, SemaphoreSlim> _actionLocks = [];
    private CancellationTokenSource _gestureCancellation = new();
    private bool _disposed;
    private MapperProfile? _profile;
    private int _screenWidth;
    private int _screenHeight;

    public InputMappingEngine(
        KeyboardHookService keyboardHook,
        MouseHookService mouseHook,
        CameraMouseLookService cameraMouseLook,
        XInputGamepadMapper gamepadMapper,
        IInputSimulator inputSimulator,
        ITouchSimulator touchSimulator,
        TouchEngine touchEngine,
        HotkeyParser hotkeyParser,
        CoordinateScaler coordinateScaler,
        ILogger<InputMappingEngine> logger)
    {
        _keyboardHook = keyboardHook;
        _mouseHook = mouseHook;
        _cameraMouseLook = cameraMouseLook;
        _gamepadMapper = gamepadMapper;
        _inputSimulator = inputSimulator;
        _touchSimulator = touchSimulator;
        _touchEngine = touchEngine;
        _hotkeyParser = hotkeyParser;
        _coordinateScaler = coordinateScaler;
        _logger = logger;

        // Get actual screen resolution
        _screenWidth = Win32.NativeMethods.GetSystemMetrics(Win32.NativeMethods.SM_CXSCREEN);
        _screenHeight = Win32.NativeMethods.GetSystemMetrics(Win32.NativeMethods.SM_CYSCREEN);

        _keyboardHook.KeyDown += OnKeyDown;
        _keyboardHook.KeyUp += OnKeyUp;
        _mouseHook.ButtonDown += OnMouseButtonDown;
        _mouseHook.ButtonUp += OnMouseButtonUp;
        _keyboardHook.ShouldSuppressKey = ShouldSuppressKey;
        _mouseHook.ShouldSuppressButton = ShouldSuppressButton;
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

            IsActive = true;
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

    public void Stop()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            IsActive = false;
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
        }

        ActiveChanged?.Invoke(this, false);
        _logger.LogInformation("Input mapping inactive.");
    }

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
                var (scaledAnchorX, scaledAnchorY) = ScalePointToScreen(rawAnchorX, rawAnchorY);
                _logger.LogInformation(
                    "Camera: RawAnchor={RX},{RY} (profile {PW}x{PH}) → ScaledAnchor={SX},{SY} (screen {SW}x{SH})",
                    rawAnchorX, rawAnchorY, _profile.ResolutionWidth, _profile.ResolutionHeight,
                    scaledAnchorX, scaledAnchorY, _screenWidth, _screenHeight);
                _cameraMouseLook.Start(_profile.Camera, scaledAnchorX, scaledAnchorY);
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
                    var (scaledX, scaledY) = ScalePointToScreen(binding.CenterX, binding.CenterY);
                    _logger.LogInformation("HandlePressedInput: Calling TouchDown({ContactId}, {ScaledX}, {ScaledY}) for MouseArea '{Name}'", contactId, scaledX, scaledY, binding.Name);
                    _touchEngine.StartTouch(contactId, scaledX, scaledY);
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

        _logger.LogInformation(
            "UpdateJoystickBindings: ProfileResolution={PW}x{PH}, ScreenResolution={SW}x{SH}",
            _profile.ResolutionWidth, _profile.ResolutionHeight, _screenWidth, _screenHeight);

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

            var (scaledCenterX, scaledCenterY) = ScalePointToScreen(binding.CenterX, binding.CenterY);
            var (scaledTargetX, scaledTargetY) = ScalePointToScreen(targetX, targetY);

            _logger.LogInformation(
                "Joystick: BindingCenter={BCX},{BCY} → ScaledCenter={SCX},{SCY}, " +
                "BindingTarget={BTX},{BTY} → ScaledTarget={STX},{STY}",
                binding.CenterX, binding.CenterY, scaledCenterX, scaledCenterY,
                targetX, targetY, scaledTargetX, scaledTargetY);

            if (!_joysticks.TryGetValue(binding.Id, out var state))
            {
                state = new JoystickRuntimeState(GetJoystickContactId(binding));
                _joysticks[binding.Id] = state;
                _touchEngine.StartTouch(state.ContactId, scaledCenterX, scaledCenterY);
            }

            _touchEngine.MoveTouch(state.ContactId, scaledTargetX, scaledTargetY);
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
                        var (scaledX, scaledY) = ScalePointToScreen(binding.CenterX, binding.CenterY);
                        _touchEngine.StartTouch(contactId, scaledX, scaledY);
                        await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                        _touchEngine.EndTouch(contactId);
                        break;
                    }
                case BindingKind.DoubleTap:
                    {
                        var (scaledX, scaledY) = ScalePointToScreen(binding.CenterX, binding.CenterY);
                        _touchEngine.StartTouch(contactId, scaledX, scaledY);
                        await Task.Delay(30, cancellationToken).ConfigureAwait(false);
                        _touchEngine.EndTouch(contactId);
                        await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                        _touchEngine.StartTouch(contactId, scaledX, scaledY);
                        await Task.Delay(30, cancellationToken).ConfigureAwait(false);
                        _touchEngine.EndTouch(contactId);
                        break;
                    }
                case BindingKind.Hold:
                    {
                        var (scaledX, scaledY) = ScalePointToScreen(binding.CenterX, binding.CenterY);
                        _touchEngine.StartTouch(contactId, scaledX, scaledY);
                        await Task.Delay(Math.Max(35, binding.HoldMilliseconds), cancellationToken).ConfigureAwait(false);
                        _touchEngine.EndTouch(contactId);
                        break;
                    }
                case BindingKind.Swipe:
                    {
                        var (scaledStartX, scaledY) = ScalePointToScreen(binding.X, binding.CenterY);
                        var (scaledEndX, _) = ScalePointToScreen(binding.X + binding.Width, binding.CenterY);
                        const int steps = 10;
                        _touchEngine.StartTouch(contactId, scaledStartX, scaledY);
                        for (int i = 1; i <= steps; i++)
                        {
                            double t = (double)i / steps;
                            double x = scaledStartX + (scaledEndX - scaledStartX) * t;
                            _touchEngine.MoveTouch(contactId, x, scaledY);
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
                        var (scaledX, scaledY) = ScalePointToScreen(binding.CenterX, binding.CenterY);
                        await _touchSimulator.TapAsync(scaledX, scaledY, cancellationToken: CancellationToken.None).ConfigureAwait(false);
                        break;
                    }
                case BindingKind.DoubleTap:
                    {
                        var (scaledX, scaledY) = ScalePointToScreen(binding.CenterX, binding.CenterY);
                        await _touchSimulator.DoubleTapAsync(scaledX, scaledY, CancellationToken.None).ConfigureAwait(false);
                        break;
                    }
                case BindingKind.Hold:
                    {
                        var (scaledX, scaledY) = ScalePointToScreen(binding.CenterX, binding.CenterY);
                        await _touchSimulator.HoldAsync(GetActionContactId(binding), scaledX, scaledY, Math.Max(35, binding.HoldMilliseconds), CancellationToken.None).ConfigureAwait(false);
                        break;
                    }
                case BindingKind.Swipe:
                    {
                        var (scaledStartX, scaledY) = ScalePointToScreen(binding.X, binding.CenterY);
                        var (scaledEndX, _) = ScalePointToScreen(binding.X + binding.Width, binding.CenterY);
                        await _touchSimulator.SwipeAsync(
                            GetActionContactId(binding),
                            scaledStartX,
                            scaledY,
                            scaledEndX,
                            scaledY,
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
        lock (_gate)
        {
            if (_disposed || !IsActive || _profile is null)
            {
                return false;
            }

            var pressed = new HashSet<int>(_pressedKeys) { virtualKey };
            if (_hotkeyParser.Matches(_profile.EnableHotkey, virtualKey, pressed) ||
                _hotkeyParser.Matches(_profile.DisableHotkey, virtualKey, pressed) ||
                _hotkeyParser.Matches(_profile.ToggleOverlayHotkey, virtualKey, pressed) ||
                _hotkeyParser.Matches(_profile.EditorHotkey, virtualKey, pressed))
            {
                return false;
            }

            if (IsWasdKey(virtualKey) && _profile.Bindings.Any(IsWasdJoystick))
            {
                return true;
            }

            if (_hotkeyParser.Matches(_profile.Camera.ActivationHotkey, virtualKey, pressed))
            {
                return true;
            }

            return _profile.Bindings.Any(binding =>
                binding.IsActive &&
                !binding.UseNativeInput &&
                _hotkeyParser.Matches(binding.Hotkey, virtualKey, pressed));
        }
    }

    private bool ShouldSuppressButton(int virtualKey)
    {
        lock (_gate)
        {
            if (_disposed || !IsActive || _profile is null)
            {
                return false;
            }

            var pressed = new HashSet<int>(_pressedKeys) { virtualKey };
            return _profile.Bindings.Any(binding =>
                binding.IsActive &&
                !binding.UseNativeInput &&
                _hotkeyParser.Matches(binding.Hotkey, virtualKey, pressed));
        }
    }

    private static int GetJoystickContactId(ControlBinding binding)
    {
        return (int)Models.FixedContacts.Joystick;
    }

    private (double X, double Y) ScalePointToScreen(double x, double y)
    {
        if (_profile == null)
        {
            return (x, y);
        }

        _screenWidth = Win32.NativeMethods.GetSystemMetrics(Win32.NativeMethods.SM_CXSCREEN);
        _screenHeight = Win32.NativeMethods.GetSystemMetrics(Win32.NativeMethods.SM_CYSCREEN);
        return _coordinateScaler.ScalePoint(
            x, y,
            _profile.ResolutionWidth, _profile.ResolutionHeight,
            _screenWidth, _screenHeight);
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
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _gestureCancellation.Cancel();
            IsActive = false;
            _cameraMouseLook.Stop();
            _gamepadMapper.Stop();
            ReleaseAllJoysticks();
            _touchSimulator.ReleaseAll();
            _pressedKeys.Clear();
        }

        _keyboardHook.KeyDown -= OnKeyDown;
        _keyboardHook.KeyUp -= OnKeyUp;
        _mouseHook.ButtonDown -= OnMouseButtonDown;
        _mouseHook.ButtonUp -= OnMouseButtonUp;
        _keyboardHook.ShouldSuppressKey = null;
        _mouseHook.ShouldSuppressButton = null;
        _keyboardHook.Stop();
        _mouseHook.Stop();
        _gestureCancellation.Dispose();
        foreach (var actionLock in _actionLocks.Values) actionLock.Dispose();
    }
}
