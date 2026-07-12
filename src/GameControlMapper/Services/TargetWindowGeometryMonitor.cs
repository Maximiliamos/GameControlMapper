using GameControlMapper.Models;
using GameControlMapper.Win32;

namespace GameControlMapper.Services;

public interface ITargetWindowGeometryNativeAdapter
{
    nint Install(Action<nint> callback);
    void Uninstall(nint hook);
}

public interface ITargetWindowGeometryMonitor : IDisposable
{
    event EventHandler<long>? Invalidated;
    void Track(TargetWindowSession session);
    void Stop(long generation);
}

public sealed class TargetWindowGeometryMonitor : ITargetWindowGeometryMonitor
{
    private readonly IGameWindowGeometryProvider _geometry;
    private readonly IGameWindowNativeAdapter _native;
    private readonly ITargetWindowGeometryNativeAdapter _events;
    private readonly ITimer _timer;
    private readonly nint _hook;
    private TargetWindowSession? _tracked;
    private int _validationQueued;
    private int _disposed;

    public TargetWindowGeometryMonitor(IGameWindowGeometryProvider geometry, IGameWindowNativeAdapter native,
        ITargetWindowGeometryNativeAdapter events, TimeProvider timeProvider)
    {
        _geometry = geometry; _native = native; _events = events;
        _hook = events.Install(OnNativeEvent);
        _timer = timeProvider.CreateTimer(_ => QueueValidation(), null, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));
    }

    public event EventHandler<long>? Invalidated;
    public void Track(TargetWindowSession session) => Volatile.Write(ref _tracked, session);
    public void Stop(long generation)
    {
        var current = Volatile.Read(ref _tracked);
        if (current?.Generation == generation) Volatile.Write(ref _tracked, null);
    }
    private void OnNativeEvent(nint hwnd)
    {
        var current = Volatile.Read(ref _tracked);
        if (current is not null && (hwnd == 0 || hwnd == current.WindowHandle)) QueueValidation();
    }
    private void QueueValidation()
    {
        if (Volatile.Read(ref _disposed) != 0 || Interlocked.Exchange(ref _validationQueued, 1) != 0) return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var current = Volatile.Read(ref _tracked);
                if (current is null) return;
                var pidMatches = _native.GetWindowProcessId(current.WindowHandle) == current.ProcessId;
                var result = _geometry.GetClientRect(current.WindowHandle);
                if (!pidMatches || !result.Succeeded || result.ClientRect != current.ClientRect)
                {
                    Volatile.Write(ref _tracked, null);
                    Invalidated?.Invoke(this, current.Generation);
                }
            }
            finally { Interlocked.Exchange(ref _validationQueued, 0); }
        });
    }
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _timer.Dispose(); if (_hook != 0) _events.Uninstall(_hook); Volatile.Write(ref _tracked, null); Invalidated = null;
    }
}

public sealed class WindowsTargetWindowGeometryNativeAdapter : ITargetWindowGeometryNativeAdapter
{
    private readonly List<nint> _hooks = [];
    private NativeMethods.WinEventProc? _callback;
    public nint Install(Action<nint> callback)
    {
        _callback = (_, _, hwnd, _, _, _, _) => callback(hwnd);
        foreach (var eventId in new uint[] { 0x8001, 0x8003, 0x800B, 0x0016, 0x0017 })
        {
            var hook = NativeMethods.SetWinEventHook(eventId, eventId, 0, _callback, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
            if (hook != 0) _hooks.Add(hook);
        }
        return _hooks.Count == 0 ? 0 : 1;
    }
    public void Uninstall(nint hook) { foreach (var item in _hooks) NativeMethods.UnhookWinEvent(item); _hooks.Clear(); _callback = null; }
}
