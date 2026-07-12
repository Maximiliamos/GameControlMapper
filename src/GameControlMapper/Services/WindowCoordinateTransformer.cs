using GameControlMapper.Models;

namespace GameControlMapper.Services;

/// <summary>
/// Converts profile coordinates into absolute physical screen pixels inside a physical client rectangle.
/// WPF device-independent pixels must be converted at the UI boundary before using this service.
/// </summary>
public sealed class WindowCoordinateTransformer
{
    public CoordinateTransformResult TryTransform(
        ProfilePoint point,
        ProfileSize profileSize,
        PhysicalClientRect clientRect,
        CoordinateScaleMode mode)
    {
        if (!IsFinite(point.X) || !IsFinite(point.Y))
            return CoordinateTransformResult.Failure("Profile point must contain finite values.");
        if (!IsFinite(profileSize.Width) || !IsFinite(profileSize.Height) || profileSize.Width <= 0 || profileSize.Height <= 0)
            return CoordinateTransformResult.Failure("Profile width and height must be finite and greater than zero.");
        if (clientRect.Width <= 0 || clientRect.Height <= 0)
            return CoordinateTransformResult.Failure("Client width and height must be greater than zero.");
        if (!Enum.IsDefined(mode))
            return CoordinateTransformResult.Failure("Unknown coordinate scale mode.");

        double scaleX;
        double scaleY;
        ContentViewport viewport;
        if (mode == CoordinateScaleMode.Stretch)
        {
            scaleX = clientRect.Width / profileSize.Width;
            scaleY = clientRect.Height / profileSize.Height;
            viewport = new ContentViewport(clientRect.Left, clientRect.Top, clientRect.Width, clientRect.Height);
        }
        else
        {
            var scale = Math.Min(clientRect.Width / profileSize.Width, clientRect.Height / profileSize.Height);
            scaleX = scale;
            scaleY = scale;
            var width = profileSize.Width * scale;
            var height = profileSize.Height * scale;
            viewport = new ContentViewport(
                clientRect.Left + (clientRect.Width - width) / 2d,
                clientRect.Top + (clientRect.Height - height) / 2d,
                width,
                height);
        }

        var screenX = viewport.Left + point.X * scaleX;
        var screenY = viewport.Top + point.Y * scaleY;
        if (!IsFinite(screenX) || !IsFinite(screenY) ||
            screenX < int.MinValue || screenX > int.MaxValue ||
            screenY < int.MinValue || screenY > int.MaxValue)
            return CoordinateTransformResult.Failure("Transformed point is outside the supported physical pixel range.");

        var outside = point.X < 0 || point.Y < 0 || point.X > profileSize.Width || point.Y > profileSize.Height;
        return new CoordinateTransformResult(
            true,
            new PhysicalScreenPoint(RoundPixel(screenX), RoundPixel(screenY)),
            viewport,
            outside,
            null);
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static int RoundPixel(double value) => checked((int)Math.Round(value, MidpointRounding.AwayFromZero));
}
