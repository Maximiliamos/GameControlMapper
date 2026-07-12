using System.Runtime.InteropServices;
using System.Windows.Input;
using GameControlMapper.Models;
using GameControlMapper.Win32;
using Microsoft.Extensions.Logging;

namespace GameControlMapper.Services;

public sealed class SendInputSimulator : IInputSimulator
{
    private readonly ILogger<SendInputSimulator> _logger;

    public SendInputSimulator(ILogger<SendInputSimulator> logger)
    {
        _logger = logger;
    }

    public async Task ExecuteBindingAsync(ControlBinding binding, CancellationToken cancellationToken = default)
    {
        if (InputModeGuard.TouchInjectionMode || !binding.IsActive)
        {
            return;
        }

        if (binding.DelayMilliseconds > 0)
        {
            await Task.Delay(binding.DelayMilliseconds, cancellationToken);
        }

        switch (binding.Kind)
        {
            case BindingKind.Tap:
                await ClickAsync(binding.CenterX, binding.CenterY, cancellationToken: cancellationToken);
                break;
            case BindingKind.DoubleTap:
                await DoubleClickAsync(binding.CenterX, binding.CenterY, cancellationToken: cancellationToken);
                break;
            case BindingKind.Hold:
                await HoldAsync(binding.CenterX, binding.CenterY, binding.HoldMilliseconds, cancellationToken);
                break;
            case BindingKind.Swipe:
                await SwipeAsync(binding.X, binding.CenterY, binding.X + binding.Width, binding.CenterY, Math.Max(80, binding.HoldMilliseconds), cancellationToken);
                break;
            case BindingKind.Macro:
            case BindingKind.Sequence:
                await ExecuteMacroAsync(binding.Actions, cancellationToken);
                break;
            case BindingKind.Joystick:
            case BindingKind.Aim:
            case BindingKind.MouseArea:
                await ClickAsync(binding.CenterX, binding.CenterY, cancellationToken: cancellationToken);
                break;
            default:
                _logger.LogWarning("Unsupported binding type {Kind}", binding.Kind);
                break;
        }
    }

    public async Task ClickAsync(double x, double y, SimulatedMouseButton button = SimulatedMouseButton.Left, CancellationToken cancellationToken = default)
    {
        if (InputModeGuard.TouchInjectionMode) return;

        var original = GetCursorPosition();
        MouseDownAt(x, y, button);
        await Task.Delay(20, cancellationToken);
        MouseUp(button);
        RestoreCursor(original.X, original.Y);
    }

    public async Task DoubleClickAsync(double x, double y, SimulatedMouseButton button = SimulatedMouseButton.Left, CancellationToken cancellationToken = default)
    {
        if (InputModeGuard.TouchInjectionMode) return;
        await ClickAsync(x, y, button, cancellationToken);
        await Task.Delay(60, cancellationToken);
        await ClickAsync(x, y, button, cancellationToken);
    }

    public async Task SwipeAsync(double startX, double startY, double endX, double endY, int durationMilliseconds, CancellationToken cancellationToken = default)
    {
        if (InputModeGuard.TouchInjectionMode) return;

        var original = GetCursorPosition();
        MouseDownAt(startX, startY);
        var steps = Math.Max(4, durationMilliseconds / 12);
        for (var index = 1; index <= steps; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var t = (double)index / steps;
            MouseMoveTo(startX + (endX - startX) * t, startY + (endY - startY) * t);
            await Task.Delay(Math.Max(1, durationMilliseconds / steps), cancellationToken);
        }

        MouseUp();
        RestoreCursor(original.X, original.Y);
    }

    public void MouseDownAt(double x, double y, SimulatedMouseButton button = SimulatedMouseButton.Left)
    {
        if (InputModeGuard.TouchInjectionMode) return;
        MouseMoveTo(x, y);
        SendMouse(GetMouseDownFlag(button));
    }

    public void MouseMoveTo(double x, double y)
    {
        if (InputModeGuard.TouchInjectionMode) return;
        MoveAbsolute(x, y);
    }

    public void MouseUp(SimulatedMouseButton button = SimulatedMouseButton.Left)
    {
        if (InputModeGuard.TouchInjectionMode) return;
        SendMouse(GetMouseUpFlag(button));
    }

    public void MouseDown(SimulatedMouseButton button = SimulatedMouseButton.Left)
    {
        if (InputModeGuard.TouchInjectionMode) return;
        SendMouse(GetMouseDownFlag(button));
    }

    public void KeyDown(string key)
    {
        if (InputModeGuard.TouchInjectionMode) return;
        SendKey(key, false);
    }

    public void KeyUp(string key)
    {
        if (InputModeGuard.TouchInjectionMode) return;
        SendKey(key, true);
    }

