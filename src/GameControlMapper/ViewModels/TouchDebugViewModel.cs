using System.Collections.ObjectModel;
using GameControlMapper.Services;

namespace GameControlMapper.ViewModels;

public sealed record TouchOwnerDisplay(
    int ContactId,
    string OwnerId,
    string OwnerType,
    string? BindingName,
    TouchLeaseState State);

public sealed class TouchDebugViewModel : ObservableObject, IDisposable
{
    private readonly ContactManager _contacts;
    private readonly TouchScheduler _scheduler;
    private readonly ITouchContactAllocator _allocator;
    private readonly IUiDispatcher _dispatcher;
    private DebugSnapshot? _latestSnapshot;
    private double _injectedFps;
    private int _updateScheduled;
    private int _disposed;

    public TouchDebugViewModel(
        ContactManager contacts,
        TouchScheduler scheduler,
        ITouchContactAllocator allocator,
        IUiDispatcher dispatcher)
    {
        _contacts = contacts;
        _scheduler = scheduler;
        _allocator = allocator;
        _dispatcher = dispatcher;
        _contacts.ContactsChanged += OnSourceChanged;
        _scheduler.FpsChanged += OnSourceChanged;
        CaptureAndSchedule();
    }

    public ObservableCollection<TouchOwnerDisplay> Contacts { get; } = [];

    public double InjectedFps
    {
        get => _injectedFps;
        private set => SetProperty(ref _injectedFps, value);
    }

    private void OnSourceChanged(object? sender, EventArgs e) => CaptureAndSchedule();

    private void CaptureAndSchedule()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        var leases = _allocator.ActiveLeases
            .OrderBy(lease => lease.ContactId)
            .Select(ToDisplay)
            .ToArray();
        Interlocked.Exchange(ref _latestSnapshot, new DebugSnapshot(leases, _scheduler.CurrentFps));

        if (Interlocked.CompareExchange(ref _updateScheduled, 1, 0) == 0)
        {
            _dispatcher.Post(ApplyLatestSnapshot);
        }
    }

    private void ApplyLatestSnapshot()
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            Interlocked.Exchange(ref _latestSnapshot, null);
            Volatile.Write(ref _updateScheduled, 0);
            return;
        }

        var snapshot = Interlocked.Exchange(ref _latestSnapshot, null);
        if (snapshot is not null)
        {
            Contacts.Clear();
            foreach (var contact in snapshot.Contacts)
            {
                Contacts.Add(contact);
            }

            InjectedFps = snapshot.InjectedFps;
        }

        Volatile.Write(ref _updateScheduled, 0);
        if (Volatile.Read(ref _disposed) == 0 &&
            Volatile.Read(ref _latestSnapshot) is not null &&
            Interlocked.CompareExchange(ref _updateScheduled, 1, 0) == 0)
        {
            _dispatcher.Post(ApplyLatestSnapshot);
        }
    }

    private static TouchOwnerDisplay ToDisplay(TouchContactLease lease)
    {
        var parts = lease.OwnerId.Split(':', 2);
        var type = parts[0] switch
        {
            "camera" => "Камера",
            "joystick" => "Джойстик",
            "mouse-area" => "Область мыши",
            "binding" => "Привязка",
            _ => "Динамический контакт"
        };
        return new TouchOwnerDisplay(
            lease.ContactId,
            lease.OwnerId,
            type,
            parts.Length == 2 ? parts[1] : null,
            lease.State);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _contacts.ContactsChanged -= OnSourceChanged;
        _scheduler.FpsChanged -= OnSourceChanged;
        Interlocked.Exchange(ref _latestSnapshot, null);
    }

    private sealed record DebugSnapshot(TouchOwnerDisplay[] Contacts, double InjectedFps);
}
