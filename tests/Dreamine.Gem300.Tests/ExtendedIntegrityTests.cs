using Dreamine.Communication.Abstractions.Enums;
using Dreamine.Gem.Abstractions.Interfaces;
using Dreamine.Gem.Abstractions.Model;
using Dreamine.Gem.Abstractions.States;
using Dreamine.Gem300.Abstractions.Interfaces;
using Dreamine.Gem300.Abstractions.Model;
using Dreamine.Gem300.Abstractions.States;
using Dreamine.Gem300.Infrastructure;
using Dreamine.Gem300.Jobs;
using Dreamine.Gem300.ObjectServices;
using Dreamine.Gem300.Substrate;
using Dreamine.Secs.Abstractions.Interfaces;
using Dreamine.Secs.Abstractions.Model;
using Xunit;

namespace Dreamine.Gem300.Tests;

public sealed class ExtendedIntegrityTests
{
    [Fact]
    public void CarrierArrivalPlanRejectsUndefinedNumericSlotStateBeforeMutation()
    {
        Assert.Throws<ArgumentException>(() => new CarrierArrivalPlan("P1", "C1", [(CarrierSlotState)999], []));
    }

    [Fact]
    public void NestedWritableAttributeRequiresTheOriginalListSchema()
    {
        var service = new Gem300ObjectService(new Gem300EventJournal());
        var key = new Gem300ObjectKey("Recipe", "R1");
        service.Register(key, [new Gem300AttributeDefinition("Schema", new SecsListItem(new SecsAsciiItem("A"), new SecsUInt16Item(1)), true)]);

        Assert.True(service.TrySetAttribute(key, "Schema", new SecsListItem(new SecsAsciiItem("B"), new SecsUInt16Item(2, 3))));
        Assert.False(service.TrySetAttribute(key, "Schema", new SecsListItem(new SecsAsciiItem("B"), new SecsUInt32Item(2))));
        Assert.False(service.TrySetAttribute(key, "Schema", new SecsListItem(new SecsAsciiItem("B"))));
    }

    [Fact]
    public void ObjectActionsAreBoundedAndCanBeExplicitlyUnregistered()
    {
        var service = new Gem300ObjectService(new Gem300EventJournal(), null, 1);
        var key = new Gem300ObjectKey("Carrier", "C1");
        service.Register(key, []);
        service.RegisterAction(key, "A", static (_, _) => ValueTask.FromResult(new GemCommandResult(GemCommandStatus.Completed)));

        Assert.Throws<InvalidOperationException>(() => service.RegisterAction(key, "B", static (_, _) => ValueTask.FromResult(new GemCommandResult(GemCommandStatus.Completed))));
        Assert.True(service.UnregisterAction(key, "A"));
        service.RegisterAction(key, "B", static (_, _) => ValueTask.FromResult(new GemCommandResult(GemCommandStatus.Completed)));
    }

    [Fact]
    public async Task ThrowingActionCancellationCallbackCannotMakeRemoveAmbiguous()
    {
        var service = new Gem300ObjectService(new Gem300EventJournal());
        var key = new Gem300ObjectKey("Carrier", "C1");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var never = new TaskCompletionSource<GemCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Register(key, []);
        service.RegisterAction(key, "Wait", (_, token) =>
        {
            token.Register(static () => throw new InvalidOperationException("callback failure"));
            entered.SetResult();
            return new(never.Task);
        });
        var execution = service.ExecuteActionAsync(key, "Wait", new Dictionary<string, SecsItem>(), TimeSpan.FromSeconds(5)).AsTask();
        await entered.Task;

        Assert.True(service.Remove(key));

        Assert.Equal(GemCommandStatus.Failed, (await execution).Status);
        Assert.False(service.TryGetAttribute(key, "ObjID", out _));
    }

    [Fact]
    public void ObjectEventsPreserveTypeAndIdAndJournalExposesRetentionHealth()
    {
        var journal = new Gem300EventJournal(capacity: 2);
        var service = new Gem300ObjectService(journal);
        service.Register(new("Carrier", "1"), []);
        service.Register(new("Substrate", "1"), []);
        service.Register(new("ProcessJob", "1"), []);

        var events = journal.GetSnapshot();
        Assert.Equal(["Substrate", "ProcessJob"], events.Select(static value => value.AggregateType));
        Assert.All(events, static value => Assert.Equal("1", value.AggregateId));
        Assert.Single(events.Select(static value => value.JournalId).Distinct());
        var health = journal.GetHealth();
        Assert.Equal(2, health.Capacity); Assert.Equal(2, health.RetainedCount); Assert.Equal(3, health.TotalRecorded); Assert.Equal(1, health.DroppedCount);
        Assert.Equal(2, health.FirstRetainedSequence); Assert.Equal(3, health.LastRetainedSequence);
        Assert.Equal([3L], journal.GetSnapshot(2, 10).Select(static value => value.Sequence));
        Assert.NotEqual(health.JournalId, new Gem300EventJournal().GetHealth().JournalId);
    }

