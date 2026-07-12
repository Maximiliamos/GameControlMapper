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
        if (_cts != null) return;

        _cts = new CancellationTokenSource();
        _lastFpsUpdate = DateTime.Now;
        _worker = Task.Run(() => RunLoopAsync(_cts.Token), _cts.Token);
        _logger.LogInformation("TouchScheduler started");
    }

    public void Stop()
    {
        if (_cts == null) return;

        _cts.Cancel();
        try { _worker?.Wait(TimeSpan.FromSeconds(1)); }
        catch (AggregateException ex) when (ex.InnerExceptions.All(e => e is TaskCanceledException)) { }
        _cts.Dispose();
        _cts = null;
        _worker = null;
        _logger.LogInformation("TouchScheduler stopped");
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await SendFrameAsync(ct);
                await Task.Delay(8, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { _logger.LogError(ex, "Touch scheduler stopped unexpectedly"); }
    }

    private Task SendFrameAsync(CancellationToken ct)
    {
        _frameId++;
        _frameCount++;
        
        // Calculate FPS
        var now = DateTime.Now;
        if ((now - _lastFpsUpdate).TotalSeconds >= 1)
        {
            CurrentFps = _frameCount / (now - _lastFpsUpdate).TotalSeconds;
            _frameCount = 0;
            _lastFpsUpdate = now;
        }
        
        var contacts = _manager.GetActiveContacts();
        if (contacts.Count == 0) return Task.CompletedTask;

        using var frame = new TouchFrame(
            _frameId,
            now,
            TimeSpan.FromMilliseconds(8),
            _context);
        frame.SetContacts(contacts);

        LogFrame(frame);

        if (!_backend.SendFrame(frame)) return Task.CompletedTask;
        _manager.AdvanceSentContacts(contacts.Where(c => c.State == TouchState.Down).Select(c => c.ContactId));
        _manager.CompleteReleasedContacts(contacts.Where(c => c.State == TouchState.Up).Select(c => c.ContactId));
        FrameSent?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
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
        Stop();
    }
}
