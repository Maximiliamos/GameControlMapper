namespace GameControlMapper.Models;

/// <summary>
/// Window/process data used to bind a profile to a game.
/// </summary>
public sealed class GameWindowBinding
{
    public string ProcessName { get; set; } = string.Empty;
    public string WindowTitle { get; set; } = string.Empty;
    public long WindowHandle { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public double ScaleX { get; set; } = 1;
    public double ScaleY { get; set; } = 1;
}
