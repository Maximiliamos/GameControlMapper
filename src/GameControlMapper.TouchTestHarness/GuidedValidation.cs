using System.Runtime.InteropServices;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameControlMapper.TouchTestHarness;

public enum ValidationStatus { NotStarted, InProgress, Passed, Failed, NotAvailable }
public enum ValidationVerdict { Passed, Failed, PassedWithUnverifiedEnvironments }
public enum EnvironmentRequirement { None, HighDpi, MultipleMonitors, NegativeOrigin, MixedDpi }

public sealed class ScenarioEvidence
{
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset CompletedAt { get; set; }
    public int DownBefore { get; set; }
    public int MoveBefore { get; set; }
    public int UpBefore { get; set; }
    public int DownAfter { get; set; }
    public int MoveAfter { get; set; }
    public int UpAfter { get; set; }
    public int EventCount { get; set; }
    public int MaximumConcurrentContacts { get; set; }
    public ValidationStatus AutomaticVerdict { get; set; }
    public string AutomaticReason { get; set; } = "";
}

public sealed class ValidationScenario
{
    public ValidationScenario(int id, string name, EnvironmentRequirement requirement = EnvironmentRequirement.None)
    {
        Id = id;
        Name = name;
        Requirement = requirement;
    }

    public ValidationScenario(int id, string name, bool environmentOnly)
        : this(id, name, environmentOnly ? EnvironmentRequirement.MultipleMonitors : EnvironmentRequirement.None) { }

    public int Id { get; init; }
    public string Name { get; init; }
    [JsonIgnore] public EnvironmentRequirement Requirement { get; init; }
    public bool EnvironmentOnly => Requirement != EnvironmentRequirement.None;
    public ValidationStatus Status { get; set; }
    public ValidationStatus UserVerdict { get; set; }
    public ValidationStatus FinalVerdict { get; set; }
    public ScenarioEvidence? Evidence { get; set; }
    public string Comment { get; set; } = "";
}

public sealed record MonitorMetadata(
    string Id,
    int X,
    int Y,
    int Width,
    int Height,
    double DpiX,
    double DpiY,
    bool Primary,
    int ScalePercent);

public sealed record MonitorEnvironmentMetadata(
    IReadOnlyList<MonitorMetadata> Monitors,
    bool HasNegativeOrigin,
    bool HasMixedDpi,
    string? PrimaryMonitorId,
    string? HarnessMonitorId,
    string? TargetMonitorId);

public interface IMonitorInformationProvider
{
    MonitorEnvironmentMetadata Capture(nint harnessWindow, nint targetWindow = 0);
}

public sealed class WindowsMonitorInformationProvider : IMonitorInformationProvider
{
    private readonly IMonitorPlatform _platform;

    public WindowsMonitorInformationProvider() : this(new WindowsMonitorPlatform()) { }
    public WindowsMonitorInformationProvider(IMonitorPlatform platform) => _platform = platform;

    public MonitorEnvironmentMetadata Capture(nint harnessWindow, nint targetWindow = 0)
    {
        var raw = _platform.Enumerate()
            .OrderByDescending(monitor => monitor.Primary)
            .ThenBy(monitor => monitor.X)
            .ThenBy(monitor => monitor.Y)
            .ToArray();
        var monitors = raw.Select((monitor, index) => new MonitorMetadata(
            $"Monitor-{index + 1}",
            monitor.X,
            monitor.Y,
            monitor.Width,
            monitor.Height,
            monitor.DpiX,
            monitor.DpiY,
            monitor.Primary,
            (int)Math.Round(monitor.DpiX / 96d * 100d))).ToArray();

        string? IdForWindow(nint window)
        {
            if (window == 0) return null;
            var handle = _platform.MonitorFromWindow(window);
            var index = Array.FindIndex(raw, monitor => monitor.Handle == handle);
            return index >= 0 ? monitors[index].Id : null;
        }

        return new MonitorEnvironmentMetadata(
            monitors,
            monitors.Any(monitor => monitor.X < 0 || monitor.Y < 0),
            monitors.Select(monitor => (Math.Round(monitor.DpiX), Math.Round(monitor.DpiY))).Distinct().Count() > 1,
            monitors.FirstOrDefault(monitor => monitor.Primary)?.Id,
            IdForWindow(harnessWindow),
            IdForWindow(targetWindow));
    }
}

