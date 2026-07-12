using GameControlMapper.Models;
using GameControlMapper.Services;
using Xunit;

namespace GameControlMapper.Tests;

public sealed class CoordinateScalerTests
{
    [Fact]
    public void ScalePoint_UsesIndependentAxisScale()
    {
        var scaler = new CoordinateScaler();

        var point = scaler.ScalePoint(960, 540, 1920, 1080, 2560, 1440);

        Assert.Equal(1280, point.X);
        Assert.Equal(720, point.Y);
    }

    [Fact]
    public void ScaleBinding_PreservesIdentityAndScalesSize()
    {
        var scaler = new CoordinateScaler();
        var binding = new ControlBinding
        {
            Id = Guid.NewGuid(),
            Name = "Fire",
            X = 100,
            Y = 50,
            Width = 80,
            Height = 40
        };

        var scaled = scaler.ScaleBinding(binding, 1000, 500, 2000, 1000);

        Assert.Equal(binding.Id, scaled.Id);
        Assert.Equal("Fire", scaled.Name);
        Assert.Equal(200, scaled.X);
        Assert.Equal(100, scaled.Y);
        Assert.Equal(160, scaled.Width);
        Assert.Equal(80, scaled.Height);
    }
}
