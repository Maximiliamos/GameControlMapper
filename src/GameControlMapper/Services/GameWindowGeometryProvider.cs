using System.ComponentModel;
using GameControlMapper.Models;
using GameControlMapper.Win32;
using Microsoft.Extensions.Logging;

namespace GameControlMapper.Services;

public interface IGameWindowGeometryProvider
{
    WindowGeometryResult GetClientRect(nint windowHandle);
}

public readonly record struct NativeClientRect(int Left, int Top, int Right, int Bottom);

public interface IGameWindowNativeAdapter
{
    bool IsWindow(nint windowHandle);
    bool IsWindowVisible(nint windowHandle);
    bool IsIconic(nint windowHandle);
    bool GetClientRect(nint windowHandle, out NativeClientRect rect);
    bool ClientToScreen(nint windowHandle, ref PhysicalScreenPoint point);
    int GetLastError();
    nint GetAncestor(nint windowHandle, uint flags) => windowHandle;
    uint GetWindowProcessId(nint windowHandle) => 1;
}

/// <summary>
/// Returns client geometry in absolute physical screen pixels. Because the application is PerMonitorV2 aware,
/// Win32 client coordinates are not manually multiplied by a WPF DPI scale.
/// </summary>
public sealed class GameWindowGeometryProvider : IGameWindowGeometryProvider
{
    private readonly IGameWindowNativeAdapter _native;
    private readonly ILogger<GameWindowGeometryProvider> _logger;

    public GameWindowGeometryProvider(IGameWindowNativeAdapter native, ILogger<GameWindowGeometryProvider> logger)
    {
        _native = native;
        _logger = logger;
    }

    public WindowGeometryResult GetClientRect(nint windowHandle)
    {
        if (windowHandle == 0 || !_native.IsWindow(windowHandle))
            return Fail("IsWindow", windowHandle, _native.GetLastError(), "Window handle is invalid or the window was destroyed.");
        if (!_native.IsWindowVisible(windowHandle))
            return Fail("IsWindowVisible", windowHandle, _native.GetLastError(), "Window is not visible.");
        if (_native.IsIconic(windowHandle))
            return Fail("IsIconic", windowHandle, 0, "Window is minimized.");
        if (!_native.GetClientRect(windowHandle, out var client))
            return Fail("GetClientRect", windowHandle, _native.GetLastError(), "Unable to read the client rectangle.");

        var width = (long)client.Right - client.Left;
        var height = (long)client.Bottom - client.Top;
        if (width <= 0 || height <= 0 || width > int.MaxValue || height > int.MaxValue)
            return Fail("GetClientRect", windowHandle, 0, "Window client area size is invalid or outside the supported pixel range.");

        var origin = new PhysicalScreenPoint(client.Left, client.Top);
        if (!_native.ClientToScreen(windowHandle, ref origin))
            return Fail("ClientToScreen", windowHandle, _native.GetLastError(), "Unable to convert the client origin to screen coordinates.");

        return WindowGeometryResult.Success(new PhysicalClientRect(origin.X, origin.Y, (int)width, (int)height));
    }

    private WindowGeometryResult Fail(string operation, nint handle, int errorCode, string message)
    {
        var systemMessage = errorCode == 0 ? "No Win32 error was reported." : new Win32Exception(errorCode).Message;
        var error = $"{operation} failed for HWND 0x{handle.ToInt64():X}: {message} Win32 error {errorCode}: {systemMessage}";
        _logger.LogError("{Error}", error);
        return WindowGeometryResult.Failure(operation, errorCode, error);
    }
}

public sealed class WindowsGameWindowNativeAdapter : IGameWindowNativeAdapter
{
    public bool IsWindow(nint windowHandle) => NativeMethods.IsWindow(windowHandle);
    public bool IsWindowVisible(nint windowHandle) => NativeMethods.IsWindowVisible(windowHandle);
    public bool IsIconic(nint windowHandle) => NativeMethods.IsIconic(windowHandle);

    public bool GetClientRect(nint windowHandle, out NativeClientRect rect)
    {
        var result = NativeMethods.GetClientRect(windowHandle, out var nativeRect);
        rect = new NativeClientRect(nativeRect.Left, nativeRect.Top, nativeRect.Right, nativeRect.Bottom);
        return result;
    }

    public bool ClientToScreen(nint windowHandle, ref PhysicalScreenPoint point)
    {
        var nativePoint = new NativeMethods.POINT { X = point.X, Y = point.Y };
        var result = NativeMethods.ClientToScreen(windowHandle, ref nativePoint);
        point = new PhysicalScreenPoint(nativePoint.X, nativePoint.Y);
        return result;
    }

    public int GetLastError() => System.Runtime.InteropServices.Marshal.GetLastWin32Error();
    public nint GetAncestor(nint windowHandle, uint flags) => NativeMethods.GetAncestor(windowHandle, flags);
    public uint GetWindowProcessId(nint windowHandle)
    {
        NativeMethods.GetWindowThreadProcessId(windowHandle, out var processId);
        return processId;
    }
}