public sealed record PlatformMonitor(nint Handle, int X, int Y, int Width, int Height, double DpiX, double DpiY, bool Primary);
public interface IMonitorPlatform
{
    IReadOnlyList<PlatformMonitor> Enumerate();
    nint MonitorFromWindow(nint window);
}

internal sealed class WindowsMonitorPlatform : IMonitorPlatform
{
    public IReadOnlyList<PlatformMonitor> Enumerate()
    {
        var result = new List<PlatformMonitor>();
        EnumDisplayMonitors(0, 0, (nint monitor, nint hdc, ref NativeRect rect, nint data) =>
        {
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            var primary = GetMonitorInfo(monitor, ref info) && (info.Flags & 1) != 0;
            var dpiX = 96u;
            var dpiY = 96u;
            try { _ = GetDpiForMonitor(monitor, 0, out dpiX, out dpiY); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            result.Add(new PlatformMonitor(
                monitor, rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top,
                dpiX, dpiY, primary));
            return true;
        }, 0);
        return result;
    }

    public nint MonitorFromWindow(nint window) => NativeMonitorFromWindow(window, 2);

    private delegate bool MonitorEnumProc(nint monitor, nint hdc, ref NativeRect rect, nint data);
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(nint hdc, nint clip, MonitorEnumProc callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("Shcore.dll")] private static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);
    [DllImport("user32.dll", EntryPoint = "MonitorFromWindow")] private static extern nint NativeMonitorFromWindow(nint window, uint flags);
}

public sealed class ManualValidationReport
{
    public string SchemaVersion { get; set; } = "1.0";
    public string ProductVersion { get; set; } = "";
    public string CommitHash { get; set; } = "";
    public string ApplicationCommitHash { get; set; } = "";
    public string HarnessCommitHash { get; set; } = "";
    public string ApplicationArchiveSha256 { get; set; } = "";
    public string HarnessArchiveSha256 { get; set; } = "";
    public string WindowsVersion { get; set; } = "";
    public string DotNetVersion { get; set; } = "";
    public List<MonitorMetadata> Monitors { get; set; } = [];
    public int MonitorCount { get; set; }
    public bool HasNegativeOrigin { get; set; }
    public bool HasMixedDpi { get; set; }
    public string? PrimaryMonitorId { get; set; }
    public string? HarnessMonitorId { get; set; }
    public string? TargetMonitorId { get; set; }
    public DateTime BuildDateUtc { get; set; } = DateTime.UtcNow;
    public List<ValidationScenario> Scenarios { get; set; } = [];
    public List<string> ProtocolErrors { get; set; } = [];
    public int DownCount { get; set; }
    public int MoveCount { get; set; }
    public int UpCount { get; set; }
    public int MaximumContacts { get; set; }
    public int ActiveContactsAtEnd { get; set; }
    public ValidationVerdict Verdict { get; set; }
}

