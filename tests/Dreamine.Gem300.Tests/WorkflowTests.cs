using Dreamine.Gem.Abstractions.Model;
using Dreamine.Gem300.Abstractions.Model;
using Dreamine.Gem300.Abstractions.States;
using Xunit;

namespace Dreamine.Gem300.Tests;

public sealed class WorkflowTests
{
    [Fact]
    public async Task CarrierToProcessToRemovalFlowPreservesEventOrder()
    {
        var runtime = CreateRuntime(); PrepareArrival(runtime); PrepareJobs(runtime);
        await runtime.Workflow.ExecuteControlJobAsync("CJ1", (process, _) => { Assert.Equal("PJ1", process.Id); runtime.Substrates.Move("S1", "DST"); return ValueTask.CompletedTask; });
        Assert.Equal(ProcessJobState.ProcessComplete, runtime.ProcessJobs.Get("PJ1").State); Assert.Equal(ControlJobState.Completed, runtime.ControlJobs.Get("CJ1").State); Assert.Equal(SubstrateProcessingState.Processed, runtime.Substrates.Get("S1").ProcessingState);
        runtime.Workflow.ReleaseCarrier("C1");
        Assert.Equal(LoadPortTransferState.ReadyToLoad, runtime.Carriers.GetLoadPort("P1").TransferState); Assert.Throws<KeyNotFoundException>(() => runtime.Substrates.Get("S1"));
        var events = runtime.Events.GetSnapshot(); Assert.True(events.Zip(events.Skip(1), static (left, right) => left.Sequence < right.Sequence).All(static value => value));
    }

    [Fact]
    public async Task ProcessorFailureAbortsJobControlAndSubstrateWithoutSwallowingError()
    {
        var runtime = CreateRuntime(); PrepareArrival(runtime); PrepareJobs(runtime);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.Workflow.ExecuteControlJobAsync("CJ1", (_, _) => throw new InvalidOperationException("equipment failure")));
        Assert.Equal("equipment failure", exception.Message); Assert.Equal(ProcessJobState.Aborted, runtime.ProcessJobs.Get("PJ1").State); Assert.Equal(ControlJobState.Completed, runtime.ControlJobs.Get("CJ1").State); Assert.Equal(SubstrateProcessingState.Aborted, runtime.Substrates.Get("S1").ProcessingState);
        runtime.Substrates.Move("S1", "DST"); runtime.Workflow.ReleaseCarrier("C1");
    }

    [Fact]
    public async Task CancellationIsPropagatedAfterDeterministicAbortCleanup()
    {
        var runtime = CreateRuntime(); PrepareArrival(runtime); PrepareJobs(runtime); using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runtime.Workflow.ExecuteControlJobAsync("CJ1", async (_, token) => { cancellation.Cancel(); await Task.Delay(Timeout.InfiniteTimeSpan, token); }, cancellation.Token));
        Assert.Equal(ProcessJobState.Aborted, runtime.ProcessJobs.Get("PJ1").State); Assert.Equal(SubstrateProcessingState.Aborted, runtime.Substrates.Get("S1").ProcessingState);
    }

    [Fact]
    public void InvalidArrivalPlanDoesNotMutateCarrierState()
    {
        var runtime = CreateRuntime(); runtime.Carriers.RegisterLoadPort("P1"); runtime.Carriers.SetInService("P1");
        var plan = new CarrierArrivalPlan("P1", "C1", new[] { CarrierSlotState.CrossSlotted }, new[] { new SubstrateArrivalPlan("S1", "SRC", "DST") });
        Assert.Throws<InvalidOperationException>(() => runtime.Workflow.AcceptCarrier(plan)); Assert.Equal(CarrierAssociationState.NotAssociated, runtime.Carriers.GetLoadPort("P1").AssociationState);
    }

    [Fact]
    public void RuntimeRetainsProviderNeutralGemBoundary()
    {
        var programs = new FakeProcessPrograms(); programs.Put(new GemProcessProgram("R1", new byte[] { 1 })); var gem = new FakeGemRuntime(); var runtime = new Gem300Runtime(gem, programs);
        Assert.Same(gem, runtime.GemRuntime); Assert.NotNull(runtime.Objects); Assert.NotNull(runtime.Workflow);
    }

    private static Gem300Runtime CreateRuntime()
    {
        var programs = new FakeProcessPrograms(); programs.Put(new GemProcessProgram("R1", new byte[] { 1, 2 })); return new(new FakeGemRuntime(), programs);
    }
    private static void PrepareArrival(Gem300Runtime runtime)
    {
        runtime.Carriers.RegisterLoadPort("P1"); runtime.Carriers.SetInService("P1");
        runtime.Workflow.AcceptCarrier(new("P1", "C1", new[] { CarrierSlotState.CorrectlyOccupied }, new[] { new SubstrateArrivalPlan("S1", "SRC", "DST") }));
    }
    private static void PrepareJobs(Gem300Runtime runtime)
    {
        runtime.ProcessJobs.Create(new("PJ1", "R1", new[] { "S1" })); runtime.ControlJobs.Create(new("CJ1", new[] { "PJ1" }));
    }
}
