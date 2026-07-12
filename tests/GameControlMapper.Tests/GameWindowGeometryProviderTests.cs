using GameControlMapper.Models;
using GameControlMapper.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameControlMapper.Tests;

public sealed class GameWindowGeometryProviderTests
{
    [Fact]
    public void GetClientRect_ConvertsClientOriginToAbsoluteScreenPixels()
    {
        var native = ValidNative();
        native.ScreenOrigin = new(120, 240);

        var result = Provider(native).GetClientRect(0x1234);

        Assert.True(result.Succeeded);
        Assert.Equal(new PhysicalClientRect(120, 240, 1280, 720), result.ClientRect);
    }

    [Fact]
    public void GetClientRect_PreservesNegativeScreenOrigin()
    {
        var native = ValidNative();
        native.ScreenOrigin = new(-1600, -200);

        var result = Provider(native).GetClientRect(0x1234);

        Assert.True(result.Succeeded);
        Assert.Equal(-1600, result.ClientRect.Left);
        Assert.Equal(-200, result.ClientRect.Top);
    }

    [Fact]
    public void GetClientRect_ReturnsControlledErrorWhenGetClientRectFails()
    {
        var native = ValidNative();
        native.GetClientRectResult = false;
        native.LastError = 1400;

        var result = Provider(native).GetClientRect(0x1234);

        Assert.False(result.Succeeded);
        Assert.Equal("GetClientRect", result.Operation);
        Assert.Equal(1400, result.Win32ErrorCode);
    }

    [Fact]
    public void GetClientRect_ReturnsControlledErrorWhenClientToScreenFails()
    {
        var native = ValidNative();
        native.ClientToScreenResult = false;
        native.LastError = 5;

        var result = Provider(native).GetClientRect(0x1234);

        Assert.False(result.Succeeded);
        Assert.Equal("ClientToScreen", result.Operation);
        Assert.Equal(5, result.Win32ErrorCode);
    }

    [Fact]
    public void GetClientRect_RejectsZeroClientSize()
    {
        var native = ValidNative();
        native.ClientRect = new(0, 0, 0, 720);

        var result = Provider(native).GetClientRect(0x1234);

        Assert.False(result.Succeeded);
        Assert.Equal("GetClientRect", result.Operation);
    }

    [Fact]
    public void GetClientRect_RejectsDestroyedOrInvalidWindow()
    {
        var native = ValidNative();
        native.IsWindowResult = false;
        native.LastError = 1400;

        var result = Provider(native).GetClientRect(0x1234);

        Assert.False(result.Succeeded);
        Assert.Equal("IsWindow", result.Operation);
    }

    [Fact]
    public void GetClientRect_RejectsHiddenWindow()
    {
        var native = ValidNative();
        native.IsVisibleResult = false;

        var result = Provider(native).GetClientRect(0x1234);

        Assert.False(result.Succeeded);
        Assert.Equal("IsWindowVisible", result.Operation);
    }

    private static GameWindowGeometryProvider Provider(FakeNative native) =>
        new(native, NullLogger<GameWindowGeometryProvider>.Instance);

    private static FakeNative ValidNative() => new()
    {
        IsWindowResult = true,
        IsVisibleResult = true,
        GetClientRectResult = true,
        ClientToScreenResult = true,
        ClientRect = new NativeClientRect(0, 0, 1280, 720),
        ScreenOrigin = new PhysicalScreenPoint(0, 0)
    };

    private sealed class FakeNative : IGameWindowNativeAdapter
    {
        public bool IsWindowResult { get; set; }
        public bool IsVisibleResult { get; set; }
        public bool GetClientRectResult { get; set; }
        public bool ClientToScreenResult { get; set; }
        public NativeClientRect ClientRect { get; set; }
        public PhysicalScreenPoint ScreenOrigin { get; set; }
        public int LastError { get; set; }
        public bool IsWindow(nint windowHandle) => IsWindowResult;
        public bool IsWindowVisible(nint windowHandle) => IsVisibleResult;
        public bool GetClientRect(nint windowHandle, out NativeClientRect rect) { rect = ClientRect; return GetClientRectResult; }
        public bool ClientToScreen(nint windowHandle, ref PhysicalScreenPoint point) { point = ScreenOrigin; return ClientToScreenResult; }
        public int GetLastError() => LastError;
    }
}
