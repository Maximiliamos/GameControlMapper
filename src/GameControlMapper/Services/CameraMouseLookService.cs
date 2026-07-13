using GameControlMapper.Models;
using Microsoft.Extensions.Logging;

namespace GameControlMapper.Services;

public sealed class CameraMouseLookService : IDisposable
{
    private const double RebaseThresholdRatio = 0.96;
    private const double MaximumPendingDelta = 4096;
    private const double ActivationDeltaThreshold = 3;

    private readonly TouchEngine _touch;
    private readonly ILogger<CameraMouseLookService> _logger;
    private readonly TimeProvider _time;
    private readonly IMouseCursorController? _cursor;
    private readonly TargetWindowSessionManager? _target;
    private readonly TouchScheduler? _scheduler;
    private readonly object _gate = new();

    private CameraSettings _settings = new();
    private bool _active;
    private bool _disposed;
    private bool _rebasing;
    private double _x;
    private double _y;
    private double _vx;
    private double _vy;
    private double _pendingDx;
    private double _pendingDy;
    private double _frameDx;
    private double _frameDy;
    private double _armingDx;
    private double _armingDy;
    private double _radiusX;
    private double _radiusY;
    private bool _cursorHidden;
    private int _cursorWarningLogged;
    private PhysicalScreenPoint _anchor;
    private long _generation;
    private long _lastTimestamp;
    private long _nextGeneration;
    private long _rebaseCount;
    private TouchContactLease? _lease;
    private CancellationTokenSource? _cycleCancellation;

    public CameraMouseLookService(
        TouchEngine touch,
        ILogger<CameraMouseLookService> logger,
        IMouseCursorController? cursor = null,
        TimeProvider? time = null,
        TargetWindowSessionManager? target = null,
        TouchScheduler? scheduler = null)
    {
        _touch = touch;
        _logger = logger;
        _cursor = cursor;
        _time = time ?? TimeProvider.System;
        _target = target;
        _scheduler = scheduler;
        if (_scheduler is not null) _scheduler.FrameSent += OnTouchFrameSent;
    }

    public event EventHandler<bool>? ActiveChanged;

    public bool IsActive
    {
        get { lock (_gate) return _active; }
    }

    public long Generation
    {
        get { lock (_gate) return _generation; }
    }

    public long RebaseCount => Interlocked.Read(ref _rebaseCount);

    public bool Start(CameraSettings settings, double anchorX, double anchorY)
    {
        var started = false;
        lock (_gate)
        {
            if (_disposed || _active) return false;

            var target = _target?.Current;
            if (_target is not null && (target is null || !target.IsActive))
            {
                _logger.LogWarning("Camera start rejected: target session is inactive");
                return false;
            }

            _settings = settings;
            _anchor = new PhysicalScreenPoint((int)Math.Round(anchorX), (int)Math.Round(anchorY));
            (_radiusX, _radiusY) = CalculateStrokeRadii(settings, target, _anchor);
            _x = _anchor.X;
            _y = _anchor.Y;
            _vx = _vy = 0;
            _pendingDx = _pendingDy = 0;
            _frameDx = _frameDy = 0;
            _armingDx = _armingDy = 0;
            _rebasing = false;
            _generation = target?.Generation ?? ++_nextGeneration;
            _lastTimestamp = _time.GetTimestamp();
            _lease = null;
            if (_cursor is not null && !_cursor.TrySetVisible(false))
            {
                _generation = 0;
                _logger.LogWarning("Camera start rejected: the system cursor could not be hidden");
                return false;
            }
            _cursorHidden = _cursor is not null;
            Interlocked.Exchange(ref _cursorWarningLogged, 0);

            _cycleCancellation?.Dispose();
            _cycleCancellation = new CancellationTokenSource();
            Interlocked.Exchange(ref _rebaseCount, 0);
            _active = true;
            started = true;
            _logger.LogInformation(
                "Camera armed without a touch-down; safe stroke radii are {RadiusX:F0}x{RadiusY:F0}px",
                _radiusX, _radiusY);
        }

        if (started)
        {
            if (_cursor is not null && _cycleCancellation is not null)
                _ = GuardCursorVisibilityAsync(_cycleCancellation.Token);
            ActiveChanged?.Invoke(this, true);
        }
        return started;
    }

