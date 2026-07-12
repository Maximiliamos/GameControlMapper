using GameControlMapper.Models;
using Microsoft.Extensions.Logging;

namespace GameControlMapper.Services;

public interface ITargetWindowSessionValidator
{
    bool ValidateActiveSession();
}

public sealed class TargetWindowSessionManager : ITargetWindowSessionValidator
{
    public const CoordinateScaleMode ProductionScaleMode = CoordinateScaleMode.Stretch;

    private readonly IGameWindowGeometryProvider _geometryProvider;
    private readonly ILogger<TargetWindowSessionManager> _logger;
    private readonly IGameWindowNativeAdapter? _native;
    private readonly ITargetWindowActivationMonitor? _activationMonitor;
    private readonly ITargetWindowGeometryMonitor? _geometryMonitor;
    private readonly object _gate = new();
    private TargetWindowSession? _session;
    private long _generation;
    private long _invalidatedGeneration;

    public TargetWindowSessionManager(IGameWindowGeometryProvider geometryProvider, ILogger<TargetWindowSessionManager> logger,
        IGameWindowNativeAdapter? native = null, ITargetWindowActivationMonitor? activationMonitor = null,
        ITargetWindowGeometryMonitor? geometryMonitor = null)
    {
        _geometryProvider = geometryProvider;
        _logger = logger;
        _native = native;
        _activationMonitor = activationMonitor;
        _geometryMonitor = geometryMonitor;
        if (_geometryMonitor is not null) _geometryMonitor.Invalidated += OnGeometryInvalidated;
    }

    public TargetWindowSession? Current
    {
        get { lock (_gate) return _session; }
    }

    public TargetSessionStartResult TryStart(nint windowHandle, ProfileSize profileSize)
    {
        if (windowHandle == 0)
            return new(false, null, "A target window must be selected before starting input mapping.");
        if (!double.IsFinite(profileSize.Width) || !double.IsFinite(profileSize.Height) || profileSize.Width <= 0 || profileSize.Height <= 0)
            return new(false, null, "Profile dimensions must be finite and greater than zero.");

        var root = _native?.GetAncestor(windowHandle, GameControlMapper.Win32.NativeMethods.GA_ROOT) ?? windowHandle;
        if (root == 0) return new(false, null, "Unable to normalize the target window.");
        var processId = _native?.GetWindowProcessId(root) ?? 1;
        if (processId == 0) return new(false, null, "Unable to identify the target process.");
        if (_activationMonitor is not null &&
            (!_activationMonitor.TryGetForeground(out var foregroundRoot, out var foregroundPid) || foregroundRoot != root || foregroundPid != processId))
            return new(false, null, "The selected target window is not the foreground window.");

        var geometry = _geometryProvider.GetClientRect(root);
        if (!geometry.Succeeded)
            return new(false, null, geometry.Error);

        lock (_gate)
        {
            _session = new TargetWindowSession(
                root,
                profileSize,
                ProductionScaleMode,
                geometry.ClientRect,
                processId,
                ++_generation,
                true);
            Volatile.Write(ref _invalidatedGeneration, 0);
            _geometryMonitor?.Track(_session);
            return new(true, _session, null);
        }
    }

    public bool IsForegroundActive()
    {
        TargetWindowSession? snapshot;
        lock (_gate) snapshot = _session;
        if (snapshot is null || !snapshot.IsActive || _activationMonitor is null) return _activationMonitor is null && snapshot?.IsActive == true;
        return _activationMonitor.TryGetForeground(out var root, out var pid) &&
               root == snapshot.WindowHandle && pid == snapshot.ProcessId;
    }

    public bool ValidateActiveSession()
    {
        TargetWindowSession? snapshot;
        lock (_gate) snapshot = _session;
        if (snapshot is null) return true;
        if (Volatile.Read(ref _invalidatedGeneration) == snapshot.Generation) return false;
        if (!snapshot.IsActive) return true;

        return snapshot.IsActive;
    }

    private void OnGeometryInvalidated(object? sender, long generation)
    {
        lock (_gate)
        {
            if (_session?.Generation != generation || !_session.IsActive) return;
            _session = _session with { IsActive = false };
            Volatile.Write(ref _invalidatedGeneration, generation);
        }
        _logger.LogWarning("Target window session {Generation} became invalid after cached geometry validation.", generation);
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_session is not null) { _geometryMonitor?.Stop(_session.Generation); Volatile.Write(ref _invalidatedGeneration, 0); _session = _session with { IsActive = false }; }
        }
    }
}
