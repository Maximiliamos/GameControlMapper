using GameControlMapper.Models;
using GameControlMapper.Services;
using GameControlMapper.ViewModels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameControlMapper.Tests;

public sealed class UiAsyncStabilityTests
{
    [Fact]
    public async Task AsyncRelayCommand_ExceptionIsObserved()
    {
        var command = new AsyncRelayCommand(_ => throw new InvalidOperationException("expected"));
        var exception = await Record.ExceptionAsync(() => command.ExecuteAsync());
        Assert.Null(exception);
    }

    [Fact]
    public async Task AsyncRelayCommand_RaisesFailure()
    {
        var expected = new InvalidOperationException("expected");
        Exception? observed = null;
        var command = new AsyncRelayCommand(_ => throw expected);
        command.ExecutionFailed += (_, args) => observed = args.Exception;

        await command.ExecuteAsync();

        Assert.Same(expected, observed);
    }

    [Fact]
    public async Task AsyncRelayCommand_RestoresCanExecute()
    {
        var command = new AsyncRelayCommand(_ => throw new InvalidOperationException("expected"));
        await command.ExecuteAsync();
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task AsyncRelayCommand_PreventsConcurrentExecution()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var command = new AsyncRelayCommand(async _ =>
        {
            Interlocked.Increment(ref calls);
            entered.TrySetResult();
            await release.Task;
        });

        var first = command.ExecuteAsync();
        await entered.Task;
        await command.ExecuteAsync();
        Assert.Equal(1, calls);
        Assert.False(command.CanExecute(null));
        release.TrySetResult();
        await first;
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task InitializationFailure_DoesNotCrashDispatcher()
    {
        var exception = await Record.ExceptionAsync(() => UiInitializationGuard.RunAsync(
            () => throw new IOException("profile store unavailable"),
            NullLogger.Instance,
            _ => { }));
        Assert.Null(exception);
    }

    [Fact]
    public async Task InitializationFailure_IsLogged()
    {
        var logger = new CollectingLogger();
        await UiInitializationGuard.RunAsync(
            () => throw new IOException("profile store unavailable"),
            logger,
            _ => { });
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Error && entry.Exception is IOException);
    }

    [Fact]
    public async Task InitializationFailure_ShowsRecoverableState()
    {
        Exception? recoverableFailure = null;
        await UiInitializationGuard.RunAsync(
            () => throw new IOException("profile store unavailable"),
            NullLogger.Instance,
            ex => recoverableFailure = ex);
        Assert.IsType<IOException>(recoverableFailure);
    }

    [Fact]
    public async Task TouchDebug_BackgroundCallbackIsMarshalledToUi()
    {
        using var fixture = new DebugFixture();
        fixture.Dispatcher.Drain();
        fixture.Allocator.TryAcquire(1, "binding:Fire");

        await Task.Run(() => fixture.Contacts.GetOrCreate(0));

        Assert.Empty(fixture.ViewModel.Contacts);
        Assert.True(fixture.Dispatcher.PendingCount > 0);
        fixture.Dispatcher.Drain();
        Assert.Single(fixture.ViewModel.Contacts);
    }

    [Fact]
    public void TouchDebug_UpdatesFromSingleUiAction()
    {
        using var fixture = new DebugFixture();
        fixture.Dispatcher.Drain();
        var baseline = fixture.Dispatcher.PostCount;
        fixture.Allocator.TryAcquire(1, "binding:Fire");

        for (var index = 0; index < 20; index++) fixture.Contacts.ReleaseAll();

        Assert.Equal(baseline + 1, fixture.Dispatcher.PostCount);
        fixture.Dispatcher.Drain();
        Assert.Single(fixture.ViewModel.Contacts);
    }

    [Fact]
    public void TouchDebug_DisposeUnsubscribes()
    {
        var fixture = new DebugFixture();
        fixture.Dispatcher.Drain();
        fixture.ViewModel.Dispose();
        var baseline = fixture.Dispatcher.PostCount;
        fixture.Contacts.ReleaseAll();
        Assert.Equal(baseline, fixture.Dispatcher.PostCount);
        fixture.Dispose();
    }

    [Fact]
    public void TouchDebug_UpdateAfterDisposeIsIgnored()
    {
        var fixture = new DebugFixture();
        fixture.Dispatcher.Drain();
        fixture.Allocator.TryAcquire(1, "binding:Fire");
        fixture.Contacts.ReleaseAll();
        fixture.ViewModel.Dispose();
        fixture.Dispatcher.Drain();
        Assert.Empty(fixture.ViewModel.Contacts);
        fixture.Dispose();
    }

    [Fact]
    public async Task TouchDebug_RapidUpdatesDoNotThrow()
    {
        using var fixture = new DebugFixture();
        fixture.Dispatcher.Drain();
        var exception = await Record.ExceptionAsync(() => Task.WhenAll(
            Enumerable.Range(0, 50).Select(_ => Task.Run(fixture.Contacts.ReleaseAll))));
        fixture.Dispatcher.Drain();
        Assert.Null(exception);
    }

    [Fact]
    public void TouchDebug_OwnerMetadataIsPreserved()
    {
        using var fixture = new DebugFixture();
        fixture.Dispatcher.Drain();
        fixture.Allocator.TryAcquire(7, "binding:Fire");
        fixture.Contacts.ReleaseAll();
        fixture.Dispatcher.Drain();
        var item = Assert.Single(fixture.ViewModel.Contacts);
        Assert.Equal("binding:Fire", item.OwnerId);
        Assert.Equal("Fire", item.BindingName);
        Assert.Equal(7, fixture.Allocator.ActiveLeases.Single().SessionGeneration);
    }

    private sealed class DebugFixture : IDisposable
    {
        public ContactManager Contacts { get; }
        public TouchContactAllocator Allocator { get; }
        public QueuedUiDispatcher Dispatcher { get; } = new();
        public TouchDebugViewModel ViewModel { get; }

        public DebugFixture()
        {
            var capabilities = new TouchCapabilities(10, true, false, true);
            Contacts = new ContactManager(NullLogger<ContactManager>.Instance, capabilities);
            Allocator = new TouchContactAllocator(capabilities, NullLogger<TouchContactAllocator>.Instance);
            var scheduler = new TouchScheduler(
                NullLogger<TouchScheduler>.Instance,
                Contacts,
                new NoOpTouchBackend(),
                new FrameContext());
            ViewModel = new TouchDebugViewModel(Contacts, scheduler, Allocator, Dispatcher);
        }

        public void Dispose() => ViewModel.Dispose();
    }

    private sealed class QueuedUiDispatcher : IUiDispatcher
    {
        private readonly Queue<Action> _actions = new();
        public int PostCount { get; private set; }
        public int PendingCount { get { lock (_actions) return _actions.Count; } }
        public bool CheckAccess() => false;
        public void Post(Action action)
        {
            lock (_actions)
            {
                PostCount++;
                _actions.Enqueue(action);
            }
        }
        public void Drain()
        {
            while (true)
            {
                Action action;
                lock (_actions)
                {
                    if (_actions.Count == 0) return;
                    action = _actions.Dequeue();
                }
                action();
            }
        }
    }

    private sealed class NoOpTouchBackend : ITouchBackend
    {
        public TouchCapabilities Capabilities { get; } = new(10, true, false, true);
        public bool Initialize() => true;
        public bool SendFrame(TouchFrame frame) => true;
        public void Shutdown() { }
    }

    private sealed class CollectingLogger : ILogger
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Entries.Add((logLevel, exception));
    }
}