    private async Task GuardCursorVisibilityAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                lock (_gate)
                {
                    if (!_active || !_cursorHidden) return;
                    if (_cursor?.TrySetVisible(false) == false &&
                        Interlocked.Exchange(ref _cursorWarningLogged, 1) == 0)
                        _logger.LogWarning("Camera cursor guard could not keep the system cursor hidden");
                }
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    public void OnMouseMove(int dx, int dy)
    {
        QueueOrProcessMove(dx, dy, null);
    }

    public void OnMouseMove(int dx, int dy, long generation)
    {
        QueueOrProcessMove(dx, dy, generation);
    }

    private void QueueOrProcessMove(int dx, int dy, long? generation)
    {
        var failed = false;
        var restoreCursor = false;
        lock (_gate)
        {
            var expectedGeneration = generation ?? _generation;
            if (!_active || _disposed || expectedGeneration != _generation) return;
            var magnitude = Math.Sqrt((double)dx * dx + (double)dy * dy);
            if (!double.IsFinite(magnitude) || magnitude <= Math.Max(0, _settings.DeadZone)) return;
            double inputDx = dx;
            double inputDy = dy;
            if (_lease is null && !_rebasing)
            {
                _armingDx = Math.Clamp(_armingDx + dx, -MaximumPendingDelta, MaximumPendingDelta);
                _armingDy = Math.Clamp(_armingDy + dy, -MaximumPendingDelta, MaximumPendingDelta);
                if (Math.Sqrt(_armingDx * _armingDx + _armingDy * _armingDy) < ActivationDeltaThreshold) return;
                inputDx = _armingDx;
                inputDy = _armingDy;
                _armingDx = _armingDy = 0;
            }
            if (_lease is null && !_rebasing && !TryStartDirectionalStrokeLocked(inputDx, inputDy, expectedGeneration, "camera"))
            {
                failed = true;
                restoreCursor = _cursorHidden;
                _cursorHidden = false;
                _active = false;
                _generation = 0;
                _cycleCancellation?.Cancel();
            }
            if (_scheduler is null)
            {
                if (!failed) ProcessMoveLocked(inputDx, inputDy, expectedGeneration);
            }
            else if (!failed)
            {
                _frameDx = Math.Clamp(_frameDx + inputDx, -MaximumPendingDelta, MaximumPendingDelta);
                _frameDy = Math.Clamp(_frameDy + inputDy, -MaximumPendingDelta, MaximumPendingDelta);
            }
        }

        if (restoreCursor) _cursor?.TrySetVisible(true);
        if (failed) ActiveChanged?.Invoke(this, false);
    }

    private bool TryStartDirectionalStrokeLocked(double dx, double dy, long generation, string owner)
    {
        var directionX = dx * (_settings.InvertX ? -1 : 1);
        var directionY = dy * (_settings.InvertY ? -1 : 1);
        var (startX, startY) = CalculateOppositeStrokeStart(directionX, directionY);
        var lease = _touch.StartTouch(generation, owner, startX, startY);
        if (lease is null)
        {
            _logger.LogWarning("Camera stroke rejected: no touch contact is available");
            return false;
        }

        _lease = lease;
        _x = startX;
        _y = startY;
        _logger.LogDebug("Camera stroke {Owner} started at {X:F0},{Y:F0}", owner, startX, startY);
        return true;
    }

