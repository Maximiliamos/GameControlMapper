using System.Collections.Concurrent;
using GameControlMapper.Models;
using GameControlMapper.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameControlMapper.Tests;

public sealed class GracefulTouchShutdownTests
{
    [Fact]
    public async Task StopWithNoContacts_DoesNotSendEmptyFrame()
    {
        using var fixture = new SchedulerFixture();

        var result = await fixture.Scheduler.PauseAndFlushAsync();

        Assert.True(result.Succeeded);
        Assert.False(result.FinalFrameAttempted);
        Assert.Empty(fixture.Backend.Frames);
    }

    [Fact]
    public async Task StopAfterSentDown_SendsExactlyOneUpAndClearsContact()
    {
        using var fixture = new SchedulerFixture();
        fixture.Manager.StartContact(4, 100, 200);
        fixture.StartAndWaitForFrame();

        var result = await fixture.Scheduler.PauseAndFlushAsync();

        Assert.True(result.Succeeded);
        Assert.Single(fixture.Backend.Contacts(TouchState.Down, 4));
        Assert.Single(fixture.Backend.Contacts(TouchState.Up, 4));
        Assert.Empty(fixture.Manager.GetActiveContacts());
    }

    [Fact]
    public async Task StopWithMultipleSentContacts_SendsAllIdsInFinalUpFrame()
    {
        using var fixture = new SchedulerFixture();
        fixture.Manager.StartContact(0, 10, 20);
        fixture.Manager.StartContact(1, 30, 40);
        fixture.StartAndWaitForFrame();

        var result = await fixture.Scheduler.PauseAndFlushAsync();
        var finalFrame = fixture.Backend.Frames.Last();

        Assert.True(result.Succeeded);
        Assert.Equal([0, 1], finalFrame.Contacts.Select(c => c.ContactId).OrderBy(id => id));
        Assert.All(finalFrame.Contacts, contact => Assert.Equal(TouchState.Up, contact.State));
        Assert.Empty(fixture.Manager.GetActiveContacts());
    }

    [Fact]
    public async Task StopBeforeFirstDownWasSent_DropsContactWithoutOrphanUp()
    {
        using var fixture = new SchedulerFixture();
        fixture.Manager.StartContact(4, 100, 200);

        var result = await fixture.Scheduler.PauseAndFlushAsync();

        Assert.True(result.Succeeded);
        Assert.False(result.FinalFrameAttempted);
        Assert.Empty(fixture.Backend.Frames);
        Assert.Empty(fixture.Manager.GetActiveContacts());
    }

    [Fact]
    public async Task RepeatedStop_DoesNotSendAdditionalUp()
    {
        using var fixture = new SchedulerFixture();
        fixture.Manager.StartContact(4, 100, 200);
        fixture.StartAndWaitForFrame();

        await fixture.Scheduler.PauseAndFlushAsync();
        var framesAfterFirstStop = fixture.Backend.Frames.Count;
        var secondResult = await fixture.Scheduler.PauseAndFlushAsync();

        Assert.True(secondResult.Succeeded);
        Assert.False(secondResult.FinalFrameAttempted);
        Assert.Equal(framesAfterFirstStop, fixture.Backend.Frames.Count);
        Assert.Single(fixture.Backend.Contacts(TouchState.Up, 4));
    }

    [Fact]
    public async Task StopConcurrentWithTick_WaitsForTickThenSendsOneFinalUp()
    {
        using var enteredSend = new ManualResetEventSlim();
        using var releaseSend = new ManualResetEventSlim();
        using var fixture = new SchedulerFixture(new RecordingBackend((_, contacts) =>
        {
            if (contacts.Any(c => c.State == TouchState.Down))
            {
                enteredSend.Set();
                releaseSend.Wait(TimeSpan.FromSeconds(5));
            }
            return true;
        }));
        fixture.Manager.StartContact(4, 100, 200);
        fixture.Scheduler.Start();
        Assert.True(enteredSend.Wait(TimeSpan.FromSeconds(5)));

        fixture.Manager.ReleaseAll();
        var stopTask = fixture.Scheduler.PauseAndFlushAsync();
        Assert.False(stopTask.IsCompleted);
        releaseSend.Set();
        var result = await stopTask;

        Assert.True(result.Succeeded);
        Assert.Single(fixture.Backend.Contacts(TouchState.Up, 4));
        var upFrameIndex = fixture.Backend.Frames.FindIndex(f => f.Contacts.Any(c => c.State == TouchState.Up));
        Assert.DoesNotContain(fixture.Backend.Frames.Skip(upFrameIndex + 1).SelectMany(f => f.Contacts), c => c.State == TouchState.Update);
        Assert.Empty(fixture.Manager.GetActiveContacts());
    }

