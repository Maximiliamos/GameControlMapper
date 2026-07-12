using GameControlMapper.Win32;
using Microsoft.Extensions.Logging;

namespace GameControlMapper.Services;

public interface ITargetWindowActivationNativeAdapter
{
    nint GetForegroundWindow();
    nint GetRootWindow(nint hwnd);
    uint GetProcessId(nint hwnd);
    bool IsWindow(nint hwnd);
    bool IsIconic(nint hwnd);
    nint InstallForegroundHook(Action<nint> callback);
    void UninstallForegroundHook(nint hook);
}

public interface ITargetWindowActivationMonitor : IDisposable
{
    event EventHandler? ActivationChanged;
    bool TryGetForeground(out nint rootWindow, out uint processId);
}

public sealed class TargetWindowActivationMonitor : ITargetWindowActivationMonitor
{
    private readonly ITargetWindowActivationNativeAdapter _native;
    private readonly ILogger<TargetWindowActivationMonitor> _logger;
    private readonly nint _hook;
    private int _disposed;

    public TargetWindowActivationMonitor(ITargetWindowActivationNativeAdapter native, ILogger<TargetWindowActivationMonitor> logger)
    {
        _native = native;
        _logger = logger;
        _hook = native.InstallForegroundHook(OnNativeForeground);
        if (_hook == 0) _logger.LogError("Foreground monitor installation failed; input mapping will fail closed.");
    }

    public event EventHandler? ActivationChanged;
    public bool TryGetForeground(out nint rootWindow, out uint processId)
    {
        rootWindow = 0; processId = 0;
        if (Volatile.Read(ref _disposed) != 0 || _hook == 0) return false;
        var foreground = _native.GetForegroundWindow();
        if (foreground == 0 || !_native.IsWindow(foreground) || _native.IsIconic(foreground)) return false;
        rootWindow = _native.GetRootWindow(foreground);
        if (rootWindow == 0 || !_native.IsWindow(rootWindow)) return false;
        processId = _native.GetProcessId(rootWindow);
        return processId != 0;
    }

    private void OnNativeForeground(nint hwnd)
    {
        if (Volatile.Read(ref _disposed) == 0)
            ThreadPool.QueueUserWorkItem(_ => { if (Volatile.Read(ref _disposed) == 0) ActivationChanged?.Invoke(this, EventArgs.Empty); });
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_hook != 0) _native.UninstallForegroundHook(_hook);
        ActivationChanged = null;
    }
}

public sealed class WindowsTargetWindowActivationNativeAdapter : ITargetWindowActivationNativeAdapter
{
    private NativeMethods.WinEventProc? _callback;
    public nint GetForegroundWindow() => NativeMethods.GetForegroundWindow();
    public nint GetRootWindow(nint hwnd) => NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
    public uint GetProcessId(nint hwnd) { NativeMethods.GetWindowThreadProcessId(hwnd, out var pid); return pid; }
    public bool IsWindow(nint hwnd) => NativeMethods.IsWindow(hwnd);
    public bool IsIconic(nint hwnd) => NativeMethods.IsIconic(hwnd);
    public nint InstallForegroundHook(Action<nint> callback)
    {
        _callback = (_, _, hwnd, _, _, _, _) => callback(hwnd);
        return NativeMethods.SetWinEventHook(NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND, 0, _callback, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
    }
    public void UninstallForegroundHook(nint hook) { NativeMethods.UnhookWinEvent(hook); _callback = null; }
}