    private void OnTouchFrameSent(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (!_active || _disposed) return;
            var dx = _frameDx;
            var dy = _frameDy;
            _frameDx = _frameDy = 0;
            if (dx != 0 || dy != 0) ProcessMoveLocked(dx, dy, _generation);
        }
    }

    private void ProcessMoveLocked(double dx, double dy, long generation)
    {
        if (!_active || _disposed || generation != _generation) return;
        if (_rebasing)
        {
            _pendingDx = Math.Clamp(_pendingDx + dx, -MaximumPendingDelta, MaximumPendingDelta);
            _pendingDy = Math.Clamp(_pendingDy + dy, -MaximumPendingDelta, MaximumPendingDelta);
            return;
        }

        var magnitude = Math.Sqrt(dx * dx + dy * dy);
        if (!double.IsFinite(magnitude) || magnitude <= Math.Max(0, _settings.DeadZone)) return;

        var sx = dx * _settings.SensitivityX * (_settings.InvertX ? -1 : 1);
        var sy = dy * _settings.SensitivityY * (_settings.InvertY ? -1 : 1);
        var factor = 1 + Math.Max(0, _settings.Acceleration) * magnitude;
        var tx = sx * factor;
        var ty = sy * factor;
        var now = _time.GetTimestamp();
        var dt = Math.Clamp(_time.GetElapsedTime(_lastTimestamp, now).TotalSeconds, 1e-4, 0.25);
        _lastTimestamp = now;
        var alpha = _settings.Smooth <= 0 ? 1 : 1 - Math.Exp(-dt / Math.Max(1e-4, _settings.Smooth));
        _vx += alpha * (tx - _vx);
        _vy += alpha * (ty - _vy);
        var speed = Math.Sqrt(_vx * _vx + _vy * _vy);
        var max = Math.Max(0, _settings.MaxSpeed);
        if (speed > max && speed > 0)
        {
            _vx *= max / speed;
            _vy *= max / speed;
        }

        _x += _vx;
        _y += _vy;
        var ox = _x - _anchor.X;
        var oy = _y - _anchor.Y;
        var normalizedDistance = Math.Sqrt(
            ox * ox / (_radiusX * _radiusX) +
            oy * oy / (_radiusY * _radiusY));
        if (normalizedDistance >= RebaseThresholdRatio && normalizedDistance > 0)
        {
            var clamp = RebaseThresholdRatio / normalizedDistance;
            _x = _anchor.X + ox * clamp;
            _y = _anchor.Y + oy * clamp;
        }

        if (!double.IsFinite(_x) || !double.IsFinite(_y) || _lease is null) return;
        _touch.MoveTouch(_lease, _x, _y);
        if (normalizedDistance >= RebaseThresholdRatio) BeginRebaseLocked(_lease, generation);
    }

    private void BeginRebaseLocked(TouchContactLease lease, long generation)
    {
        if (_rebasing || _cycleCancellation is null) return;
        _rebasing = true;
        _ = CycleContactAsync(lease, generation, _cycleCancellation.Token);
    }

    private async Task CycleContactAsync(TouchContactLease previousLease, long generation, CancellationToken cancellationToken)
    {
        try
        {
            // Preserve the final movement frame before lifting the old finger.
            await PumpFrameAsync(cancellationToken).ConfigureAwait(false);
            TouchContactLease? nextLease;
            lock (_gate)
            {
                if (!IsCurrentCycleLocked(previousLease, generation)) return;
                // Acquire a different contact before lifting the old one. The next
                // frame then contains old Up and new Down together, so the game
                // never observes an empty camera frame during unlimited rotation.
                var directionX = Math.Abs(_pendingDx) > 0.001
                    ? _pendingDx * (_settings.InvertX ? -1 : 1)
                    : _vx;
                var directionY = Math.Abs(_pendingDy) > 0.001
                    ? _pendingDy * (_settings.InvertY ? -1 : 1)
                    : _vy;
                var (nextX, nextY) = CalculateOppositeStrokeStart(directionX, directionY);
                nextLease = _touch.StartTouch(generation, "camera:handoff", nextX, nextY);
                if (nextLease is null)
                {
                    _rebasing = false;
                    _pendingDx = _pendingDy = 0;
                    _lastTimestamp = _time.GetTimestamp();
                    _logger.LogWarning("Camera contact handoff was postponed because no touch contact is available");
                    return;
                }

                _touch.EndTouch(previousLease);
                _lease = nextLease;
                _x = nextX;
                _y = nextY;
            }

            // Send old Up and new Down atomically before applying accumulated movement.
            await PumpFrameAsync(cancellationToken).ConfigureAwait(false);
            var sendContinuationFrame = false;
            lock (_gate)
            {
                if (!_active || _disposed || generation != _generation || !ReferenceEquals(_lease, nextLease)) return;
                var rebaseNumber = Interlocked.Increment(ref _rebaseCount);
                _logger.LogInformation("Camera stroke handoff {RebaseNumber} completed without an empty touch frame", rebaseNumber);
                var pendingDx = _pendingDx;
                var pendingDy = _pendingDy;
                _pendingDx = _pendingDy = 0;
                _rebasing = false;
                _lastTimestamp = _time.GetTimestamp();
                if (pendingDx != 0 || pendingDy != 0)
                {
                    ProcessMoveLocked(pendingDx, pendingDy, generation);
                    sendContinuationFrame = true;
                }
                else if (_lease is not null && (Math.Abs(_vx) > 0.001 || Math.Abs(_vy) > 0.001))
                {
                    _x += _vx;
                    _y += _vy;
                    _touch.MoveTouch(_lease, _x, _y);
                    sendContinuationFrame = true;
                }
            }
            if (sendContinuationFrame) await PumpFrameAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            var notify = false;
            var restoreCursor = false;
            lock (_gate) (notify, restoreCursor) = FailCycleLocked("Camera contact rebase failed.", ex);
            if (restoreCursor) _cursor?.TrySetVisible(true);
            if (notify) ActiveChanged?.Invoke(this, false);
        }
    }

    private bool IsCurrentCycleLocked(TouchContactLease lease, long generation) =>
        _active && !_disposed && generation == _generation && ReferenceEquals(_lease, lease);

    private async Task PumpFrameAsync(CancellationToken cancellationToken)
    {
        if (_scheduler is not null)
        {
            await _scheduler.SendFrameOnceAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await Task.Delay(12, cancellationToken).ConfigureAwait(false);
    }

    private (bool Changed, bool RestoreCursor) FailCycleLocked(string message, Exception? exception = null)
    {
        var changed = _active;
        var restoreCursor = _cursorHidden;
        _cursorHidden = false;
        var lease = _lease;
        _lease = null;
        _rebasing = false;
        _pendingDx = _pendingDy = 0;
        _armingDx = _armingDy = 0;
        _active = false;
        _generation = 0;
        _cycleCancellation?.Cancel();
        if (lease is not null) _touch.EndTouch(lease);
        if (exception is null) _logger.LogError("{Message}", message);
        else _logger.LogError(exception, "{Message}", message);
        return (changed, restoreCursor);
    }

    public void Stop()
    {
        TouchContactLease? lease;
        CancellationTokenSource? cancellation;
        bool changed;
        bool restoreCursor;
        lock (_gate)
        {
            lease = _lease;
            _lease = null;
            cancellation = _cycleCancellation;
            _cycleCancellation = null;
            changed = _active;
            restoreCursor = _cursorHidden;
            _cursorHidden = false;
            _active = false;
            _rebasing = false;
            _pendingDx = _pendingDy = 0;
            _frameDx = _frameDy = 0;
            _armingDx = _armingDy = 0;
            _generation = 0;
        }

        cancellation?.Cancel();
        if (lease is not null) _touch.EndTouch(lease);
        cancellation?.Dispose();
        if (restoreCursor) _cursor?.TrySetVisible(true);
        if (changed) ActiveChanged?.Invoke(this, false);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }

        Stop();
        if (_scheduler is not null) _scheduler.FrameSent -= OnTouchFrameSent;
    }

    private static (double X, double Y) CalculateStrokeRadii(
        CameraSettings settings, TargetWindowSession? target, PhysicalScreenPoint anchor)
    {
        var requested = Math.Max(24, settings.DragRadius);
        if (target is null) return (requested, requested);
        var rect = target.ClientRect;
        var right = rect.Left + rect.Width;
        var bottom = rect.Top + rect.Height;
        var availableX = Math.Max(8, Math.Min(anchor.X - rect.Left, right - anchor.X) - 16);
        var availableY = Math.Max(8, Math.Min(anchor.Y - rect.Top, bottom - anchor.Y) - 16);
        var radiusX = Math.Min(availableX, Math.Max(requested, rect.Width * 0.42));
        var radiusY = Math.Min(availableY, Math.Max(requested, rect.Height * 0.34));
        return (Math.Max(8, radiusX), Math.Max(8, radiusY));
    }

    private (double X, double Y) CalculateOppositeStrokeStart(double directionX, double directionY)
    {
        if (!double.IsFinite(directionX) || !double.IsFinite(directionY) ||
            (Math.Abs(directionX) < 0.001 && Math.Abs(directionY) < 0.001))
            return (_anchor.X, _anchor.Y);
        var normalized = Math.Sqrt(
            directionX * directionX / (_radiusX * _radiusX) +
            directionY * directionY / (_radiusY * _radiusY));
        if (!double.IsFinite(normalized) || normalized <= 0) return (_anchor.X, _anchor.Y);
        var scale = RebaseThresholdRatio / normalized;
        return (_anchor.X - directionX * scale, _anchor.Y - directionY * scale);
    }
}
