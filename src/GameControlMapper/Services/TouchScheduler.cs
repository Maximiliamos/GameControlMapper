using System;
using System.Threading;
using System.Threading.Tasks;
using GameControlMapper.Models;
using Microsoft.Extensions.Logging;

namespace GameControlMapper.Services;

public class TouchScheduler : IDisposable
{
    private readonly ILogger<TouchScheduler> _logger;
    private readonly ContactManager _manager;
    private readonly ITouchBackend _backend;
    private readonly FrameContext _context;
    private CancellationTokenSource? _cts;
    private int _frameId = 0;
    
    public event EventHandler? FrameSent;
    
    private int _frameCount;
    private DateTime _lastFpsUpdate;
    private double _currentFps;
    private Task? _worker;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private volatile bool _paused;
    private bool _disposed;
    public double CurrentFps
    {
        get => _currentFps;
        private set
        {
            if (_currentFps != value)
            {
                _currentFps = value;
                FpsChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }
    public event EventHandler? FpsChanged;

    public TouchScheduler(
        ILogger<TouchScheduler> logger,
        ContactManager manager,
        ITouchBackend backend,
        FrameContext context)
    {
        _logger = logger;
        _manager = manager;
        _backend = backend;
        _context = context;
    }

    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(TouchScheduler));
        _paused = false;
        if (_cts != null) return;

        _cts = new CancellationTokenSource();
        _lastFpsUpdate = DateTime.Now;
        _worker = Task.Run(() => RunLoopAsync(_cts.Token), _cts.Token);
        _logger.LogInformation("TouchScheduler started");
    }

    public void Stop()
    {
        ShutdownAsync().GetAwaiter().GetResult();
    }

    public async Task ShutdownAsync()
    {
        if (_cts == null) return;

        _cts.Cancel();
        if (_worker is not null)
        {
            try { await _worker.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _cts.Dispose();
        _cts = null;
        _worker = null;
        _logger.LogInformation("TouchScheduler stopped");
    }

    public void Resume() => _paused = false;

    public async Task<TouchShutdownResult> PauseAndFlushAsync(CancellationToken cancellationToken = default)
    {
        _paused = true;
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var contactIds = _manager.PrepareForShutdown();
            if (contactIds.Count == 0)
            {
                return new TouchShutdownResult(true, false, [], []);
            }

            var contacts = _manager.GetContactsForFrame();
            using var frame = CreateFrame(contacts);
            var sent = _backend.SendFrame(frame);
            if (sent)
            {
                _manager.CompleteReleasedContacts(contactIds);
                FrameSent?.Invoke(this, EventArgs.Empty);
                return new TouchShutdownResult(true, true, contactIds, []);
            }

            _logger.LogError("Final touch release frame failed for contacts: {ContactIds}", string.Join(", ", contactIds));
            _manager.DiscardContacts(contactIds);
            return new TouchShutdownResult(false, true, [], contactIds);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (!_paused) await SendFrameAsync(ct).ConfigureAwait(false);
                await Task.Delay(8, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { _logger.LogError(ex, "Touch scheduler stopped unexpectedly"); }
    }

    private Task SendFrameAsync(CancellationToken ct)
    {
        return SendFrameCoreAsync(ct);
    }

    private async Task SendFrameCoreAsync(CancellationToken ct)
    {
        await _sendGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_paused) return;
            SendFrameUnderGate();
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private void SendFrameUnderGate()
    {
        _frameCount++;
        
        // Calculate FPS
        var now = DateTime.Now;
        if ((now - _lastFpsUpdate).TotalSeconds >= 1)
        {
            CurrentFps = _frameCount / (now - _lastFpsUpdate).TotalSeconds;
            _frameCount = 0;
            _lastFpsUpdate = now;
        }
        
        var contacts = _manager.GetContactsForFrame();
        if (contacts.Count == 0) return;

        using var frame = CreateFrame(contacts);

        LogFrame(frame);

        if (!_backend.SendFrame(frame)) return;
        _manager.AdvanceSentContacts(contacts.Where(c => c.State == TouchState.Down).Select(c => c.ContactId));
        _manager.CompleteReleasedContacts(contacts.Where(c => c.State == TouchState.Up).Select(c => c.ContactId));
        FrameSent?.Invoke(this, EventArgs.Empty);
    }

    private TouchFrame CreateFrame(List<TouchContact> contacts)
    {
        var frame = new TouchFrame(++_frameId, DateTime.Now, TimeSpan.FromMilliseconds(8), _context);
        frame.SetContacts(contacts);
        return frame;
    }

    private void LogFrame(TouchFrame frame)
    {
        if (!_context.IsDebugMode) return;
        var contacts = frame.GetContacts();
        _logger.LogInformation("FRAME {FrameId} | Timestamp: {Timestamp}", frame.FrameId, DateTime.Now.ToString("HH:mm:ss.fff"));
        foreach (var contact in contacts)
        {
            string contactType = contact.ContactId switch
            {
                0 => "Camera",
                1 => "Joystick",
                2 => "Fire",
                3 => "Aim",
                _ => "Other"
            };
            _logger.LogInformation(
                "  Contact {ContactId} | Type: {Type} | State: {State} | X: {X:F0} | Y: {Y:F0}",
                contact.ContactId,
                contactType,
                contact.State,
                contact.X,
                contact.Y);
        }
        _logger.LogInformation("  InjectTouchInput({Count})", contacts.Length);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _sendGate.Dispose();
        _disposed = true;
    }
}