    [Fact]
    public void EventPublisherFailureIsObservableWithoutAmbiguousMutationFailure()
    {
        var tracker = new SubstrateTracker(new ThrowingEventJournal());

        tracker.Register("S1", "SRC", "DST");

        var health = tracker.EventHealth;
        Assert.Equal(1, health.FailureCount);
        Assert.Contains("journal unavailable", health.LastError, StringComparison.Ordinal);
        Assert.NotNull(health.LastFailureAt);
    }

    [Fact]
    public void RuntimeModulesShareOneNonThrowingEventPublisherHealthCounter()
    {
        var programs = new FakeProcessPrograms();
        var runtime = new Gem300Runtime(new FakeGemRuntime(), programs, new ThrowingTimeProvider());

        runtime.Carriers.RegisterLoadPort("P1");

        Assert.Equal(1, runtime.EventHealth.FailureCount);
        Assert.Equal(0, runtime.Events.GetHealth().TotalRecorded);
        Assert.Equal(1, runtime.Carriers.EventHealth.FailureCount);
        Assert.Equal(1, runtime.Substrates.EventHealth.FailureCount);
        Assert.Equal(1, runtime.ProcessJobs.EventHealth.FailureCount);
        Assert.Equal(1, runtime.ControlJobs.EventHealth.FailureCount);
        Assert.Equal(1, runtime.Objects.EventHealth.FailureCount);
    }

    [Fact]
    public void SnapshotQueriesUseStableOrdinalIdentityOrder()
    {
        var events = new Gem300EventJournal();
        var substrates = new SubstrateTracker(events);
        substrates.Register("S2", "SRC2", "DST2"); substrates.Register("S1", "SRC1", "DST1");
        var programs = new FakeProcessPrograms(); programs.Put(new("R1", [1]));
        var processes = new ProcessJobManager(substrates, programs, events);
        processes.Create(new("P2", "R1", ["S2"])); processes.Create(new("P1", "R1", ["S1"]));

        Assert.Equal(["S1", "S2"], substrates.GetSnapshot().Select(static value => value.Id));
        Assert.Equal(["P1", "P2"], processes.GetSnapshot().Select(static value => value.Definition.Id));
    }

    [Fact]
    public async Task DeleteVersusClaimHasOnlyOneCommittedWinner()
    {
        for (var iteration = 0; iteration < 25; iteration++)
        {
            var events = new Gem300EventJournal(); var substrates = new SubstrateTracker(events); substrates.Register("S1", "SRC", "DST");
            var programs = new FakeProcessPrograms(); programs.Put(new("R1", [1]));
            var process = new ProcessJobManager(substrates, programs, events); process.Create(new("PJ1", "R1", ["S1"]));
            var controls = new ControlJobManager(process, events);
            using var barrier = new Barrier(2);
            Exception? claimError = null; Exception? deleteError = null;
            var claim = Task.Run(() => { barrier.SignalAndWait(); try { controls.Create(new("CJ1", ["PJ1"])); } catch (Exception exception) { claimError = exception; } });
            var delete = Task.Run(() => { barrier.SignalAndWait(); try { process.Delete("PJ1"); } catch (Exception exception) { deleteError = exception; } });
            await Task.WhenAll(claim, delete);

            Assert.True((claimError is null) ^ (deleteError is null));
            if (claimError is null) Assert.Equal(ProcessJobState.Queued, process.Get("PJ1").State);
            else Assert.Throws<KeyNotFoundException>(() => process.Get("PJ1"));
        }
    }

    [Fact]
    public void ConcreteGemRuntimeFactoryUsesItsOwnedProcessProgramStore()
    {
        var gem = new Dreamine.Gem.GemRuntime(new FakeGemTransport(), new GemEquipmentIdentity("MODEL", "1.0"));
        var runtime = Gem300Runtime.CreateFromGemRuntime(gem);
        gem.ProcessPrograms.Put(new("R1", [1, 2, 3]));
        runtime.Substrates.Register("S1", "SRC", "DST");

        runtime.ProcessJobs.Create(new("PJ1", "R1", ["S1"]));
        Assert.True(gem.ProcessPrograms.Delete("R1"));

        Assert.Equal([1, 2, 3], runtime.ProcessJobs.Get("PJ1").ProcessProgram!.Body.ToArray());
    }

    private sealed class ThrowingEventJournal : IGem300EventJournal
    {
        public Gem300DomainEvent Record(Gem300EventKind kind, string aggregateId) => throw new InvalidOperationException("journal unavailable");
        public IReadOnlyList<Gem300DomainEvent> GetSnapshot() => [];
    }

    private sealed class ThrowingTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => throw new InvalidOperationException("clock unavailable");
    }

    private sealed class FakeGemTransport : IGemMessageTransport
    {
        private uint _systemBytes;
        public ISecsConnection Connection { get; } = new FakeSecsConnection();
        public SecsSessionId SessionId { get; } = new(1);
        public event EventHandler<SecsMessage>? MessageReceived { add { } remove { } }
        public Task SendAsync(SecsMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<SecsMessage> RequestAsync(SecsMessage message, CancellationToken cancellationToken = default) => Task.FromException<SecsMessage>(new NotSupportedException());
        public SecsSystemBytes AllocateSystemBytes() => new(++_systemBytes);
    }

    private sealed class FakeSecsConnection : ISecsConnection
    {
        public string ProviderKey => "test";
        public ConnectionState State => ConnectionState.Connected;
        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
