namespace GameControlMapper.Models;

public sealed record TargetWindowSession(
    nint WindowHandle,
    ProfileSize ProfileSize,
    CoordinateScaleMode ScaleMode,
    PhysicalClientRect ClientRect,
    uint ProcessId,
    long Generation,
    bool IsActive);

public sealed record TargetSessionStartResult(bool Succeeded, TargetWindowSession? Session, string? Error);
