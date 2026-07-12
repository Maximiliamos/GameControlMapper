using System.Windows.Input;
using GameControlMapper.Models;
using GameControlMapper.Win32;
using Microsoft.Extensions.Logging;

namespace GameControlMapper.Services;

public sealed class XInputGamepadMapper : IDisposable
{
    private const uint ErrorSuccess = 0;
    private const ushort ButtonDPadUp = 0x0001;
    private const ushort ButtonDPadLeft = 0x0004;
    private const ushort ButtonDPadRight = 0x0008;
    private const ushort ButtonLeftShoulder = 0x0100;
    private const ushort ButtonRightShoulder = 0x0200;
    private const ushort ButtonB = 0x2000;
    private const ushort ButtonX = 0x4000;
    private const ushort ButtonY = 0x8000;

    private readonly IInputSimulator _inputSimulator;
    private readonly ILogger<XInputGamepadMapper> _logger;
    private readonly object _gate = new();
    private readonly HashSet<string> _heldKeys = [];
    private CancellationTokenSource? _cancellation;
    private Task? _worker;
    private MapperProfile? _profile;
    private bool _leftMouseDown;
    private bool _rightMouseDown;

    public XInputGamepadMapper(IInputSimulator inputSimulator, ILogger<XInputGamepadMapper> logger)
    {
        _inputSimulator = inputSimulator;
        _logger = logger;
    }

    public bool IsRunning => _worker is { IsCompleted: false };

    public void SetProfile(MapperProfile profile)
    {
        lock (_gate)
        {
            _profile = profile;
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (IsRunning)
            {
                return;
            }

            _cancellation = new CancellationTokenSource();
            _worker = Task.Run(() => RunAsync(_cancellation.Token));
        }

        _logger.LogInformation("XInput gamepad mapper started.");
    }

    public void Stop()
    {
        CancellationTokenSource? cancellation;
        Task? worker;
        lock (_gate)
        {
            cancellation = _cancellation;
            worker = _worker;
            _cancellation = null;
            _worker = null;
        }

        cancellation?.Cancel();
        try
        {
            worker?.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch
        {
            // Best effort shutdown; held inputs are released below.
        }

        cancellation?.Dispose();
        ReleaseAll();
        _logger.LogInformation("XInput gamepad mapper stopped.");
    }

    public void Dispose()
    {
        Stop();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            MapperProfile? profile;
            lock (_gate)
            {
                profile = _profile;
            }

            if (profile?.Gamepad.Enabled == true && TryGetState(out var state))
            {
                ApplyState(profile.Gamepad, state.Gamepad);
            }
            else
            {
                ReleaseAll();
            }

            await Task.Delay(12, cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool TryGetState(out NativeMethods.XINPUT_STATE state)
    {
        for (uint index = 0; index < 4; index++)
        {
            if (NativeMethods.XInputGetState(index, out state) == ErrorSuccess)
            {
                return true;
            }
        }

        state = default;
        return false;
    }

    private void ApplyState(GamepadMappingSettings settings, NativeMethods.XINPUT_GAMEPAD gamepad)
    {
        var leftX = NormalizeStick(gamepad.sThumbLX);
        var leftY = NormalizeStick(gamepad.sThumbLY);
        SetKey(settings.MoveForwardKey, leftY > settings.LeftStickDeadZone);
        SetKey(settings.MoveBackKey, leftY < -settings.LeftStickDeadZone);
        SetKey(settings.MoveLeftKey, leftX < -settings.LeftStickDeadZone);
        SetKey(settings.MoveRightKey, leftX > settings.LeftStickDeadZone);

        var rightX = NormalizeStick(gamepad.sThumbRX);
        var rightY = NormalizeStick(gamepad.sThumbRY);
        if (Math.Abs(rightX) > settings.RightStickDeadZone || Math.Abs(rightY) > settings.RightStickDeadZone)
        {
            var dx = (int)Math.Round(rightX * settings.MouseSensitivityX);
            var dy = (int)Math.Round(-rightY * settings.MouseSensitivityY);
            if (dx != 0 || dy != 0)
            {
                _inputSimulator.MoveRelative(dx, dy);
            }
        }

        SetMouse(SimulatedMouseButton.Left, gamepad.bRightTrigger > 35 || IsDown(gamepad.wButtons, ButtonRightShoulder), ref _leftMouseDown);
        SetMouse(SimulatedMouseButton.Right, gamepad.bLeftTrigger > 35 || IsDown(gamepad.wButtons, ButtonLeftShoulder), ref _rightMouseDown);
        SetKey(settings.RepairKey, IsDown(gamepad.wButtons, ButtonX));
        SetKey(settings.Consumable2Key, IsDown(gamepad.wButtons, ButtonY));
        SetKey(settings.Consumable3Key, IsDown(gamepad.wButtons, ButtonB));
        SetKey(settings.Shell1Key, IsDown(gamepad.wButtons, ButtonDPadLeft));
        SetKey(settings.Shell2Key, IsDown(gamepad.wButtons, ButtonDPadUp));
        SetKey(settings.Shell3Key, IsDown(gamepad.wButtons, ButtonDPadRight));
    }

    private void SetKey(string key, bool down)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (down && _heldKeys.Add(key))
        {
            _inputSimulator.KeyDown(key);
        }
        else if (!down && _heldKeys.Remove(key))
        {
            _inputSimulator.KeyUp(key);
        }
    }

    private void SetMouse(SimulatedMouseButton button, bool down, ref bool state)
    {
        if (down == state)
        {
            return;
        }

        state = down;
        if (down)
        {
            _inputSimulator.MouseDown(button);
        }
        else
        {
            _inputSimulator.MouseUp(button);
        }
    }

    private void ReleaseAll()
    {
        foreach (var key in _heldKeys.ToArray())
        {
            _inputSimulator.KeyUp(key);
            _heldKeys.Remove(key);
        }

        if (_leftMouseDown)
        {
            _leftMouseDown = false;
            _inputSimulator.MouseUp(SimulatedMouseButton.Left);
        }

        if (_rightMouseDown)
        {
            _rightMouseDown = false;
            _inputSimulator.MouseUp(SimulatedMouseButton.Right);
        }
    }

    private static double NormalizeStick(short value)
    {
        return value >= 0 ? value / 32767d : value / 32768d;
    }

    private static bool IsDown(ushort buttons, ushort mask)
    {
        return (buttons & mask) == mask;
    }
}
