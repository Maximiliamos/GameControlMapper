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
    private readonly object _gate = new();
    private TargetWindowSession? _session;
    private long _generation;

    public TargetWindowSessionManager(IGameWindowGeometryProvider geometryProvider, ILogger<TargetWindowSessionManager> logger)
    {
        _geometryProvider = geometryProvider;
        _logger = logger;
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

        var geometry = _geometryProvider.GetClientRect(windowHandle);
        if (!geometry.Succeeded)
            return new(false, null, geometry.Error);

        lock (_gate)
        {
            _session = new TargetWindowSession(
                windowHandle,
                profileSize,
                ProductionScaleMode,
                geometry.ClientRect,
                ++_generation,
                true);
            return new(true, _session, null);
        }
    }

    public bool ValidateActiveSession()
    {
        TargetWindowSession? snapshot;
        lock (_gate) snapshot = _session;
        if (snapshot is null || !snapshot.IsActive) return true;

        var geometry = _geometryProvider.GetClientRect(snapshot.WindowHandle);
        if (geometry.Succeeded && geometry.ClientRect == snapshot.ClientRect) return true;

        lock (_gate)
        {
            if (_session?.Generation == snapshot.Generation)
                _session = snapshot with { IsActive = false };
        }
        _logger.LogWarning("Target window session {Generation} became invalid: {Reason}", snapshot.Generation,
            geometry.Succeeded ? "client geometry changed" : geometry.Error);
        return false;
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_session is not null) _session = _session with { IsActive = false };
        }
    }
}
