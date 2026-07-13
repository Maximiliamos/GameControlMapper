using System.Runtime.InteropServices;
using GameControlMapper.Win32;
using Microsoft.Extensions.Logging;

namespace GameControlMapper.Services;

public sealed class WindowsTouchSimulator : ITouchSimulator
{
    private const uint MaxTouchCount = 10;
    private const int FirstTransientContactId = 3;
    private const int LastTransientContactId = 9;
    private const int ContactRadius = 7;
    private const uint DefaultTouchPressure = 512;
    private readonly ILogger<WindowsTouchSimulator> _logger;
    private readonly object _gate = new();
    private readonly Dictionary<int, (double x, double y)> _contacts = new();
    private readonly HashSet<int> _activeContacts = new();
    private int _nextTransientId = FirstTransientContactId - 1;
    private bool _initialized;

    public WindowsTouchSimulator(ILogger<WindowsTouchSimulator> logger)
    {
        _logger = logger;
        InitializeInjection();
    }

    private void InitializeInjection()
    {
        _initialized = NativeMethods.InitializeTouchInjection(MaxTouchCount, NativeMethods.TOUCH_FEEDBACK_NONE);
        if (!_initialized)
        {
            var error = Marshal.GetLastWin32Error();
            _logger.LogError("InitializeTouchInjection failed! Win32 error: {Error}", error);
        }
        else
        {
            _logger.LogInformation("InitializeTouchInjection succeeded!");
        }
    }

    public void TouchDown(int contactId, double x, double y)
    {
        lock (_gate)
        {
            if (!_initialized)
            {
                _logger.LogWarning("TouchDown ignored: touch injection not initialized");
                return;
            }

            if (!IsValidContactId(contactId))
            {
                _logger.LogWarning("TouchDown ignored: invalid contact id {ContactId}. Valid range 0..{Max}", contactId, MaxTouchCount - 1);
                return;
            }

            if (_activeContacts.Contains(contactId))
            {
                _logger.LogWarning("TouchDown ignored: contact {ContactId} is already active", contactId);
                return;
            }

            _logger.LogTrace("Legacy touch contact started");
            _contacts[contactId] = (x, y);
            if (InjectSingleContact(contactId, (x, y), 0))
            {
                _activeContacts.Add(contactId);
            }
        }
    }

    public void TouchMove(int contactId, double x, double y)
    {
        lock (_gate)
        {
            if (!_initialized)
            {
                _logger.LogWarning("TouchMove ignored: touch injection not initialized");
                return;
            }

            if (!_activeContacts.Contains(contactId))
            {
                _logger.LogWarning("TouchMove ignored: contact {ContactId} is not active", contactId);
                return;
            }

            _logger.LogDebug("TouchMove: contact {ContactId} at ({X}, {Y})", contactId, x, y);
            _contacts[contactId] = (x, y);
            InjectSingleContact(contactId, (x, y), 1);
        }
    }

    public void TouchUp(int contactId)
    {
        lock (_gate)
        {
            if (!_initialized)
            {
                _logger.LogWarning("TouchUp ignored: touch injection not initialized");
                return;
            }

            if (!_activeContacts.Contains(contactId))
            {
                _logger.LogWarning("TouchUp ignored: contact {ContactId} is not active", contactId);
                return;
            }

            _logger.LogTrace("Legacy touch contact ended");
            if (_contacts.TryGetValue(contactId, out var pos))
            {
                InjectSingleContact(contactId, pos, 2);
                _contacts.Remove(contactId);
            }

            _activeContacts.Remove(contactId);
        }
    }

    public void ReleaseAll()
    {
        lock (_gate)
        {
            if (!_initialized)
            {
                _logger.LogInformation("ReleaseAll: touch injection not initialized");
                return;
            }

            _logger.LogInformation("ReleaseAll: releasing {Count} active contacts", _contacts.Count);
            foreach (var kvp in _contacts.ToArray())
            {
                InjectSingleContact(kvp.Key, kvp.Value, 2);
            }

            _contacts.Clear();
            _activeContacts.Clear();
        }
    }

