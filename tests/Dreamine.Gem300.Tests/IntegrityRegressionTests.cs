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

public sealed class IntegrityRegressionTests
{
    [Fact]
    public void ProcessJobOwnershipIsSharedAcrossControlJobManagers()
    {
        var process = CreateProcessManager(out _, "S1");
        process.Create(new("PJ1", "R1", ["S1"]));
        var first = new ControlJobManager(process, new Gem300EventJournal());
        var second = new ControlJobManager(process, new Gem300EventJournal());

        first.Create(new("CJ1", ["PJ1"]));

        Assert.Throws<InvalidOperationException>(() => second.Create(new("CJ2", ["PJ1"])));
    }

    [Fact]
    public void ClaimedProcessJobCannotBeDeleted()
    {
        var process = CreateProcessManager(out _, "S1");
        process.Create(new("PJ1", "R1", ["S1"]));
        var controls = new ControlJobManager(process, new Gem300EventJournal());
        controls.Create(new("CJ1", ["PJ1"]));

        Assert.Throws<InvalidOperationException>(() => process.Delete("PJ1"));
        Assert.Equal(ProcessJobState.Queued, process.Get("PJ1").State);
    }

    [Fact]
    public void AbortedControlJobCannotReleaseAnActiveProcessJob()
    {
        var process = CreateProcessManager(out _, "S1");
        process.Create(new("PJ1", "R1", ["S1"]));
        var controls = new ControlJobManager(process, new Gem300EventJournal());
        controls.Create(new("CJ1", ["PJ1"]));
        controls.Select("CJ1");
        controls.Ready("CJ1");
        process.Allocate("PJ1");
        process.CompleteSetup("PJ1");
        controls.Abort("CJ1");

        Assert.Throws<InvalidOperationException>(() => controls.Delete("CJ1"));
        Assert.Throws<InvalidOperationException>(() =>
            new ControlJobManager(process, new Gem300EventJournal()).Create(new("CJ2", ["PJ1"])));
    }

    [Fact]
    public void ProcessJobRetainsItsReferencedSubstrateUntilDeletion()
    {
        var process = CreateProcessManager(out var substrates, "S1");
        process.Create(new("PJ1", "R1", ["S1"]));
        substrates.BeginProcessing("S1");
        substrates.CompleteProcessing("S1", SubstrateProcessingState.Processed);
        substrates.Move("S1", "DST-S1");

        Assert.Throws<InvalidOperationException>(() => substrates.Remove("S1"));

        process.Delete("PJ1");
        substrates.Remove("S1");
        Assert.False(substrates.TryGet("S1", out _));
    }

    [Fact]
    public void CarrierWorkflowRetainsSubstratesUntilRelease()
    {
        var runtime = CreateRuntime();
        runtime.Carriers.RegisterLoadPort("P1");
        runtime.Carriers.SetInService("P1");
        runtime.Workflow.AcceptCarrier(new("P1", "C1", [CarrierSlotState.CorrectlyOccupied],
            [new SubstrateArrivalPlan("S1", "SRC", "DST")], [new CarrierSubstrateSlotAssignment(0, "S1")]));
        runtime.Substrates.BeginProcessing("S1");
        runtime.Substrates.CompleteProcessing("S1", SubstrateProcessingState.Processed);
        runtime.Substrates.Move("S1", "DST");

        Assert.Throws<InvalidOperationException>(() => runtime.Substrates.Remove("S1"));
        runtime.Workflow.ReleaseCarrier("C1");
        Assert.False(runtime.Substrates.TryGet("S1", out _));
    }

    [Fact]
    public async Task CancellationAfterProcessorReturnCannotCommitSuccess()
    {
        var runtime = CreateRuntime();
        runtime.Carriers.RegisterLoadPort("P1");
        runtime.Carriers.SetInService("P1");
        runtime.Workflow.AcceptCarrier(new("P1", "C1", [CarrierSlotState.CorrectlyOccupied],
            [new SubstrateArrivalPlan("S1", "SRC", "DST")], [new CarrierSubstrateSlotAssignment(0, "S1")]));
        runtime.ProcessJobs.Create(new("PJ1", "R1", ["S1"]));
        runtime.ControlJobs.Create(new("CJ1", ["PJ1"]));
        using var cancellation = new CancellationTokenSource();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runtime.Workflow.ExecuteControlJobAsync(
            "CJ1",
            (_, _) =>
            {
                cancellation.Cancel();
                return ValueTask.CompletedTask;
            },
            cancellation.Token));

