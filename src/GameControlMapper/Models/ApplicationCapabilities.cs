namespace GameControlMapper.Models;

public enum CapabilityStatus { Supported, AutomatedOnly, Unsupported, Unavailable, Experimental }
public sealed record ApplicationCapability(string Id,string DisplayName,CapabilityStatus Status,string Limitation);

public sealed class ApplicationCapabilities
{
    public static ApplicationCapabilities Beta { get; } = new(
    [
        new("windows-touch","Windows Touch Injection",CapabilityStatus.AutomatedOnly,"Windows 10/11 x64; manual report is pending"),
        new("keyboard-mouse","Клавиатура и мышь",CapabilityStatus.AutomatedOnly,"Привязки к сенсорным действиям"),
        new("target-window","Координаты целевого окна",CapabilityStatus.AutomatedOnly,"Клиентская область выбранного окна"),
        new("multitouch","Мультитач",CapabilityStatus.AutomatedOnly,"До лимита Windows backend"),
        new("camera","Непрерывная камера",CapabilityStatus.AutomatedOnly,"Mouse-look с handoff внутри safe client-area ellipse"),
        new("mixed-dpi","Mixed DPI",CapabilityStatus.AutomatedOnly,"Есть автоматические тесты; ручная матрица не завершена"),
        new("multi-monitor","Несколько мониторов",CapabilityStatus.AutomatedOnly,"Есть автоматические тесты; ручная матрица не завершена"),
        new("negative-origin","Отрицательный origin",CapabilityStatus.AutomatedOnly,"Есть автоматические тесты; ручная матрица не завершена"),
        new("diagnostics","Диагностика",CapabilityStatus.AutomatedOnly,"Privacy-фильтр проверяется автоматически"),
        new("profile-backup","Резервные копии профилей",CapabilityStatus.AutomatedOnly,"Локальное атомарное хранение"),
        new("tanks-blitz","Tanks Blitz",CapabilityStatus.Experimental,"Профиль не является гарантией совместимости с версией игры"),
        new("xvm","Blitz XVM / Olenemer",CapabilityStatus.Experimental,"Удалено из публичного beta UI"),
        new("xinput","XInput",CapabilityStatus.Unsupported,"Runtime отсутствует"),
        new("macro-sequence","Macro/Sequence",CapabilityStatus.Unsupported,"Runtime отсутствует"),
        new("raw-input-public","Raw Input public contract",CapabilityStatus.Unavailable,"Внутренняя реализация не является публичным API"),
        new("interception","Interception",CapabilityStatus.Unavailable,"Не реализовано"),
        new("vigem","ViGEm",CapabilityStatus.Unavailable,"Не реализовано"),
        new("adb","ADB/Android",CapabilityStatus.Unavailable,"Не реализовано"),
        new("pinch","Pinch",CapabilityStatus.Unsupported,"Не реализовано"),
        new("rotation","Rotation",CapabilityStatus.Unsupported,"Не реализовано")
    ]);

    public ApplicationCapabilities(IReadOnlyList<ApplicationCapability> items)=>Items=items;
    public IReadOnlyList<ApplicationCapability> Items{get;}
    public bool IsSupported(string id)=>Items.Any(item=>item.Id==id&&item.Status==CapabilityStatus.Supported);
    public bool IsRuntimeAvailable(string id)=>Items.Any(item=>item.Id==id&&item.Status is CapabilityStatus.Supported or CapabilityStatus.AutomatedOnly);
}
