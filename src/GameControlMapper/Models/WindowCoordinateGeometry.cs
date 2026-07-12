namespace GameControlMapper.Models;

public readonly record struct ProfilePoint(double X, double Y);

public readonly record struct ProfileSize(double Width, double Height);

public readonly record struct PhysicalScreenPoint(int X, int Y);

/// <summary>Client area in absolute physical Windows screen pixels.</summary>
public readonly record struct PhysicalClientRect(int Left, int Top, int Width, int Height);

/// <summary>Content viewport in absolute physical screen pixels before final point rounding.</summary>
public readonly record struct ContentViewport(double Left, double Top, double Width, double Height);

public enum CoordinateScaleMode
{
    Stretch,
    UniformFit
}

public sealed record CoordinateTransformResult(
    bool Succeeded,
    PhysicalScreenPoint Point,
    ContentViewport Viewport,
    bool IsOutsideProfile,
    string? Error)
{
    public static CoordinateTransformResult Failure(string error) =>
        new(false, default, default, false, error);
}

public sealed record WindowGeometryResult(
    bool Succeeded,
    PhysicalClientRect ClientRect,
    string? Operation,
    int Win32ErrorCode,
    string? Error)
{
    public static WindowGeometryResult Failure(string operation, int errorCode, string error) =>
        new(false, default, operation, errorCode, error);

    public static WindowGeometryResult Success(PhysicalClientRect rect) =>
        new(true, rect, null, 0, null);
}