        Assert.Equal(ProcessJobState.Aborted, runtime.ProcessJobs.Get("PJ1").State);
        Assert.Equal(SubstrateProcessingState.Aborted, runtime.Substrates.Get("S1").ProcessingState);
    }

    [Fact]
    public void WritableObjectAttributeRejectsAnIncompatibleSecsFormat()
    {
        var service = new Gem300ObjectService(new Gem300EventJournal());
        var key = new Gem300ObjectKey("Carrier", "C1");
        service.Register(key, [new Gem300AttributeDefinition("Usage", new SecsAsciiItem("PRODUCT"), true)]);

        Assert.False(service.TrySetAttribute(key, "Usage", new SecsUInt16Item(1)));
        Assert.Equal("PRODUCT", Assert.IsType<SecsAsciiItem>(service.GetAttributes(key)["Usage"]).Value);
    }

    [Fact]
    public async Task ObjectActionTimeoutCancelsTheHandlerToken()
    {
        var time = new ManualTimeProvider();
        var service = new Gem300ObjectService(new Gem300EventJournal(time), time);
        var key = new Gem300ObjectKey("Process", "1");
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Register(key, []);
        service.RegisterAction(key, "Wait", async (_, token) =>
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                canceled.TrySetResult();
                throw;
            }
            return new GemCommandResult(GemCommandStatus.Completed);
        });

        var execution = service.ExecuteActionAsync(key, "Wait", new Dictionary<string, SecsItem>(), TimeSpan.FromSeconds(1)).AsTask();
        time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(GemCommandStatus.Failed, (await execution).Status);
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RemovedObjectActionCannotReportSuccessForReplacementGeneration()
    {
        var journal = new Gem300EventJournal();
        var service = new Gem300ObjectService(journal);
        var key = new Gem300ObjectKey("Carrier", "C1");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        service.Register(key, []);
        service.RegisterAction(key, "Assign", async (_, _) =>
        {
            entered.SetResult();
            await release.Task;
            return new GemCommandResult(GemCommandStatus.Completed);
        });
        var execution = service.ExecuteActionAsync(key, "Assign", new Dictionary<string, SecsItem>(), TimeSpan.FromSeconds(5)).AsTask();
        await entered.Task;
        Assert.True(service.Remove(key));
        service.Register(key, []);
        var eventCountForReplacement = journal.GetSnapshot().Count;
        release.SetResult();

        Assert.NotEqual(GemCommandStatus.Completed, (await execution).Status);
        Assert.Equal(eventCountForReplacement, journal.GetSnapshot().Count);
    }

    [Fact]
    public void ThrowingEventJournalDoesNotTurnCommittedMutationIntoFailure()
    {
        var tracker = new SubstrateTracker(new ThrowingEventJournal());

        tracker.Register("S1", "SRC", "DST");

        Assert.Equal("S1", tracker.Get("S1").Id);
    }

    private static ProcessJobManager CreateProcessManager(out SubstrateTracker substrates, params string[] substrateIds)
    {
        var events = new Gem300EventJournal();
        substrates = new(events);
        foreach (var id in substrateIds) substrates.Register(id, $"SRC-{id}", $"DST-{id}");
        var programs = new FakeProcessPrograms();
        programs.Put(new GemProcessProgram("R1", [1]));
        return new(substrates, programs, events);
    }

    private static Gem300Runtime CreateRuntime()
    {
        var programs = new FakeProcessPrograms();
        programs.Put(new GemProcessProgram("R1", [1]));
        return new(new FakeGemRuntime(), programs);
    }

    private sealed class ThrowingEventJournal : IGem300EventJournal
    {
        public Gem300DomainEvent Record(Gem300EventKind kind, string aggregateId) =>
            throw new InvalidOperationException("journal unavailable");

        public IReadOnlyList<Gem300DomainEvent> GetSnapshot() => [];
    }
}