    public async Task TapAsync(double x, double y, int milliseconds = 35, CancellationToken cancellationToken = default)
    {
        var contactId = GetNextTransientId();
        TouchDown(contactId, x, y);
        try
        {
            await Task.Delay(Math.Max(milliseconds, 50), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TouchUp(contactId);
            ReturnTransientId(contactId);
        }
    }

    public async Task DoubleTapAsync(double x, double y, CancellationToken cancellationToken = default)
    {
        await TapAsync(x, y, 30, cancellationToken).ConfigureAwait(false);
        await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        await TapAsync(x, y, 30, cancellationToken).ConfigureAwait(false);
    }

    public async Task HoldAsync(int contactId, double x, double y, int milliseconds, CancellationToken cancellationToken = default)
    {
        TouchDown(contactId, x, y);
        try
        {
            await Task.Delay(Math.Max(milliseconds, 50), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TouchUp(contactId);
        }
    }

    public async Task SwipeAsync(int contactId, double startX, double startY, double endX, double endY, int milliseconds, CancellationToken cancellationToken = default)
    {
        TouchDown(contactId, startX, startY);
        try
        {
            const int steps = 10;
            for (int i = 1; i <= steps; i++)
            {
                double t = (double)i / steps;
                double x = startX + (endX - startX) * t;
                double y = startY + (endY - startY) * t;
                TouchMove(contactId, x, y);
                await Task.Delay(Math.Max(milliseconds / steps, 10), cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            TouchUp(contactId);
        }
    }

    private int GetNextTransientId()
    {
        lock (_gate)
        {
            var nextId = Interlocked.Increment(ref _nextTransientId);
            if (nextId > LastTransientContactId)
            {
                nextId = FirstTransientContactId;
                Interlocked.Exchange(ref _nextTransientId, FirstTransientContactId);
            }
            return nextId;
        }
    }

    private void ReturnTransientId(int contactId)
    {
    }

    private bool InjectSingleContact(int contactId, (double x, double y) pos, int phase)
    {
        var touchInfo = CreateTouchInfo(contactId, pos, phase);
        var result = NativeMethods.InjectTouchInput(1, new NativeMethods.POINTER_TOUCH_INFO[] { touchInfo });
        if (!result)
        {
            var error = Marshal.GetLastWin32Error();
            _logger.LogError("InjectTouchInput failed! Win32 error: {Error}", error);
        }
        return result;
    }

    private NativeMethods.POINTER_TOUCH_INFO CreateTouchInfo(int contactId, (double x, double y) pos, int phase)
    {
        var screenWidth = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
        var screenHeight = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);

        var x = Math.Clamp((int)Math.Round(pos.x), 0, screenWidth - 1);
        var y = Math.Clamp((int)Math.Round(pos.y), 0, screenHeight - 1);

        uint flags = phase switch
        {
            0 => NativeMethods.POINTER_FLAG_DOWN | NativeMethods.POINTER_FLAG_INRANGE | NativeMethods.POINTER_FLAG_INCONTACT,
            1 => NativeMethods.POINTER_FLAG_UPDATE | NativeMethods.POINTER_FLAG_INRANGE | NativeMethods.POINTER_FLAG_INCONTACT,
            2 => NativeMethods.POINTER_FLAG_UP,
            _ => NativeMethods.POINTER_FLAG_NONE
        };

        return new NativeMethods.POINTER_TOUCH_INFO
        {
            pointerInfo = new NativeMethods.POINTER_INFO
            {
                pointerType = NativeMethods.PT_TOUCH,
                pointerId = (uint)contactId,
                frameId = 0,
                pointerFlags = flags,
                sourceDevice = IntPtr.Zero,
                hwndTarget = IntPtr.Zero,
                ptPixelLocation = new NativeMethods.POINT { X = x, Y = y },
                ptHimetricLocation = new NativeMethods.POINT(),
                ptPixelLocationRaw = new NativeMethods.POINT(),
                ptHimetricLocationRaw = new NativeMethods.POINT(),
                dwTime = 0,
                historyCount = 0,
                inputData = 0,
                dwKeyStates = 0,
                PerformanceCount = 0,
                ButtonChangeType = 0
            },
            touchFlags = 0,
            touchMask = NativeMethods.TOUCH_MASK_CONTACTAREA | NativeMethods.TOUCH_MASK_ORIENTATION | NativeMethods.TOUCH_MASK_PRESSURE,
            rcContact = new NativeMethods.RECT
            {
                Left = x - ContactRadius,
                Top = y - ContactRadius,
                Right = x + ContactRadius,
                Bottom = y + ContactRadius
            },
            rcContactRaw = new NativeMethods.RECT(),
            orientation = 0,
            pressure = DefaultTouchPressure
        };
    }

    private bool IsValidContactId(int contactId)
    {
        return contactId >= 0 && contactId < MaxTouchCount;
    }
}
