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
using Dreamine.Secs.Abstractions.Model;
using Xunit;

namespace Dreamine.Gem300.Tests;

public sealed class ClosureRemediationTests
{
    [Fact]
    public void AcceptCarrierClockFailureLeavesNoPartialCarrierOrSubstrates()
    {
        var time = new ArmableThrowingTimeProvider();
        var programs = new FakeProcessPrograms(); programs.Put(new("R1", [1]));
        var runtime = new Gem300Runtime(new FakeGemRuntime(), programs, time);
        runtime.Carriers.RegisterLoadPort("P1"); runtime.Carriers.SetInService("P1");
        time.Arm(1);

        Assert.Throws<InvalidOperationException>(() => runtime.Workflow.AcceptCarrier(new("P1", "C1",
            [CarrierSlotState.CorrectlyOccupied, CarrierSlotState.CorrectlyOccupied],
            [new("S1", "SRC1", "DST1"), new("S2", "SRC2", "DST2")],
            [new(0, "S1"), new(1, "S2")])));

        Assert.Throws<KeyNotFoundException>(() => runtime.Carriers.GetCarrier("C1"));
        Assert.Equal(CarrierAssociationState.NotAssociated, runtime.Carriers.GetLoadPort("P1").AssociationState);
        Assert.False(runtime.Substrates.TryGet("S1", out _));
        Assert.False(runtime.Substrates.TryGet("S2", out _));
        Assert.Empty(runtime.Workflow.GetCoordinatedCarrierIds());
    }

    [Fact]
    public void EmptyCarrierPlanDoesNotStrandACommittedCarrier()
    {
        var runtime = CreateRuntime();
        runtime.Carriers.RegisterLoadPort("P1"); runtime.Carriers.SetInService("P1");

        runtime.Workflow.AcceptCarrier(new("P1", "C1", [CarrierSlotState.Empty], []));

        Assert.Equal(CarrierAccessingStatus.InAccess, runtime.Carriers.GetCarrier("C1").AccessingStatus);
        runtime.Workflow.ReleaseCarrier("C1");
        Assert.Equal(CarrierAssociationState.NotAssociated, runtime.Carriers.GetLoadPort("P1").AssociationState);
    }

    [Fact]
    public void CoordinatedCarrierRejectsDirectUnloadStateChanges()
    {
        var runtime = CreateRuntime(); PrepareArrival(runtime, "S1");

        Assert.Throws<InvalidOperationException>(() => runtime.Carriers.CompleteAccess("C1"));

        Assert.Equal(CarrierAccessingStatus.InAccess, runtime.Carriers.GetCarrier("C1").AccessingStatus);
        Assert.Equal(["Carrier>C1"], runtime.Substrates.GetLeaseOwners("S1"));
        Assert.Equal(["C1"], runtime.Workflow.GetCoordinatedCarrierIds());
    }

    [Fact]
    public async Task ReentrantControlMutationDoesNotProduceAggregateCleanupFailure()
    {
        var runtime = CreateRuntime();
        runtime.Carriers.RegisterLoadPort("P1"); runtime.Carriers.SetInService("P1");
        runtime.Workflow.AcceptCarrier(new("P1", "C1",
            [CarrierSlotState.CorrectlyOccupied, CarrierSlotState.CorrectlyOccupied],
            [new("S1", "SRC1", "DST1"), new("S2", "SRC2", "DST2")],
            [new(0, "S1"), new(1, "S2")]));
        runtime.ProcessJobs.Create(new("PJ1", "R1", ["S1"]));
        runtime.ProcessJobs.Create(new("PJ2", "R1", ["S2"]));
        runtime.ControlJobs.Create(new("CJ1", ["PJ1", "PJ2"]));

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.Workflow.ExecuteControlJobAsync("CJ1", (process, _) =>
        {
            Assert.Equal("PJ1", process.Id);
            runtime.ProcessJobs.Complete("PJ1");
            runtime.ControlJobs.Pause("CJ1");
            return ValueTask.CompletedTask;
        }));

