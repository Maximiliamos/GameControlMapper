namespace GameControlMapper.Models;

/// <summary>
/// AntiMicroX-style gamepad mapping tuned for Tanks Blitz native Windows controls.
/// </summary>
public sealed class GamepadMappingSettings
{
    public bool Enabled { get; set; }
    public double LeftStickDeadZone { get; set; } = 0.28;
    public double RightStickDeadZone { get; set; } = 0.22;
    public double MouseSensitivityX { get; set; } = 18;
    public double MouseSensitivityY { get; set; } = 14;
    public string MoveForwardKey { get; set; } = "W";
    public string MoveBackKey { get; set; } = "S";
    public string MoveLeftKey { get; set; } = "A";
    public string MoveRightKey { get; set; } = "D";
    public string RepairKey { get; set; } = "Q";
    public string Consumable2Key { get; set; } = "E";
    public string Consumable3Key { get; set; } = "R";
    public string Shell1Key { get; set; } = "1";
    public string Shell2Key { get; set; } = "2";
    public string Shell3Key { get; set; } = "3";
}
