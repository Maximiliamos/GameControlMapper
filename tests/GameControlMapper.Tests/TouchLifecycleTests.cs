using GameControlMapper.Models;
using GameControlMapper.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameControlMapper.Tests;

public sealed class TouchLifecycleTests
{
    private static ContactManager CreateManager() => new(
        NullLogger<ContactManager>.Instance,
        new TouchCapabilities(10, true, false, true));

    [Fact]
    public void EndTouch_RemainsInUpStateUntilFrameIsCompleted()
    {
        var manager = CreateManager();
        var engine = new TouchEngine(NullLogger<TouchEngine>.Instance, manager);

        engine.StartTouch(4, 100, 200);
        engine.EndTouch(4);

        Assert.Equal(TouchState.Up, manager.GetActiveContacts().Single().State);
        manager.CompleteReleasedContacts([4]);
        Assert.Empty(manager.GetActiveContacts());
    }

    [Fact]
    public void ReleaseAll_ProducesUpContactsInsteadOfDroppingThem()
    {
        var manager = CreateManager();
        var engine = new TouchEngine(NullLogger<TouchEngine>.Instance, manager);
        engine.StartTouch(0, 10, 20);
        engine.StartTouch(1, 30, 40);

        engine.ReleaseAll();

        Assert.All(manager.GetActiveContacts(), contact => Assert.Equal(TouchState.Up, contact.State));
    }

    [Fact]
    public void ActiveContacts_IsAnIndependentSnapshot()
    {
        var manager = CreateManager();
        manager.StartContact(2, 10, 20);
        var snapshot = manager.ActiveContacts;

        manager.MoveContact(2, 50, 60);

        Assert.Equal(10, snapshot[2].X);
        Assert.Equal(50, manager.ActiveContacts[2].X);
    }

    [Fact]
    public void SentDownContact_AdvancesToUpdateForFollowingFrames()
    {
        var manager = CreateManager();
        manager.StartContact(1, 100, 200);

        manager.AdvanceSentContacts([1]);

        Assert.Equal(TouchState.Update, manager.ActiveContacts[1].State);
    }

    [Fact]
    public void MoveBeforeFirstFrame_UpdatesCoordinatesButPreservesDownState()
    {
        var manager = CreateManager();
        manager.StartContact(1, 100, 200);

        manager.MoveContact(1, 150, 250);

        Assert.Equal(TouchState.Down, manager.ActiveContacts[1].State);
        Assert.Equal(150, manager.ActiveContacts[1].X);
        Assert.Equal(250, manager.ActiveContacts[1].Y);
    }
}
