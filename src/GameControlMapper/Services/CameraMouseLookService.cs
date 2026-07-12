using System;
using GameControlMapper.Models;
using GameControlMapper.Win32;
using Microsoft.Extensions.Logging;

namespace GameControlMapper.Services;

public sealed class CameraMouseLookService : IDisposable
{
    private readonly TouchEngine _touchEngine;
    private readonly ILogger<CameraMouseLookService> _logger;
    private CameraSettings _settings = new();
    private bool _active;
    private double _x;
    private double _y;
    private double _anchorX;
    private double _anchorY;
    private bool _disposed;

    public CameraMouseLookService(TouchEngine touchEngine, ILogger<CameraMouseLookService> logger)
    {
        _touchEngine = touchEngine;
        _logger = logger;
    }

    public bool IsActive => _active;

    public void Start(CameraSettings settings, double anchorX, double anchorY)
    {
        if (_disposed || _active)
            return;

        _settings = settings;
        _x = anchorX;
        _y = anchorY;
        _anchorX = anchorX;
        _anchorY = anchorY;

        _touchEngine.StartTouch((int)FixedContacts.Camera, _x, _y);

        _active = true;
        _logger.LogInformation("CameraMouseLookService started at ({X}, {Y})", _x, _y);
    }

    public void Stop()
    {
        if (!_active || _disposed)
            return;

        _touchEngine.EndTouch((int)FixedContacts.Camera);

        _active = false;
        _logger.LogInformation("CameraMouseLookService stopped");
    }

    public void OnMouseMove(int dx, int dy)
    {
        if (!_active || _disposed)
            return;

        double dxScaled = dx * _settings.SensitivityX;
        double dyScaled = dy * _settings.SensitivityY;

        dxScaled = Math.Clamp(dxScaled, -50, 50);
        dyScaled = Math.Clamp(dyScaled, -50, 50);

        if (_settings.InvertX) dxScaled *= -1;
        if (_settings.InvertY) dyScaled *= -1;

        _x += dxScaled;
        _y += dyScaled;

        int screenWidth = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSCREEN);
        int screenHeight = NativeMethods.GetSystemMetrics(NativeMethods.SM_CYSCREEN);

        _x = Math.Clamp(_x, 0, screenWidth - 1);
        _y = Math.Clamp(_y, 0, screenHeight - 1);

        double deadzone = _settings.DeadZone;
        if (Math.Abs(dx) < deadzone && Math.Abs(dy) < deadzone)
            return;

        _touchEngine.MoveTouch((int)FixedContacts.Camera, _x, _y);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }
}