public sealed class GuidedValidationSession
{
    private static readonly (int Id, string Name, EnvironmentRequirement Requirement)[] Definitions =
    [
        (1,"Tap",EnvironmentRequirement.None),(2,"Hold",EnvironmentRequirement.None),(3,"DoubleTap",EnvironmentRequirement.None),(4,"Swipe",EnvironmentRequirement.None),
        (5,"F9 во время удержания",EnvironmentRequirement.None),(6,"Закрытие mapper во время удержания",EnvironmentRequirement.None),(7,"Повторный Start после Stop",EnvironmentRequirement.None),
        (8,"WASD joystick",EnvironmentRequirement.None),(9,"Camera mouse-look не менее двух минут",EnvironmentRequirement.None),(10,"Движение камеры до физического края экрана",EnvironmentRequirement.None),
        (11,"MouseArea ЛКМ",EnvironmentRequirement.None),(12,"MouseArea ПКМ",EnvironmentRequirement.None),(13,"Camera + joystick",EnvironmentRequirement.None),(14,"Camera + MouseArea",EnvironmentRequirement.None),
        (15,"Несколько обычных контактов",EnvironmentRequirement.None),(16,"Максимум 10 контактов",EnvironmentRequirement.None),(17,"Одиннадцатое действие",EnvironmentRequirement.None),(18,"Повторный capacity cycle",EnvironmentRequirement.None),
        (19,"Alt+Tab во время WASD",EnvironmentRequirement.None),(20,"Alt+Tab во время камеры",EnvironmentRequirement.None),(21,"Alt+Tab во время MouseArea",EnvironmentRequirement.None),(22,"F9 одновременно с Alt+Tab",EnvironmentRequirement.None),
        (23,"Перемещение target",EnvironmentRequirement.None),(24,"Изменение размера target",EnvironmentRequirement.None),(25,"Сворачивание target",EnvironmentRequirement.None),(26,"Закрытие target",EnvironmentRequirement.None),(27,"Возврат фокуса без restart",EnvironmentRequirement.None),
        (28,"Создание профиля",EnvironmentRequirement.None),(29,"Сохранение профиля",EnvironmentRequirement.None),(30,"Создание backup",EnvironmentRequirement.None),(31,"Повреждённый JSON",EnvironmentRequirement.None),(32,"Восстановление backup",EnvironmentRequirement.None),
        (33,"Ошибка записи без потери primary",EnvironmentRequirement.None),(34,"Импорт профиля",EnvironmentRequirement.None),(35,"Отклонение профиля",EnvironmentRequirement.None),(36,"Файловый лог",EnvironmentRequirement.None),
        (37,"Mapping session ID",EnvironmentRequirement.None),(38,"Причина автоматического Stop",EnvironmentRequirement.None),(39,"Диагностический ZIP",EnvironmentRequirement.None),(40,"Нет содержимого профилей в ZIP",EnvironmentRequirement.None),
        (41,"Нет истории клавиш и персональных данных",EnvironmentRequirement.None),(42,"Нет log spam",EnvironmentRequirement.None),(43,"Основной монитор DPI 100%",EnvironmentRequirement.None),
        (44,"DPI 125% или выше",EnvironmentRequirement.HighDpi),(45,"Второй монитор",EnvironmentRequirement.MultipleMonitors),(46,"Монитор слева",EnvironmentRequirement.NegativeOrigin),(47,"Mixed DPI",EnvironmentRequirement.MixedDpi)
    ];
    public static readonly string[] RequiredNames = Definitions.Select(definition => definition.Name).ToArray();

    private MonitorEnvironmentMetadata _environment;
    private readonly Dictionary<int, ScenarioStart> _scenarioStarts = [];

    public GuidedValidationSession(MonitorEnvironmentMetadata? environment = null)
    {
        _environment = environment ?? new WindowsMonitorInformationProvider().Capture(0);
        Scenarios = Definitions.Select(definition => new ValidationScenario(definition.Id, definition.Name, definition.Requirement)).ToList();
    }

    public List<ValidationScenario> Scenarios { get; }
    public List<string> ProtocolErrors { get; } = [];
    public void SetEnvironment(MonitorEnvironmentMetadata environment) => _environment = environment;
    public static bool RequiresMachineEvidence(int id) => id is 1 or 2 or 3 or 4 or 9 or 15 or 16;

    public void BeginScenario(int id, TouchLifecycleTracker tracker)
    {
        _scenarioStarts[id] = new ScenarioStart(DateTimeOffset.Now, tracker.DownCount, tracker.MoveCount, tracker.UpCount);
    }

    public bool CanPass(int id, TouchLifecycleTracker tracker, out string reason)
    {
        if (!RequiresMachineEvidence(id)) { reason = "Manual evidence"; return true; }
        var evidence = BuildEvidence(id, tracker);
        reason = evidence.AutomaticReason;
        return evidence.AutomaticVerdict == ValidationStatus.Passed;
    }