        Assert.IsNotType<AggregateException>(failure);
        Assert.Equal(ProcessJobState.ProcessComplete, runtime.ProcessJobs.Get("PJ1").State);
        Assert.Equal(ProcessJobState.Queued, runtime.ProcessJobs.Get("PJ2").State);
        Assert.Equal(ControlJobState.Completed, runtime.ControlJobs.Get("CJ1").State);
        Assert.DoesNotContain(runtime.Substrates.GetSnapshot(), static item => item.ProcessingState == SubstrateProcessingState.InProcess);
    }

    [Fact]
    public void ObjectRegisterEventCanReenterRemoveWithoutWaitingForAnInternalLock()
    {
        var journal = new ReentrantJournal();
        var service = new Gem300ObjectService(journal);
        var key = new Gem300ObjectKey("ApplicationObject", "1");
        journal.Callback = () => service.Remove(key);

        service.Register(key, []);

        Assert.True(journal.CallbackCompletedSynchronously);
        Assert.False(service.TryGetAttribute(key, "ObjID", out _));
    }

    [Fact]
    public async Task ObjectRemovalCancellationCallbackRunsAfterGenerationDetachAndOutsideLocks()
    {
        var service = new Gem300ObjectService(new Gem300EventJournal());
        var key = new Gem300ObjectKey("ApplicationObject", "1");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var never = new TaskCompletionSource<GemCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackCompletedSynchronously = false;
        service.Register(key, []);
        service.RegisterAction(key, "Wait", (_, token) =>
        {
            token.Register(() =>
            {
                var replacement = Task.Run(() => service.Register(key, []));
                callbackCompletedSynchronously = replacement.Wait(TimeSpan.FromSeconds(1));
            });
            entered.SetResult();
            return new(never.Task);
        });
        var execution = service.ExecuteActionAsync(key, "Wait", new Dictionary<string, Dreamine.Secs.Abstractions.Model.SecsItem>(), TimeSpan.FromSeconds(5)).AsTask();
        await entered.Task;

        Assert.True(service.Remove(key));

        Assert.True(callbackCompletedSynchronously);
        Assert.Equal(GemCommandStatus.Failed, (await execution).Status);
        Assert.True(service.TryGetAttribute(key, "ObjID", out _));
    }

    [Fact]
    public void SubstrateTimeProviderRunsOutsideTrackerLocks()
    {
        var time = new ReentrantTimeProvider();
        var tracker = new SubstrateTracker(new Gem300EventJournal(), time);
        time.Callback = () => tracker.GetSnapshot();

        tracker.Register("S1", "SRC", "DST");

        Assert.True(time.FirstCallbackCompletedSynchronously);
    }

    [Fact]
    public void JournalTimeProviderRunsOutsideJournalLock()
    {
        var time = new ReentrantTimeProvider();
        var journal = new Gem300EventJournal(time);
        time.Callback = () => journal.GetSnapshot();

        journal.Record(Gem300EventKind.CarrierChanged, "C1");

        Assert.True(time.FirstCallbackCompletedSynchronously);
    }

    [Fact]
    public void WorkflowEventsRunAfterCoordinatorAndSharedDomainLocksAreReleased()
    {
        var time = new ReentrantTimeProvider(); var programs = new FakeProcessPrograms(); programs.Put(new("R1", [1]));
        var runtime = new Gem300Runtime(new FakeGemRuntime(), programs, time);
        runtime.Carriers.RegisterLoadPort("P1"); runtime.Carriers.SetInService("P1");
        time.Arm(() => runtime.Workflow.GetCoordinatedCarrierIds());

        runtime.Workflow.AcceptCarrier(new("P1", "C1", [CarrierSlotState.Empty], []));

        Assert.True(time.FirstCallbackCompletedSynchronously);
        Assert.Equal(["C1"], runtime.Workflow.GetCoordinatedCarrierIds());
    }

    [Fact]
    public async Task ControlJobExecutionClaimIsSharedAcrossCoordinatorInstances()
    {
        var runtime = CreateRuntime(); PrepareArrival(runtime, "S1");
        runtime.ProcessJobs.Create(new("PJ1", "R1", ["S1"])); runtime.ControlJobs.Create(new("CJ1", ["PJ1"]));
        var second = new Gem300WorkflowCoordinator(runtime.Carriers, runtime.Substrates, runtime.ProcessJobs, runtime.ControlJobs);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = runtime.Workflow.ExecuteControlJobAsync("CJ1", async (_, _) => { entered.SetResult(); await release.Task; });
        await entered.Task;

        await Assert.ThrowsAsync<InvalidOperationException>(() => second.ExecuteControlJobAsync("CJ1", static (_, _) => ValueTask.CompletedTask));

        release.SetResult(); await first;
        Assert.Equal(ControlJobState.Completed, runtime.ControlJobs.Get("CJ1").State);
    }

    [Fact]
    public void ProcessJobRejectsProgramReturnedUnderTheWrongIdentity()
    {
        var events = new Gem300EventJournal();
        var substrates = new SubstrateTracker(events); substrates.Register("S1", "SRC", "DST");
        var manager = new ProcessJobManager(substrates, new WrongIdentityPrograms(), events);

        Assert.Throws<InvalidOperationException>(() => manager.Create(new("PJ1", "R1", ["S1"])));

        Assert.Throws<KeyNotFoundException>(() => manager.Get("PJ1"));
        Assert.Empty(substrates.GetLeaseOwners("S1"));
    }

    [Fact]
    public void AmbiguousSlotOrderingIsRejectedBeforeAnyCarrierMutation()
    {
        var runtime = CreateRuntime(); runtime.Carriers.RegisterLoadPort("P1"); runtime.Carriers.SetInService("P1");
        var ambiguous = new CarrierArrivalPlan("P1", "C1", [CarrierSlotState.CorrectlyOccupied], [new("S1", "SRC", "DST")]);

        Assert.Throws<InvalidOperationException>(() => runtime.Workflow.AcceptCarrier(ambiguous));

        Assert.Equal(CarrierAssociationState.NotAssociated, runtime.Carriers.GetLoadPort("P1").AssociationState);
        Assert.False(runtime.Substrates.TryGet("S1", out _));
    }

    [Fact]
    public void ExplicitSlotAssignmentsAreValidatedAndRemainQueryable()
    {
        Assert.Throws<ArgumentException>(() => new CarrierArrivalPlan("P1", "C1",
            [CarrierSlotState.Empty, CarrierSlotState.CorrectlyOccupied], [new("S1", "SRC", "DST")], [new(0, "S1")]));
        var runtime = CreateRuntime(); runtime.Carriers.RegisterLoadPort("P1"); runtime.Carriers.SetInService("P1");

        runtime.Workflow.AcceptCarrier(new("P1", "C1", [CarrierSlotState.Empty, CarrierSlotState.CorrectlyOccupied],
            [new("S1", "SRC", "DST")], [new(1, "S1")]));

        var assignment = Assert.Single(runtime.Workflow.GetCoordinatedSlotAssignments("C1"));
        Assert.Equal(1, assignment.SlotIndex); Assert.Equal("S1", assignment.SubstrateId);
    }

    [Fact]
    public async Task ApplicationDeclaredProjectionReservesKeyAndRoutesTypedManagerCommand()
    {
        var runtime = CreateRuntime(); var key = new Gem300ObjectKey("CustomerPortProjection", "P1");
        runtime.Objects.RegisterProjection(key, () =>
        {
            var port = runtime.Carriers.GetLoadPort("P1");
            return new Dictionary<string, SecsItem> { ["AccessMode"] = new SecsAsciiItem(port.AccessMode.ToString()) };
        });
        Assert.Throws<InvalidOperationException>(() => runtime.Objects.Register(key, [new("AccessMode", new SecsAsciiItem("raw"), true)]));
        runtime.Carriers.RegisterLoadPort("P1");
        runtime.Objects.RegisterAction(key, "SetManual", (_, _) =>
        {
            runtime.Carriers.ChangeAccessMode("P1", LoadPortAccessMode.Manual);
            return ValueTask.FromResult(new GemCommandResult(GemCommandStatus.Completed));
        });

        Assert.False(runtime.Objects.TrySetAttribute(key, "AccessMode", new SecsAsciiItem("raw")));
        var result = await runtime.Objects.ExecuteActionAsync(key, "SetManual", new Dictionary<string, SecsItem>(), TimeSpan.FromSeconds(1));

        Assert.Equal(GemCommandStatus.Completed, result.Status);
        Assert.Equal(LoadPortAccessMode.Manual, runtime.Carriers.GetLoadPort("P1").AccessMode);
        Assert.Equal("Manual", Assert.IsType<SecsAsciiItem>(runtime.Objects.GetAttributes(key)["AccessMode"]).Value);
        Assert.Throws<InvalidOperationException>(() => runtime.Objects.Remove(key));
        Assert.True(runtime.Objects.UnregisterProjection(key));
    }

    [Fact]
    public void ReferenceLeasesCanBeSharedWhileSubstrateProcessingStateRemainsExclusive()
    {
        var events = new Gem300EventJournal(); var substrates = new SubstrateTracker(events); substrates.Register("S1", "SRC", "DST");
        var programs = new FakeProcessPrograms(); programs.Put(new("R1", [1])); var jobs = new ProcessJobManager(substrates, programs, events);
        jobs.Create(new("PJ1", "R1", ["S1"]));

        jobs.Create(new("PJ2", "R1", ["S1"]));
        substrates.BeginProcessing("S1");

        Assert.Throws<InvalidOperationException>(() => substrates.BeginProcessing("S1"));
        Assert.Equal(["ProcessJob>PJ1", "ProcessJob>PJ2"], substrates.GetLeaseOwners("S1"));
    }

    private static Gem300Runtime CreateRuntime()
    {
        var programs = new FakeProcessPrograms(); programs.Put(new("R1", [1]));
        return new(new FakeGemRuntime(), programs);
    }

    private static void PrepareArrival(Gem300Runtime runtime, string substrateId)
    {
        runtime.Carriers.RegisterLoadPort("P1"); runtime.Carriers.SetInService("P1");
        runtime.Workflow.AcceptCarrier(new("P1", "C1", [CarrierSlotState.CorrectlyOccupied],
            [new(substrateId, "SRC", "DST")], [new CarrierSubstrateSlotAssignment(0, substrateId)]));
    }

    private sealed class WrongIdentityPrograms : IGemProcessProgramService
    {
        public void Put(GemProcessProgram program) { }
        public bool TryGet(string id, out GemProcessProgram? program) { program = new("WRONG", [1]); return true; }
        public bool Delete(string id) => false;
        public IReadOnlyList<string> GetIds() => [];
    }

    private sealed class ArmableThrowingTimeProvider : TimeProvider
    {
        private int _remaining = int.MaxValue;
        public void Arm(int successfulCallsBeforeFailure) => _remaining = successfulCallsBeforeFailure;
        public override DateTimeOffset GetUtcNow()
        {
            if (Interlocked.Decrement(ref _remaining) < 0) throw new InvalidOperationException("clock failure");
            return DateTimeOffset.UnixEpoch;
        }
    }

    private sealed class ReentrantTimeProvider : TimeProvider
    {
        private int _invoked;
        public Func<object?>? Callback { get; set; }
        public bool FirstCallbackCompletedSynchronously { get; private set; }
        public void Arm(Func<object?> callback) { Callback = callback ?? throw new ArgumentNullException(nameof(callback)); Volatile.Write(ref _invoked, 0); FirstCallbackCompletedSynchronously = false; }
        public override DateTimeOffset GetUtcNow()
        {
            if (Callback is not null && Interlocked.Exchange(ref _invoked, 1) == 0)
            {
                var callback = Task.Run(Callback);
                FirstCallbackCompletedSynchronously = callback.Wait(TimeSpan.FromSeconds(1));
            }
            return DateTimeOffset.UnixEpoch;
        }
    }

    private sealed class ReentrantJournal : IGem300EventJournal
    {
        private long _sequence;
        public Func<object?>? Callback { get; set; }
        public bool CallbackCompletedSynchronously { get; private set; }
        public Gem300DomainEvent Record(Gem300EventKind kind, string aggregateId)
        {
            if (Callback is not null)
            {
                var callback = Task.Run(Callback);
                CallbackCompletedSynchronously = callback.Wait(TimeSpan.FromSeconds(1));
            }
            return new(Interlocked.Increment(ref _sequence), kind, aggregateId, DateTimeOffset.UnixEpoch);
        }
        public IReadOnlyList<Gem300DomainEvent> GetSnapshot() => [];
    }
}
