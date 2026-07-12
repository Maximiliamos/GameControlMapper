namespace GameControlMapper.Models;

/// <summary>
/// Mouse look settings for converting pointer movement into camera deltas.
/// </summary>
public sealed class CameraSettings
{
    public string ActivationHotkey { get; set; } = "LeftCtrl";
    public double AnchorX { get; set; } = 960;
    public double AnchorY { get; set; } = 540;
    public double DragRadius { get; set; } = 220;
    public bool UseMouseDrag { get; set; }
    public double SensitivityX { get; set; } = 1.0;
    public double SensitivityY { get; set; } = 1.0;
    public bool InvertX { get; set; }
    public bool InvertY { get; set; }
    public double Acceleration { get; set; }
    public double DeadZone { get; set; } = 0.2;
    public double Smooth { get; set; } = 0.35;
    public double MaxSpeed { get; set; } = 48;
}
