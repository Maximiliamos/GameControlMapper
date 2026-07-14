using GameControlMapper.Models;
using GameControlMapper.Win32;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

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
    private readonly ILogger<TargetWindowGeometryMonitor> _logger;
    private TargetWindowSession? _tracked;
    private int _validationQueued;
    private int _disposed;
    private int _callbackFailures;

    public TargetWindowGeometryMonitor(IGameWindowGeometryProvider geometry, IGameWindowNativeAdapter native,
        ITargetWindowGeometryNativeAdapter events, TimeProvider timeProvider,ILogger<TargetWindowGeometryMonitor>? logger=null)
    {
        _geometry = geometry; _native = native; _events = events;
        _logger=logger??NullLogger<TargetWindowGeometryMonitor>.Instance;
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
        var queuedSession=Volatile.Read(ref _tracked);
        if(queuedSession is null){Interlocked.Exchange(ref _validationQueued,0);return;}
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                var current = Volatile.Read(ref _tracked);
                if (Volatile.Read(ref _disposed)!=0||current is null||current.Generation!=queuedSession.Generation) return;
                var pidMatches = _native.GetWindowProcessId(current.WindowHandle) == current.ProcessId;
                var result = _geometry.GetClientRect(current.WindowHandle);
                if (!pidMatches || !result.Succeeded || result.ClientRect != current.ClientRect)
                {
                    if(Interlocked.CompareExchange(ref _tracked,null,current)!=current)return;
                    if(Volatile.Read(ref _disposed)==0)Invalidated?.Invoke(this, current.Generation);
                }
            }
            catch(Exception ex)
            {
                if(Interlocked.Increment(ref _callbackFailures)==1)_logger.LogError(ex,"Target geometry callback failed; mapping remains fail-closed");
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
    private readonly object _gate=new();
    private nint _hook;
    private NativeMethods.WinEventProc? _callback;
    public nint Install(Action<nint> callback)
    {
        lock(_gate)
        {
            if(_hook!=0)return _hook;
            _callback = (_, _, hwnd, _, _, _, _) => {try{callback(hwnd);}catch{}};
            _hook=NativeMethods.SetWinEventHook(0x8001,0x800B,0,_callback,0,0,NativeMethods.WINEVENT_OUTOFCONTEXT);
            if(_hook==0)_callback=null;
            return _hook;
        }
    }
    public void Uninstall(nint hook)
    {
        lock(_gate)
        {
            if(hook==0||hook!=_hook)return;
            NativeMethods.UnhookWinEvent(_hook);_hook=0;_callback=null;
        }
    }
}
