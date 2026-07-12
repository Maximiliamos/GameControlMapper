namespace GameControlMapper.Models;

/// <summary>
/// User-level application settings.
/// </summary>
public sealed class AppSettings
{
    public bool StartWithWindows { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public string Theme { get; set; } = "Dark";
    public string Language { get; set; } = "ru-RU";
    public bool ShowFpsOverlay { get; set; } = true;
    public bool AutoSave { get; set; } = true;
    public bool Backups { get; set; } = true;
    public string ProfilesPath { get; set; } = "Profiles";
}
