namespace GameControlMapper.Services;

public sealed record GameWindowInfo(
    nint Handle,
    string Title,
    string ProcessName,
    int X,
    int Y,
    int Width,
    int Height);
