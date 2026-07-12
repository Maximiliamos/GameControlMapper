using GameControlMapper.Models;
using GameControlMapper.Services;
using Xunit;

namespace GameControlMapper.Tests;

public sealed class WindowCoordinateTransformerTests
{
    private readonly WindowCoordinateTransformer _transformer = new();

    [Fact]
    public void Stretch_IdentityAtOrigin_MapsCenterExactly() => AssertPoint(
        Transform(new(960, 540), new(1920, 1080), new(0, 0, 1920, 1080), CoordinateScaleMode.Stretch), 960, 540, 0, 0, 1920, 1080);

    [Fact]
    public void Stretch_Downscales1920x1080To1280x720() => AssertPoint(
        Transform(new(960, 540), new(1920, 1080), new(0, 0, 1280, 720), CoordinateScaleMode.Stretch), 640, 360, 0, 0, 1280, 720);

    [Fact]
    public void Stretch_UsesIndependentAxesFor1600x1200() => AssertPoint(
        Transform(new(960, 540), new(1920, 1080), new(0, 0, 1600, 1200), CoordinateScaleMode.Stretch), 800, 600, 0, 0, 1600, 1200);

    [Fact]
    public void Stretch_AddsNonZeroWindowOrigin() => AssertPoint(
        Transform(new(0, 0), new(1920, 1080), new(100, 200, 1920, 1080), CoordinateScaleMode.Stretch), 100, 200, 100, 200, 1920, 1080);

    [Fact]
    public void Stretch_PreservesNegativeMonitorOrigin() => AssertPoint(
        Transform(new(960, 540), new(1920, 1080), new(-1920, -100, 1920, 1080), CoordinateScaleMode.Stretch), -960, 440, -1920, -100, 1920, 1080);

    [Fact]
    public void UniformFit_SameAspectRatioUsesEntireClient() => AssertPoint(
        Transform(new(960, 540), new(1920, 1080), new(0, 0, 1280, 720), CoordinateScaleMode.UniformFit), 640, 360, 0, 0, 1280, 720);

    [Fact]
    public void UniformFit_16x9Into16x10CreatesVerticalLetterbox() => AssertPoint(
        Transform(new(0, 0), new(1920, 1080), new(0, 0, 1920, 1200), CoordinateScaleMode.UniformFit), 0, 60, 0, 60, 1920, 1080);

    [Fact]
    public void UniformFit_16x9Into4x3CreatesVerticalLetterbox() => AssertPoint(
        Transform(new(1920, 1080), new(1920, 1080), new(0, 0, 1600, 1200), CoordinateScaleMode.UniformFit), 1600, 1050, 0, 150, 1600, 900);

    [Fact]
    public void UniformFit_4x3IntoWideClientCreatesHorizontalPillarbox() => AssertPoint(
        Transform(new(0, 0), new(400, 300), new(0, 0, 1920, 1080), CoordinateScaleMode.UniformFit), 240, 0, 240, 0, 1440, 1080);

    [Fact]
    public void UniformFit_MapsProfileCenterToClientCenter() => AssertPoint(
        Transform(new(200, 150), new(400, 300), new(100, 50, 1920, 1080), CoordinateScaleMode.UniformFit), 1060, 590, 340, 50, 1440, 1080);

    [Theory]
    [InlineData(0, 0, 240, 0)]
    [InlineData(400, 0, 1680, 0)]
    [InlineData(0, 300, 240, 1080)]
    [InlineData(400, 300, 1680, 1080)]
    public void UniformFit_MapsFourContentViewportCorners(double x, double y, int expectedX, int expectedY)
    {
        var result = Transform(new(x, y), new(400, 300), new(0, 0, 1920, 1080), CoordinateScaleMode.UniformFit);
        Assert.Equal(new PhysicalScreenPoint(expectedX, expectedY), result.Point);
    }

    [Fact]
    public void FractionalScale_RoundsPositivePixelAwayFromZero()
    {
        var result = Transform(new(.5, .5), new(3, 3), new(0, 0, 10, 10), CoordinateScaleMode.Stretch);
        Assert.Equal(new PhysicalScreenPoint(2, 2), result.Point);
    }

    [Fact]
    public void FractionalScale_RoundsNegativePixelAwayFromZero()
    {
        var result = Transform(new(1, 1), new(2, 2), new(-5, -5, 3, 3), CoordinateScaleMode.Stretch);
        Assert.Equal(new PhysicalScreenPoint(-4, -4), result.Point);
    }

    [Fact]
    public void Validation_RejectsZeroProfileWidth() => AssertFailure(new(0, 1080), new(0, 0, 100, 100));

    [Fact]
    public void Validation_RejectsNegativeProfileHeight() => AssertFailure(new(1920, -1), new(0, 0, 100, 100));

    [Fact]
    public void Validation_RejectsZeroClientArea()
    {
        var result = _transformer.TryTransform(new(0, 0), new(1920, 1080), new(0, 0, 0, 100), CoordinateScaleMode.Stretch);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Validation_RejectsNaNPoint()
    {
        var result = _transformer.TryTransform(new(double.NaN, 0), new(1920, 1080), new(0, 0, 100, 100), CoordinateScaleMode.Stretch);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void Validation_RejectsInfiniteProfileSize()
    {
        var result = _transformer.TryTransform(new(0, 0), new(double.PositiveInfinity, 1080), new(0, 0, 100, 100), CoordinateScaleMode.Stretch);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public void OutsideProfile_TransformsWithoutClampAndSetsFlag()
    {
        var result = Transform(new(2100, -100), new(1920, 1080), new(10, 20, 1920, 1080), CoordinateScaleMode.Stretch);
        Assert.True(result.IsOutsideProfile);
        Assert.Equal(new PhysicalScreenPoint(2110, -80), result.Point);
    }

    [Fact]
    public void BoundaryPoint_IsNotOutsideProfile()
    {
        var result = Transform(new(1920, 1080), new(1920, 1080), new(0, 0, 1920, 1080), CoordinateScaleMode.Stretch);
        Assert.False(result.IsOutsideProfile);
    }

    [Fact]
    public void Validation_RejectsUnknownScaleMode()
    {
        var result = _transformer.TryTransform(new(0, 0), new(1920, 1080), new(0, 0, 100, 100), (CoordinateScaleMode)99);
        Assert.False(result.Succeeded);
    }

    private CoordinateTransformResult Transform(ProfilePoint point, ProfileSize size, PhysicalClientRect rect, CoordinateScaleMode mode)
    {
        var result = _transformer.TryTransform(point, size, rect, mode);
        Assert.True(result.Succeeded, result.Error);
        return result;
    }

    private void AssertFailure(ProfileSize size, PhysicalClientRect rect)
    {
        var result = _transformer.TryTransform(new(0, 0), size, rect, CoordinateScaleMode.Stretch);
        Assert.False(result.Succeeded);
        Assert.NotNull(result.Error);
    }

    private static void AssertPoint(CoordinateTransformResult result, int x, int y, double left, double top, double width, double height)
    {
        Assert.Equal(new PhysicalScreenPoint(x, y), result.Point);
        Assert.Equal(new ContentViewport(left, top, width, height), result.Viewport);
    }
}
