using System.Diagnostics;
using System.Runtime.InteropServices;
using GameControlMapper.Win32;
using GameControlMapper.Models;
using Microsoft.Extensions.Logging;

namespace GameControlMapper.Services;

public sealed class MouseHookService : IDisposable
{
    private readonly ILogger<MouseHookService> _logger;
    private readonly NativeMethods.LowLevelProc _callback;
    private IntPtr _hook;
    private NativeMethods.POINT? _lastPoint;
    private NativeMethods.POINT _lastRawPoint;
    private int _suppressedMoveCount;
    private volatile bool _captureMovement;
    private volatile bool _suppressTouchPromotedMouseEvents;
    private volatile bool _isolateCursorFromTouch;
    private long _protectedCursorPosition;
    private int _hasProtectedCursorPosition;
    private int _cursorRestoreInProgress;
    private int _queuedDx;
    private int _queuedDy;
    private int _moveDispatchScheduled;
    private int _queuedRawDx;
    private int _queuedRawDy;
    private int _rawMoveDispatchScheduled;
    private readonly object _inputQueueGate=new();
    private readonly Queue<Action> _inputQueue=[];
    private bool _inputDrainScheduled;
    private bool _disposed;

    public MouseHookService(ILogger<MouseHookService> logger)
    {
        _logger = logger;
        _callback = HookCallback;
    }

    public event Action<int, int>? MouseMoved;
    public event EventHandler<(int Dx, int Dy)>? Moved;
    public event EventHandler<int>? ButtonDown;
    public event EventHandler<int>? ButtonUp;
    public event EventHandler<GeneratedInputEvent>? GeneratedButtonDown;
    public event EventHandler<GeneratedInputEvent>? GeneratedButtonUp;
    public Func<int, bool>? ShouldSuppressButton { get; set; }
    public Func<long>? CaptureGeneration { get; set; }
    public bool CaptureMovement
    {
        get => _captureMovement;
        set => _captureMovement = value;
    }
    public bool SuppressTouchPromotedMouseEvents{get=>_suppressTouchPromotedMouseEvents;set=>_suppressTouchPromotedMouseEvents=value;}

    public void SetCursorIsolation(bool enabled)
    {
        _isolateCursorFromTouch = enabled;
        if (enabled && NativeMethods.GetCursorPos(out var point))
        {
            RememberPhysicalCursor(point);
        }
        else if (!enabled)
        {
            Volatile.Write(ref _hasProtectedCursorPosition, 0);
        }
    }

    public void ResetMovementTracking()
    {
        _lastPoint = null;
        _lastRawPoint = default;
    }