    public bool SetStatus(int id, ValidationStatus status, string? comment, TouchLifecycleTracker tracker, out string? error)
    {
        var scenario = Scenarios.Single(item => item.Id == id);
        var evidence = BuildEvidence(id, tracker);
        scenario.Evidence = evidence;
        scenario.UserVerdict = status;
        if (status == ValidationStatus.Passed && RequiresMachineEvidence(id) && evidence.AutomaticVerdict != ValidationStatus.Passed)
        {
            error = $"Недостаточно автоматического evidence: {evidence.AutomaticReason}";
            scenario.FinalVerdict = ValidationStatus.Failed;
            return false;
        }
        var accepted = SetStatus(id, status, comment, out error);
        scenario.FinalVerdict = accepted ? status : ValidationStatus.Failed;
        return accepted;
    }

    public bool SetStatus(int id, ValidationStatus status, string? comment, out string? error)
    {
        error = null;
        var scenario = Scenarios.Single(item => item.Id == id);
        if (status == ValidationStatus.NotAvailable && !IsPhysicallyUnavailable(scenario.Requirement))
        {
            error = "NOT AVAILABLE допустим только когда требуемое оборудование физически отсутствует.";
            return false;
        }
        if (status == ValidationStatus.Failed && string.IsNullOrWhiteSpace(comment))
        {
            error = "Для FAIL требуется комментарий.";
            return false;
        }
        scenario.Status = status;
        scenario.UserVerdict = status;
        scenario.FinalVerdict = status;
        scenario.Comment = comment?.Trim() ?? "";
        return true;
    }

    private ScenarioEvidence BuildEvidence(int id, TouchLifecycleTracker tracker)
    {
        if (!_scenarioStarts.TryGetValue(id, out var start))
            start = new ScenarioStart(DateTimeOffset.Now, tracker.DownCount, tracker.MoveCount, tracker.UpCount);
        var events = tracker.Events.Where(item => item.Timestamp >= start.Timestamp).ToArray();
        var protocolClean = events.All(item => item.ProtocolError is null);
        var active = new HashSet<int>();
        var maximum = 0;
        foreach (var item in events)
        {
            if (item.State == HarnessTouchState.Down) active.Add(item.Id);
            else if (item.State == HarnessTouchState.Up) active.Remove(item.Id);
            maximum = Math.Max(maximum, active.Count);
        }

        bool CompleteLifecycle(int contactId) =>
            events.Any(item => item.Id == contactId && item.State == HarnessTouchState.Down) &&
            events.Any(item => item.Id == contactId && item.State == HarnessTouchState.Up);
        var ids = events.Select(item => item.Id).Distinct().ToArray();
        var completed = ids.Count(CompleteLifecycle);
        var down = events.FirstOrDefault(item => item.State == HarnessTouchState.Down);
        var up = down is null ? null : events.LastOrDefault(item => item.Id == down.Id && item.State == HarnessTouchState.Up);
        var duration = down is null || up is null ? TimeSpan.Zero : up.Timestamp - down.Timestamp;
        var hasMove = down is not null && events.Any(item => item.Id == down.Id && item.State == HarnessTouchState.Move);
        var passed = protocolClean && tracker.ActiveContacts.Count == 0 && id switch
        {
            1 => completed >= 1,
            2 => completed >= 1 && (hasMove || duration >= TimeSpan.FromMilliseconds(500)),
            3 => completed >= 2,
            4 => completed >= 1 && hasMove,
            9 => completed >= 1 && hasMove && duration >= TimeSpan.FromMinutes(2),
            15 => maximum >= 2,
            16 => maximum >= 10,
            _ => true
        };
        var reason = passed ? "Required lifecycle evidence is present" : id switch
        {
            1 => "Требуются Down и Up одного контакта",
            2 => "Требуются Down, удержание/Update и Up",
            3 => "Требуются два завершённых lifecycle",
            4 => "Требуются Down, Move и Up",
            9 => "Требуются Down, Move, Up и длительность не менее двух минут",
            15 => "Требуются минимум два одновременных контакта",
            16 => "Требуются десять одновременных контактов",
            _ => "Manual evidence"
        };
        return new ScenarioEvidence
        {
            StartedAt = start.Timestamp, CompletedAt = DateTimeOffset.Now,
            DownBefore = start.Down, MoveBefore = start.Move, UpBefore = start.Up,
            DownAfter = tracker.DownCount, MoveAfter = tracker.MoveCount, UpAfter = tracker.UpCount,
            EventCount = events.Length, MaximumConcurrentContacts = maximum,
            AutomaticVerdict = passed ? ValidationStatus.Passed : ValidationStatus.Failed,
            AutomaticReason = reason
        };
    }

