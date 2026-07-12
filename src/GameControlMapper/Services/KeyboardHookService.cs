using System.Diagnostics;
using System.Runtime.InteropServices;
using GameControlMapper.Win32;
using GameControlMapper.Models;
using Microsoft.Extensions.Logging;

namespace GameControlMapper.Services;

public sealed class KeyboardHookService : IDisposable
{
    private readonly ILogger<KeyboardHookService> _logger;
    private readonly NativeMethods.LowLevelProc _callback;
    private IntPtr _hook;
    private bool _disposed;

    public KeyboardHookService(ILogger<KeyboardHookService> logger)
    {
        _logger = logger;
        _callback = HookCallback;
    }

    public event EventHandler<int>? KeyDown;
    public event EventHandler<int>? KeyUp;
    public event EventHandler<GeneratedInputEvent>? GeneratedKeyDown;
    public event EventHandler<GeneratedInputEvent>? GeneratedKeyUp;
    public Func<int, bool>? ShouldSuppressKey { get; set; }
    public Func<long>? CaptureGeneration { get; set; }

    public void Start()
    {
        if (_disposed)
        {
            return;
        }

        if (_hook != IntPtr.Zero)
        {
            return;
        }

        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule;
        var moduleHandle = NativeMethods.GetModuleHandle(module?.ModuleName);
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _callback, moduleHandle, 0);
        if (_hook == IntPtr.Zero)
        {
            _logger.LogWarning("Keyboard hook was not installed. Win32 error: {Error}", Marshal.GetLastWin32Error());
        }
    }

    public void Stop()
    {
        _disposed = true;
        if (_hook == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.UnhookWindowsHookEx(_hook);
        _hook = IntPtr.Zero;
        KeyDown = null;
        KeyUp = null;
        GeneratedKeyDown = null;
        GeneratedKeyUp = null;
        ShouldSuppressKey = null;
        CaptureGeneration = null;
    }

    public void Dispose()
    {
        Stop();
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (!_disposed && nCode >= 0)
        {
            var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            var message = wParam.ToInt32();
            if (message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
            {
                QueueKeyEvent(KeyDown, GeneratedKeyDown, data.vkCode, CaptureGeneration?.Invoke() ?? 0);
                if (ShouldSuppressKey?.Invoke(data.vkCode) == true)
                {
                    return new IntPtr(1);
                }
            }
            else if (message is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP)
            {
                QueueKeyEvent(KeyUp, GeneratedKeyUp, data.vkCode, CaptureGeneration?.Invoke() ?? 0);
                if (ShouldSuppressKey?.Invoke(data.vkCode) == true)
                {
                    return new IntPtr(1);
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void QueueKeyEvent(EventHandler<int>? handler, EventHandler<GeneratedInputEvent>? generated, int virtualKey, long generation)
    {
        if (handler is null && generated is null)
        {
            return;
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            if (!_disposed)
            {
                generated?.Invoke(this, new GeneratedInputEvent(virtualKey, generation));
                handler?.Invoke(this, virtualKey);
            }
        });
    }
}
