using Dreamine.Gem300.Abstractions.Model;
using Dreamine.Gem300.Abstractions.States;
using Dreamine.Gem300.Infrastructure;
using Dreamine.Gem300.ObjectServices;
using Dreamine.Gem.Abstractions.Model;
using Dreamine.Gem.Abstractions.States;
using Dreamine.Secs.Abstractions.Model;
using Xunit;

namespace Dreamine.Gem300.Tests;

public sealed class ObjectAndEventTests
{
    [Fact]
    public void ObjectServiceAddsFundamentalAttributesAndEnforcesAccess()
    {
        var events = new Gem300EventJournal(); var service = new Gem300ObjectService(events); var key = new Gem300ObjectKey("Carrier", "C1");
        service.Register(key, new[] { new Gem300AttributeDefinition("Usage", new SecsAsciiItem("PRODUCT"), true), new Gem300AttributeDefinition("Capacity", new SecsUInt16Item(25), false) });
        Assert.True(service.TryGetAttribute(key, "ObjType", out var type)); Assert.Equal("Carrier", Assert.IsType<SecsAsciiItem>(type).Value);
        Assert.False(service.TrySetAttribute(key, "Capacity", new SecsUInt16Item(13))); Assert.True(service.TrySetAttribute(key, "Usage", new SecsAsciiItem("TEST")));
        Assert.Equal("TEST", Assert.IsType<SecsAsciiItem>(service.GetAttributes(key)["Usage"]).Value);
    }

    [Fact]
    public void ObjectServiceRejectsDuplicateObjectAndReservedAttributes()
    {
        var service = new Gem300ObjectService(new Gem300EventJournal()); var key = new Gem300ObjectKey("Subst", "S1"); service.Register(key, []);
        Assert.Throws<InvalidOperationException>(() => service.Register(key, []));
        Assert.Throws<ArgumentException>(() => new Gem300ObjectService(new Gem300EventJournal()).Register(new("X", "1"), new[] { new Gem300AttributeDefinition("ObjID", new SecsAsciiItem("2"), false) }));
    }

    [Fact]
    public void EventJournalUsesInjectedTimeMonotonicSequenceAndCapacity()
    {
        var time = new ManualTimeProvider(); var journal = new Gem300EventJournal(time, 2);
        journal.Record(Gem300EventKind.ObjectChanged, "1"); time.Advance(TimeSpan.FromSeconds(1)); journal.Record(Gem300EventKind.CarrierChanged, "2"); journal.Record(Gem300EventKind.SubstrateChanged, "3");
        var events = journal.GetSnapshot(); Assert.Equal(new long[] { 2, 3 }, events.Select(static value => value.Sequence)); Assert.Equal(DateTimeOffset.UnixEpoch.AddSeconds(1), events[0].OccurredAt);
    }

    [Fact]
    public void EventJournalRejectsUndefinedEventKind()
    {
        var journal = new Gem300EventJournal();
        Assert.Throws<ArgumentOutOfRangeException>(() => journal.Record((Gem300EventKind)999, "A1"));
        Assert.Empty(journal.GetSnapshot());
    }

    [Fact]
    public void ConcurrentObjectUpdatesRemainTypedAndObservable()
    {
        var journal = new Gem300EventJournal(capacity: 1000); var service = new Gem300ObjectService(journal); var key = new Gem300ObjectKey("Counter", "1");
        service.Register(key, new[] { new Gem300AttributeDefinition("Value", new SecsUInt32Item(0), true) });
        Parallel.For(1, 101, index => Assert.True(service.TrySetAttribute(key, "Value", new SecsUInt32Item((uint)index))));
        Assert.IsType<SecsUInt32Item>(service.GetAttributes(key)["Value"]); Assert.Equal(101, journal.GetSnapshot().Count);
    }

    [Fact]
    public async Task ObjectActionSupportsTypedParametersAndInjectedTimeout()
    {
        var time = new ManualTimeProvider(); var service = new Gem300ObjectService(new Gem300EventJournal(time), time); var key = new Gem300ObjectKey("Process", "1"); service.Register(key, []);
        service.RegisterAction(key, "Run", (parameters, _) => ValueTask.FromResult(new GemCommandResult(GemCommandStatus.Completed, Assert.IsType<SecsAsciiItem>(parameters["Mode"]).Value)));
        var completed = await service.ExecuteActionAsync(key, "Run", new Dictionary<string, SecsItem> { ["Mode"] = new SecsAsciiItem("AUTO") }, TimeSpan.FromSeconds(5)); Assert.Equal("AUTO", completed.Detail);
        var completion = new TaskCompletionSource<GemCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously); service.RegisterAction(key, "Wait", (_, _) => new(completion.Task));
        var waiting = service.ExecuteActionAsync(key, "Wait", new Dictionary<string, SecsItem>(), TimeSpan.FromSeconds(5)).AsTask(); time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(GemCommandStatus.Failed, (await waiting).Status);
    }

    [Fact]
    public async Task ObjectActionReceivesStableReadOnlyParameterSnapshot()
    {
        var service = new Gem300ObjectService(new Gem300EventJournal());
        var key = new Gem300ObjectKey("Carrier", "C1");
        service.Register(key, []);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IReadOnlyDictionary<string, SecsItem>? observed = null;
        service.RegisterAction(key, "Assign", async (parameters, cancellationToken) =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
            observed = parameters;
            return new GemCommandResult(GemCommandStatus.Completed);
        });
        var callerOwned = new Dictionary<string, SecsItem>(StringComparer.Ordinal) { ["PORT"] = new SecsAsciiItem("P1") };

        var execution = service.ExecuteActionAsync(key, "Assign", callerOwned, TimeSpan.FromSeconds(5)).AsTask();
        await entered.Task;
        callerOwned["PORT"] = new SecsAsciiItem("P2");
        release.TrySetResult();

        Assert.Equal(GemCommandStatus.Completed, (await execution).Status);
        Assert.Equal("P1", Assert.IsType<SecsAsciiItem>(observed!["PORT"]).Value);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, SecsItem>)observed).Add("EXTRA", new SecsAsciiItem("X")));
    }
}