    public (int X, int Y) GetCursorPosition()
    {
        if (InputModeGuard.TouchInjectionMode) return (0,0);
        return NativeMethods.GetCursorPos(out var point) ? (point.X, point.Y) : (0,0);
    }

    public void RestoreCursor(int x, int y)
    {
        if (InputModeGuard.TouchInjectionMode) return;
        NativeMethods.SetCursorPos(x, y);
    }

    public void MoveRelative(int dx, int dy)
    {
        if (InputModeGuard.TouchInjectionMode) return;
        SendMouse(NativeMethods.MOUSEEVENTF_MOVE, dx, dy);
    }

    private async Task HoldAsync(double x, double y, int milliseconds, CancellationToken cancellationToken)
    {
        if (InputModeGuard.TouchInjectionMode) return;

        var original = GetCursorPosition();
        MouseDownAt(x, y);
        await Task.Delay(Math.Max(1, milliseconds), cancellationToken);
        MouseUp();
        RestoreCursor(original.X, original.Y);
    }

    private async Task ExecuteMacroAsync(IEnumerable<MacroAction> actions, CancellationToken cancellationToken)
    {
        if (InputModeGuard.TouchInjectionMode) return;

        foreach (var action in actions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            switch (action.Kind)
            {
                case MacroActionKind.Delay:
                    await Task.Delay(Math.Max(0, action.DelayMilliseconds), cancellationToken);
                    break;
                case MacroActionKind.MouseDown:
                    MouseDownAt(action.X, action.Y);
                    break;
                case MacroActionKind.MouseUp:
                    MouseUp();
                    break;
                case MacroActionKind.Click:
                    await ClickAsync(action.X, action.Y, cancellationToken: cancellationToken);
                    break;
                case MacroActionKind.Move:
                    MouseMoveTo(action.X, action.Y);
                    break;
                case MacroActionKind.KeyDown:
                    SendKey(action.Key, false);
                    break;
                case MacroActionKind.KeyUp:
                    SendKey(action.Key, true);
                    break;
            }
        }
    }

    private static void MoveAbsolute(double x, double y)
    {
        var width = Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN) - 1);
        var height = Math.Max(1, NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN) - 1);
        var normalizedX = (int)Math.Round(Math.Clamp(x, 0, width) * 65535 / width);
        var normalizedY = (int)Math.Round(Math.Clamp(y, 0, height) * 65535 / height);
        SendMouse(NativeMethods.MOUSEEVENTF_MOVE | NativeMethods.MOUSEEVENTF_ABSOLUTE, normalizedX, normalizedY);
    }

    private static void SendMouse(uint flags, int dx = 0, int dy = 0)
    {
        var inputs = new[]
        {
            new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_MOUSE,
                u = new NativeMethods.InputUnion
                {
                    mi = new NativeMethods.MOUSEINPUT { dx = dx, dy = dy, dwFlags = flags }
                }
            }
        };
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static uint GetMouseDownFlag(SimulatedMouseButton button)
    {
        return button switch
        {
            SimulatedMouseButton.Right => NativeMethods.MOUSEEVENTF_RIGHTDOWN,
            SimulatedMouseButton.Middle => NativeMethods.MOUSEEVENTF_MIDDLEDOWN,
            _ => NativeMethods.MOUSEEVENTF_LEFTDOWN
        };
    }

    private static uint GetMouseUpFlag(SimulatedMouseButton button)
    {
        return button switch
        {
            SimulatedMouseButton.Right => NativeMethods.MOUSEEVENTF_RIGHTUP,
            SimulatedMouseButton.Middle => NativeMethods.MOUSEEVENTF_MIDDLEUP,
            _ => NativeMethods.MOUSEEVENTF_LEFTUP
        };
    }

    private static void SendKey(string keyText, bool keyUp)
    {
        var key = ParseKey(keyText);
        if (key == Key.None)
        {
            return;
        }

        var virtualKey = (ushort)KeyInterop.VirtualKeyFromKey(key);
        var inputs = new[]
        {
            new NativeMethods.INPUT
            {
                type = NativeMethods.INPUT_KEYBOARD,
                u = new NativeMethods.InputUnion
                {
                    ki = new NativeMethods.KEYBDINPUT
                    {
                        wVk = virtualKey,
                        dwFlags = keyUp ? NativeMethods.KEYEVENTF_KEYUP : 0
                    }
                }
            }
        };
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static Key ParseKey(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Key.None;
        }

        try
        {
            var converted = new KeyConverter().ConvertFromInvariantString(text);
            return converted is Key key ? key : Key.None;
        }
        catch
        {
            return Key.None;
        }
    }
}
