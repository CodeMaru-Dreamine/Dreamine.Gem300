using Dreamine.Gem300.Abstractions.States;
using Dreamine.Gem300.Infrastructure;
using Dreamine.Gem300.Substrate;
using Xunit;

namespace Dreamine.Gem300.Tests;

public sealed class SubstrateTrackerTests
{
    [Fact]
    public void MovementUpdatesTransportOccupancyAndHistory()
    {
        var time = new ManualTimeProvider(); var tracker = new SubstrateTracker(new Gem300EventJournal(time), time); tracker.Register("S1", "SRC", "DST");
        time.Advance(TimeSpan.FromSeconds(2)); tracker.Move("S1", "CHAMBER"); time.Advance(TimeSpan.FromSeconds(3)); tracker.Move("S1", "DST");
        var substrate = tracker.Get("S1"); Assert.Equal(SubstrateTransportState.AtDestination, substrate.TransportState); Assert.Equal(3, substrate.History.Count); Assert.Equal(TimeSpan.FromSeconds(2), substrate.History[0].TimeOut - substrate.History[0].TimeIn); Assert.Equal(MaterialLocationState.Unoccupied, tracker.GetLocationState("SRC"));
    }

    [Fact]
    public void OccupiedLocationRejectsSecondSubstrate()
    {
        var tracker = new SubstrateTracker(new Gem300EventJournal()); tracker.Register("S1", "SRC", "DST");
        Assert.Throws<InvalidOperationException>(() => tracker.Register("S2", "SRC", "DST2"));
    }

    [Fact]
    public void UnconfirmedSubstrateCannotProcessUntilConfirmed()
    {
        var tracker = new SubstrateTracker(new Gem300EventJournal()); tracker.Register("S1", "SRC", "DST", false);
        Assert.Throws<InvalidOperationException>(() => tracker.BeginProcessing("S1")); tracker.ConfirmId("S1"); tracker.BeginProcessing("S1"); tracker.CompleteProcessing("S1", SubstrateProcessingState.Processed);
        Assert.Equal(SubstrateProcessingState.Processed, tracker.Get("S1").ProcessingState);
    }

    [Theory]
    [InlineData(SubstrateProcessingState.Processed)]
    [InlineData(SubstrateProcessingState.Aborted)]
    [InlineData(SubstrateProcessingState.Stopped)]
    [InlineData(SubstrateProcessingState.Rejected)]
    public void ProcessingSupportsVerifiedTerminalResults(SubstrateProcessingState result)
    {
        var tracker = new SubstrateTracker(new Gem300EventJournal()); tracker.Register("S1", "SRC", "DST"); tracker.BeginProcessing("S1"); tracker.CompleteProcessing("S1", result);
        Assert.Equal(result, tracker.Get("S1").ProcessingState);
    }

    [Fact]
    public void UndefinedProcessingResultIsRejected()
    {
        var tracker = new SubstrateTracker(new Gem300EventJournal());
        tracker.Register("S1", "SRC", "DST");
        tracker.BeginProcessing("S1");
        Assert.Throws<ArgumentOutOfRangeException>(() => tracker.CompleteProcessing("S1", (SubstrateProcessingState)999));
        Assert.Equal(SubstrateProcessingState.InProcess, tracker.Get("S1").ProcessingState);
    }

    [Fact]
    public void LostSubstrateReleasesLocationAndCannotMove()
    {
        var tracker = new SubstrateTracker(new Gem300EventJournal()); tracker.Register("S1", "SRC", "DST"); tracker.MarkLost("S1");
        Assert.Equal(MaterialLocationState.Unoccupied, tracker.GetLocationState("SRC")); Assert.Throws<InvalidOperationException>(() => tracker.Move("S1", "X")); tracker.Remove("S1");
    }

    [Fact]
    public void ActiveSubstrateCannotBeRemovedAtDestination()
    {
        var tracker = new SubstrateTracker(new Gem300EventJournal()); tracker.Register("S1", "SRC", "DST"); tracker.Move("S1", "DST");
        Assert.Throws<InvalidOperationException>(() => tracker.Remove("S1"));
    }
}
