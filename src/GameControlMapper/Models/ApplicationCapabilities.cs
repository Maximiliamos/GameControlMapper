namespace GameControlMapper.Models;

public enum CapabilityStatus { Supported, UnsupportedInBeta }
public sealed record ApplicationCapability(string Id, string DisplayName, CapabilityStatus Status, string Limitation);
public sealed class ApplicationCapabilities
{
    public static ApplicationCapabilities Beta { get; } = new();
    private ApplicationCapabilities()
    {
        Items =
        [
            new("windows-touch", "Windows Touch Injection", CapabilityStatus.Supported, "Windows 10/11 x64"),
            new("keyboard-mouse", "Клавиатура и мышь", CapabilityStatus.Supported, "Привязки к сенсорным действиям"),
            new("target-window", "Координаты целевого окна", CapabilityStatus.Supported, "Требуется выбранное окно"),
            new("multitouch", "Мультитач", CapabilityStatus.Supported, "До лимита backend"),
            new("camera", "Непрерывная камера", CapabilityStatus.Supported, "Mouse-look"),
            new("diagnostics", "Диагностика", CapabilityStatus.Supported, "Без нажатых клавиш и содержимого профилей"),
            new("profile-backup", "Резервные копии профилей", CapabilityStatus.Supported, "Локальное хранение"),
            new("xinput", "XInput", CapabilityStatus.UnsupportedInBeta, "Отключено в beta"),
            new("macro-sequence", "Macro/Sequence", CapabilityStatus.UnsupportedInBeta, "Runtime отключён в beta"),
            new("raw-input", "RawInput", CapabilityStatus.UnsupportedInBeta, "Не реализовано"),
            new("interception", "Interception", CapabilityStatus.UnsupportedInBeta, "Не реализовано"),
            new("vigem", "ViGEm", CapabilityStatus.UnsupportedInBeta, "Не реализовано"),
            new("adb", "ADB/Android", CapabilityStatus.UnsupportedInBeta, "Не реализовано"),
            new("pinch", "Pinch", CapabilityStatus.UnsupportedInBeta, "Не реализовано"),
            new("rotation", "Rotation", CapabilityStatus.UnsupportedInBeta, "Не реализовано")
        ];
    }
    public IReadOnlyList<ApplicationCapability> Items { get; }
    public bool IsSupported(string id) => Items.Any(x => x.Id == id && x.Status == CapabilityStatus.Supported);
}