    private bool IsPhysicallyUnavailable(EnvironmentRequirement requirement) => requirement switch
    {
        EnvironmentRequirement.HighDpi => !_environment.Monitors.Any(monitor => monitor.ScalePercent >= 125),
        EnvironmentRequirement.MultipleMonitors => _environment.Monitors.Count < 2,
        EnvironmentRequirement.NegativeOrigin => !_environment.HasNegativeOrigin,
        EnvironmentRequirement.MixedDpi => !_environment.HasMixedDpi,
        _ => false
    };

    public ValidationVerdict Evaluate(int activeContacts)
    {
        if (ProtocolErrors.Count > 0 || activeContacts > 0 ||
            Scenarios.Any(scenario => scenario.Status is ValidationStatus.Failed or ValidationStatus.NotStarted or ValidationStatus.InProgress) ||
            Scenarios.Any(scenario => scenario.Status == ValidationStatus.NotAvailable && !IsPhysicallyUnavailable(scenario.Requirement)))
            return ValidationVerdict.Failed;
        return Scenarios.Any(scenario => scenario.Status == ValidationStatus.NotAvailable)
            ? ValidationVerdict.PassedWithUnverifiedEnvironments
            : ValidationVerdict.Passed;
    }

    public ManualValidationReport CreateReport(
        TouchLifecycleTracker tracker, string version, string commit, string appCommit,
        string appZip, string harnessZip, nint harnessWindow = 0, nint targetWindow = 0,
        IMonitorInformationProvider? monitorProvider = null)
    {
        var environment = (monitorProvider ?? new WindowsMonitorInformationProvider()).Capture(harnessWindow, targetWindow);
        SetEnvironment(environment);
        return new ManualValidationReport
        {
            ProductVersion = version, CommitHash = commit, ApplicationCommitHash = appCommit, HarnessCommitHash = commit,
            ApplicationArchiveSha256 = Hash(appZip), HarnessArchiveSha256 = Hash(harnessZip),
            WindowsVersion = Environment.OSVersion.VersionString, DotNetVersion = RuntimeInformation.FrameworkDescription,
            Monitors = environment.Monitors.ToList(), MonitorCount = environment.Monitors.Count,
            HasNegativeOrigin = environment.HasNegativeOrigin, HasMixedDpi = environment.HasMixedDpi,
            PrimaryMonitorId = environment.PrimaryMonitorId, HarnessMonitorId = environment.HarnessMonitorId, TargetMonitorId = environment.TargetMonitorId,
            Scenarios = Scenarios, ProtocolErrors = ProtocolErrors.Concat(tracker.Events.Where(item => item.ProtocolError is not null).Select(item => item.ProtocolError!)).ToList(),
            DownCount = tracker.DownCount, MoveCount = tracker.MoveCount, UpCount = tracker.UpCount,
            MaximumContacts = tracker.MaximumConcurrentContacts, ActiveContactsAtEnd = tracker.ActiveContacts.Count,
            Verdict = Evaluate(tracker.ActiveContacts.Count)
        };
    }

    public static void Export(ManualValidationReport report, string jsonPath)
    {
        var options = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, options));
        var textPath = Path.ChangeExtension(jsonPath, ".txt");
        var text = new StringBuilder().AppendLine($"Game Control Mapper {report.ProductVersion}")
            .AppendLine($"Commit: {report.CommitHash}").AppendLine($"Verdict: {report.Verdict}")
            .AppendLine($"Monitors: {report.MonitorCount}; negative origin: {report.HasNegativeOrigin}; mixed DPI: {report.HasMixedDpi}");
        foreach (var scenario in report.Scenarios) text.AppendLine($"{scenario.Id}. {scenario.Name}: {scenario.Status} {scenario.Comment}");
        File.WriteAllText(textPath, text.ToString());
    }

    private static string Hash(string path) => File.Exists(path)
        ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant()
        : "not supplied";

    private sealed record ScenarioStart(DateTimeOffset Timestamp, int Down, int Move, int Up);
}
