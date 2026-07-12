using GameControlMapper.Models;

namespace GameControlMapper.Services;

public sealed class CoordinateScaler
{
    public ControlBinding ScaleBinding(ControlBinding binding, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        var scaleX = GetScale(sourceWidth, targetWidth);
        var scaleY = GetScale(sourceHeight, targetHeight);
        var scaled = new ControlBinding
        {
            Id = binding.Id,
            Name = binding.Name,
            Kind = binding.Kind,
            Hotkey = binding.Hotkey,
            X = Math.Round(binding.X * scaleX, 2),
            Y = Math.Round(binding.Y * scaleY, 2),
            Width = Math.Round(binding.Width * scaleX, 2),
            Height = Math.Round(binding.Height * scaleY, 2),
            Color = binding.Color,
            Opacity = binding.Opacity,
            HoldMilliseconds = binding.HoldMilliseconds,
            DelayMilliseconds = binding.DelayMilliseconds,
            Repeat = binding.Repeat,
            Priority = binding.Priority,
            Comment = binding.Comment,
            IsActive = binding.IsActive,
            UseNativeInput = binding.UseNativeInput,
            Actions = binding.Actions.Select(action => action.Clone()).ToList()
        };
        return scaled;
    }

    public (double X, double Y) ScalePoint(double x, double y, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
    {
        return (Math.Round(x * GetScale(sourceWidth, targetWidth), 2), Math.Round(y * GetScale(sourceHeight, targetHeight), 2));
    }

    private static double GetScale(int source, int target)
    {
        if (source <= 0 || target <= 0)
        {
            return 1d;
        }

        return (double)target / source;
    }
}