    [Fact]
    public async Task FailedFinalSend_ReportsFailureLogsErrorAndDoesNotClaimRelease()
    {
        var logger = new RecordingLogger<TouchScheduler>();
        using var fixture = new SchedulerFixture(new RecordingBackend((_, contacts) =>
            !contacts.All(c => c.State == TouchState.Up)), logger);
        fixture.Manager.StartContact(4, 100, 200);
        fixture.StartAndWaitForFrame();

        var result = await fixture.Scheduler.PauseAndFlushAsync();

        Assert.False(result.Succeeded);
        Assert.True(result.FinalFrameAttempted);
        Assert.Empty(result.ReleasedContactIds);
        Assert.Equal([4], result.FailedContactIds);
        Assert.Contains(logger.Messages, message => message.Contains("Final touch release frame failed"));
        Assert.Empty(fixture.Manager.GetActiveContacts());
    }

    [Fact]
    public async Task DisposeAfterStop_DoesNotSendAdditionalFrames()
    {
        var fixture = new SchedulerFixture();
        fixture.Manager.StartContact(4, 100, 200);
        fixture.StartAndWaitForFrame();
        await fixture.Scheduler.PauseAndFlushAsync();
        var frameCount = fixture.Backend.Frames.Count;

        fixture.Dispose();
        fixture.Dispose();

        Assert.Equal(frameCount, fixture.Backend.Frames.Count);
    }

    private sealed class SchedulerFixture : IDisposable
    {
        public ContactManager Manager { get; }
        public RecordingBackend Backend { get; }
        public TouchScheduler Scheduler { get; }

        public SchedulerFixture(RecordingBackend? backend = null, ILogger<TouchScheduler>? logger = null)
        {
            Manager = new ContactManager(NullLogger<ContactManager>.Instance, new TouchCapabilities(10, true, false, true));
            Backend = backend ?? new RecordingBackend();
            Scheduler = new TouchScheduler(logger ?? NullLogger<TouchScheduler>.Instance, Manager, Backend, new FrameContext());
        }

        public void StartAndWaitForFrame()
        {
            Scheduler.Start();
            Assert.True(Backend.FrameSent.Wait(TimeSpan.FromSeconds(5)));
        }

        public void Dispose() => Scheduler.Dispose();
    }

    private sealed class RecordingBackend : ITouchBackend
    {
        private readonly Func<int, IReadOnlyList<TouchContactSnapshot>, bool> _sendResult;
        private readonly object _gate = new();
        private int _sendCount;

        public RecordingBackend(Func<int, IReadOnlyList<TouchContactSnapshot>, bool>? sendResult = null)
        {
            _sendResult = sendResult ?? ((_, _) => true);
        }

        public TouchCapabilities Capabilities { get; } = new(10, true, false, true);
        public List<TouchFrameSnapshot> Frames { get; } = [];
        public ManualResetEventSlim FrameSent { get; } = new();
        public bool Initialize() => true;

        public bool SendFrame(TouchFrame frame)
        {
            var contacts = frame.GetContacts().ToArray()
                .Select(c => new TouchContactSnapshot(c.ContactId, c.X, c.Y, c.State))
                .ToArray();
            var sendNumber = Interlocked.Increment(ref _sendCount);
            var result = _sendResult(sendNumber, contacts);
            lock (_gate) Frames.Add(new TouchFrameSnapshot(frame.FrameId, frame.Timestamp, contacts));
            FrameSent.Set();
            return result;
        }

        public List<TouchContactSnapshot> Contacts(TouchState state, int id)
        {
            lock (_gate) return Frames.SelectMany(f => f.Contacts).Where(c => c.State == state && c.ContactId == id).ToList();
        }

        public void Shutdown() { }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Messages.Enqueue(formatter(state, exception));
    }
}