    public void SuppressNextMove()
    {
        Interlocked.Increment(ref _suppressedMoveCount);
    }

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
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _callback, moduleHandle, 0);
        if (_hook == IntPtr.Zero)
        {
            _logger.LogWarning("Mouse hook was not installed. Win32 error: {Error}", Marshal.GetLastWin32Error());
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
        _lastPoint = null;
        Moved = null;
        ButtonDown = null;
        ButtonUp = null;
        GeneratedButtonDown = null;
        GeneratedButtonUp = null;
        ShouldSuppressButton = null;
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
            var message = wParam.ToInt32();
            var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            if (_suppressTouchPromotedMouseEvents && IsTouchPromotedMouseEvent(data.dwExtraInfo))
            {
                RestoreCursorAfterTouchFrame();
                return new IntPtr(1);
            }
            if ((data.flags & NativeMethods.LLMHF_INJECTED) == NativeMethods.LLMHF_INJECTED)
            {
                if (_captureMovement && message == NativeMethods.WM_MOUSEMOVE) return new IntPtr(1);
                return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
            }

            if (message == NativeMethods.WM_MOUSEMOVE)
            {
                // Обработка для нового MouseMoved события (чистые dx, dy из WH_MOUSE_LL)
                if (_lastRawPoint.X != 0 || _lastRawPoint.Y != 0)
                {
                    int dx = data.pt.X - _lastRawPoint.X;
                    int dy = data.pt.Y - _lastRawPoint.Y;
                    if (dx != 0 || dy != 0)
                    {
                        QueueRawMoveEvent(dx, dy);
                    }
                }
                _lastRawPoint = data.pt;

                // Оставляем старый код для совместимости
                var captureMovement = CaptureMovement;
                if (Interlocked.CompareExchange(ref _suppressedMoveCount, 0, 0) > 0)
                {
                    Interlocked.Decrement(ref _suppressedMoveCount);
                    _lastPoint = data.pt;
                    return captureMovement ? new IntPtr(1) : NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
                }

                if (captureMovement && _lastPoint is { } previous)
                {
                    QueueMoveEvent(data.pt.X - previous.X, data.pt.Y - previous.Y);
                }

                _lastPoint = data.pt;
                if (captureMovement)
                {
                    return new IntPtr(1);
                }
            }
            else if (TryGetMouseVirtualKey(message, out var virtualKey, out var isDown))
            {
                if (isDown)
                {
                    QueueButtonEvent(ButtonDown, GeneratedButtonDown, virtualKey, CaptureGeneration?.Invoke() ?? 0);
                }
                else
                {
                    QueueButtonEvent(ButtonUp, GeneratedButtonUp, virtualKey, CaptureGeneration?.Invoke() ?? 0);
                }

                if (ShouldSuppressButton?.Invoke(virtualKey) == true)
                {
                    return new IntPtr(1);
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    internal static bool IsTouchPromotedMouseEvent(IntPtr extraInfo) =>
        (unchecked((ulong)extraInfo.ToInt64()) & NativeMethods.MI_WP_SIGNATURE_MASK) == NativeMethods.MI_WP_SIGNATURE;

    public void OnRawPhysicalMouseMoved(int dx, int dy)
    {
        if (!_isolateCursorFromTouch || _captureMovement || (dx == 0 && dy == 0)) return;
        if (NativeMethods.GetCursorPos(out var point)) RememberPhysicalCursor(point);
    }

    private void RememberPhysicalCursor(NativeMethods.POINT point)
    {
        Interlocked.Exchange(ref _protectedCursorPosition, Pack(point));
        Volatile.Write(ref _hasProtectedCursorPosition, 1);
    }

    public void RestoreCursorAfterTouchFrame()
    {
        if (!_isolateCursorFromTouch || Volatile.Read(ref _hasProtectedCursorPosition) == 0 ||
            Interlocked.Exchange(ref _cursorRestoreInProgress, 1) != 0)
        {
            return;
        }

        try
        {
            var point = Unpack(Interlocked.Read(ref _protectedCursorPosition));
            NativeMethods.SetCursorPos(point.X, point.Y);
        }
        finally
        {
            Volatile.Write(ref _cursorRestoreInProgress, 0);
        }
    }

    internal static long Pack(NativeMethods.POINT point) =>
        ((long)point.X << 32) | (uint)point.Y;

    internal static NativeMethods.POINT Unpack(long value) => new()
    {
        X = (int)(value >> 32),
        Y = unchecked((int)(uint)value)
    };

    private static bool TryGetMouseVirtualKey(int message, out int virtualKey, out bool isDown)
    {
        virtualKey = message switch
        {
            NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_LBUTTONUP => NativeMethods.VK_LBUTTON,
            NativeMethods.WM_RBUTTONDOWN or NativeMethods.WM_RBUTTONUP => NativeMethods.VK_RBUTTON,
            NativeMethods.WM_MBUTTONDOWN or NativeMethods.WM_MBUTTONUP => NativeMethods.VK_MBUTTON,
            _ => 0
        };
        isDown = message is NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_RBUTTONDOWN or NativeMethods.WM_MBUTTONDOWN;
        return virtualKey != 0;
    }

    private void QueueMoveEvent(int dx, int dy)
    {
        var handler = Moved;
        if (handler is null)
        {
            return;
        }

        Interlocked.Add(ref _queuedDx, dx);
        Interlocked.Add(ref _queuedDy, dy);
        if (Interlocked.Exchange(ref _moveDispatchScheduled, 1) == 1)
        {
            return;
        }

        ThreadPool.QueueUserWorkItem(_ =>
        {
            while(true)
            {
                var totalDx = Interlocked.Exchange(ref _queuedDx, 0);
                var totalDy = Interlocked.Exchange(ref _queuedDy, 0);
                if (!_disposed && (totalDx != 0 || totalDy != 0))
                {
                    Moved?.Invoke(this, (totalDx, totalDy));
                }
                Interlocked.Exchange(ref _moveDispatchScheduled,0);
                if(Volatile.Read(ref _queuedDx)==0&&Volatile.Read(ref _queuedDy)==0)break;
                if(Interlocked.CompareExchange(ref _moveDispatchScheduled,1,0)!=0)break;
            }
        });
    }

    private void QueueRawMoveEvent(int dx, int dy)
    {
        if (MouseMoved is null) return;
        Interlocked.Add(ref _queuedRawDx, dx);
        Interlocked.Add(ref _queuedRawDy, dy);
        if (Interlocked.Exchange(ref _rawMoveDispatchScheduled, 1) == 1) return;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            while(true)
            {
                var totalDx = Interlocked.Exchange(ref _queuedRawDx, 0);
                var totalDy = Interlocked.Exchange(ref _queuedRawDy, 0);
                if (!_disposed && (totalDx != 0 || totalDy != 0)) MouseMoved?.Invoke(totalDx, totalDy);
                Interlocked.Exchange(ref _rawMoveDispatchScheduled,0);
                if(Volatile.Read(ref _queuedRawDx)==0&&Volatile.Read(ref _queuedRawDy)==0)break;
                if(Interlocked.CompareExchange(ref _rawMoveDispatchScheduled,1,0)!=0)break;
            }
        });
    }

    private void QueueButtonEvent(EventHandler<int>? handler, EventHandler<GeneratedInputEvent>? generated, int virtualKey, long generation)
    {
        if (handler is null && generated is null)
        {
            return;
        }

        EnqueueInput(() =>
        {
            if (!_disposed)
            {
                generated?.Invoke(this, new GeneratedInputEvent(virtualKey, generation));
                handler?.Invoke(this, virtualKey);
            }
        });
    }

    private void EnqueueInput(Action action)
    {
        lock(_inputQueueGate){_inputQueue.Enqueue(action);if(_inputDrainScheduled)return;_inputDrainScheduled=true;}
        ThreadPool.QueueUserWorkItem(_=>{while(true){Action? next;lock(_inputQueueGate){if(_inputQueue.Count==0){_inputDrainScheduled=false;return;}next=_inputQueue.Dequeue();}try{next();}catch(Exception ex){_logger.LogError(ex,"Mouse input dispatch failed");}}});
    }
}
