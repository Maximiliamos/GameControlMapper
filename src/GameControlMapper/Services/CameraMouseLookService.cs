using GameControlMapper.Models;
using Microsoft.Extensions.Logging;

namespace GameControlMapper.Services;

public sealed class CameraMouseLookService : IDisposable
{
    private const double RebaseThresholdRatio = 0.78;
    private const double MaximumPendingDelta = 4096;

    private readonly TouchEngine _touch;
    private readonly ILogger<CameraMouseLookService> _logger;
    private readonly TimeProvider _time;
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
        _time = time ?? TimeProvider.System;
        _target = target;
        _scheduler = scheduler;
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
            _x = _anchor.X;
            _y = _anchor.Y;
            _vx = _vy = 0;
            _pendingDx = _pendingDy = 0;
            _rebasing = false;
            _generation = target?.Generation ?? ++_nextGeneration;
            _lastTimestamp = _time.GetTimestamp();
            _lease = _touch.StartTouch(_generation, "camera", _x, _y);
            if (_lease is null)
            {
                _generation = 0;
                _logger.LogWarning("Camera start rejected: no touch contact is available");
                return false;
            }

            _cycleCancellation?.Dispose();
            _cycleCancellation = new CancellationTokenSource();
            Interlocked.Exchange(ref _rebaseCount, 0);
            _active = true;
            started = true;
        }

        if (started) ActiveChanged?.Invoke(this, true);
        return started;
    }

    public void OnMouseMove(int dx, int dy)
    {
        lock (_gate) ProcessMoveLocked(dx, dy, _generation);
    }

    public void OnMouseMove(int dx, int dy, long generation)
    {
        lock (_gate) ProcessMoveLocked(dx, dy, generation);
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
        var distance = Math.Sqrt(ox * ox + oy * oy);
        var radius = Math.Max(24, _settings.DragRadius);
        var threshold = radius * RebaseThresholdRatio;
        if (distance >= threshold && distance > 0)
        {
            _x = _anchor.X + ox * threshold / distance;
            _y = _anchor.Y + oy * threshold / distance;
        }

        if (!double.IsFinite(_x) || !double.IsFinite(_y) || _lease is null) return;
        _touch.MoveTouch(_lease, _x, _y);
        if (distance >= threshold) BeginRebaseLocked(_lease, generation);
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
                nextLease = _touch.StartTouch(generation, "camera:handoff", _anchor.X, _anchor.Y);
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
                _x = _anchor.X;
                _y = _anchor.Y;
            }

            // Send old Up and new Down atomically before applying accumulated movement.
            await PumpFrameAsync(cancellationToken).ConfigureAwait(false);
            lock (_gate)
            {
                if (!_active || _disposed || generation != _generation || !ReferenceEquals(_lease, nextLease)) return;
                Interlocked.Increment(ref _rebaseCount);
                var pendingDx = _pendingDx;
                var pendingDy = _pendingDy;
                _pendingDx = _pendingDy = 0;
                _rebasing = false;
                _lastTimestamp = _time.GetTimestamp();
                if (pendingDx != 0 || pendingDy != 0) ProcessMoveLocked(pendingDx, pendingDy, generation);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            lock (_gate) FailCycleLocked("Camera contact rebase failed.", ex);
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

    private void FailCycleLocked(string message, Exception? exception = null)
    {
        var lease = _lease;
        _lease = null;
        _rebasing = false;
        _pendingDx = _pendingDy = 0;
        _active = false;
        _generation = 0;
        _cycleCancellation?.Cancel();
        if (lease is not null) _touch.EndTouch(lease);
        if (exception is null) _logger.LogError("{Message}", message);
        else _logger.LogError(exception, "{Message}", message);
        ActiveChanged?.Invoke(this, false);
    }

    public void Stop()
    {
        TouchContactLease? lease;
        CancellationTokenSource? cancellation;
        bool changed;
        lock (_gate)
        {
            lease = _lease;
            _lease = null;
            cancellation = _cycleCancellation;
            _cycleCancellation = null;
            changed = _active;
            _active = false;
            _rebasing = false;
            _pendingDx = _pendingDy = 0;
            _generation = 0;
        }

        cancellation?.Cancel();
        if (lease is not null) _touch.EndTouch(lease);
        cancellation?.Dispose();
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
    }
}
