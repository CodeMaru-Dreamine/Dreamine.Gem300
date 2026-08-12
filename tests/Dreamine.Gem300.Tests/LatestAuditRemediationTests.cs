using Dreamine.Gem.Abstractions.Model;
using Dreamine.Gem300.Abstractions.Interfaces;
using Dreamine.Gem300.Abstractions.Model;
using Dreamine.Gem300.Abstractions.States;
using Dreamine.Gem300.Infrastructure;
using Dreamine.Gem300.Jobs;
using Dreamine.Gem300.Substrate;
using Xunit;

namespace Dreamine.Gem300.Tests;

public sealed class LatestAuditRemediationTests
{
    [Fact]
    public async Task ProcessorStoppedJobDoesNotPromoteSubstrateOrControlJobToSuccess()
    {
        var runtime = CreateRuntime(); PrepareFlow(runtime);

        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.Workflow.ExecuteControlJobAsync("CJ1", (job, _) =>
        {
            runtime.ProcessJobs.Stop(job.Id); runtime.ProcessJobs.ConfirmStopped(job.Id);
            return ValueTask.CompletedTask;
        }));

        Assert.Equal(ProcessJobState.Stopped, runtime.ProcessJobs.Get("PJ1").State);
        Assert.Equal(SubstrateProcessingState.Aborted, runtime.Substrates.Get("S1").ProcessingState);
        Assert.Equal(ControlJobState.Completed, runtime.ControlJobs.Get("CJ1").State);
    }

    [Fact]
    public async Task ProcessorAbortedJobDoesNotPromoteSubstrateOrControlJobToSuccess()
    {
        var runtime = CreateRuntime(); PrepareFlow(runtime);

        await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.Workflow.ExecuteControlJobAsync("CJ1", (job, _) =>
        {
            runtime.ProcessJobs.Abort(job.Id); runtime.ProcessJobs.ConfirmAborted(job.Id);
            return ValueTask.CompletedTask;
        }));

        Assert.Equal(ProcessJobState.Aborted, runtime.ProcessJobs.Get("PJ1").State);
        Assert.Equal(SubstrateProcessingState.Aborted, runtime.Substrates.Get("S1").ProcessingState);
        Assert.Equal(ControlJobState.Completed, runtime.ControlJobs.Get("CJ1").State);
    }

    [Fact]
    public void CoordinatorRejectsProcessManagerBoundToAnotherSubstrateStore()
    {
        var runtime = CreateRuntime(); var otherEvents = new Gem300EventJournal(); var otherSubstrates = new SubstrateTracker(otherEvents);
        var programs = new FakeProcessPrograms(); programs.Put(new("R1", [1])); var otherProcesses = new ProcessJobManager(otherSubstrates, programs, otherEvents);
        var otherControls = new ControlJobManager(otherProcesses, otherEvents);

        Assert.Throws<InvalidOperationException>(() => new Gem300WorkflowCoordinator(runtime.Carriers, runtime.Substrates, otherProcesses, otherControls));
    }

    [Fact]
    public void CoordinatorRejectsControlManagerBoundToAnotherProcessStore()
    {
        var runtime = CreateRuntime(); var otherEvents = new Gem300EventJournal(); var otherSubstrates = new SubstrateTracker(otherEvents);
        var programs = new FakeProcessPrograms(); programs.Put(new("R1", [1])); var otherProcesses = new ProcessJobManager(otherSubstrates, programs, otherEvents);
        var otherControls = new ControlJobManager(otherProcesses, otherEvents);

        Assert.Throws<InvalidOperationException>(() => new Gem300WorkflowCoordinator(runtime.Carriers, runtime.Substrates, runtime.ProcessJobs, otherControls));
    }

    [Fact]
    public void ProcessJobManagerFailsFastForExternalSubstrateImplementation()
    {
        var programs = new FakeProcessPrograms(); programs.Put(new("R1", [1]));

        Assert.Throws<NotSupportedException>(() => new ProcessJobManager(new ExternalSubstrateTracker(), programs, new Gem300EventJournal()));
    }

    [Fact]
    public void ControlJobManagerFailsFastForExternalProcessImplementation()
    {
        Assert.Throws<NotSupportedException>(() => new ControlJobManager(new ExternalProcessJobManager(), new Gem300EventJournal()));
    }

    private static Gem300Runtime CreateRuntime()
    {
        var programs = new FakeProcessPrograms(); programs.Put(new("R1", [1]));
        return new(new FakeGemRuntime(), programs);
    }

    private static void PrepareFlow(Gem300Runtime runtime)
    {
        runtime.Carriers.RegisterLoadPort("P1"); runtime.Carriers.SetInService("P1");
        runtime.Workflow.AcceptCarrier(new("P1", "C1", [CarrierSlotState.CorrectlyOccupied],
            [new("S1", "SRC", "DST")], [new CarrierSubstrateSlotAssignment(0, "S1")]));
        runtime.ProcessJobs.Create(new("PJ1", "R1", ["S1"])); runtime.ControlJobs.Create(new("CJ1", ["PJ1"]));
    }

    private sealed class ExternalSubstrateTracker : ISubstrateTracker
    {
        public void Register(string substrateId, string sourceLocation, string destinationLocation, bool idConfirmed = true) => throw new NotSupportedException();
        public void ConfirmId(string substrateId) => throw new NotSupportedException();
        public void RejectId(string substrateId) => throw new NotSupportedException();
        public void Move(string substrateId, string locationId) => throw new NotSupportedException();
        public void BeginProcessing(string substrateId) => throw new NotSupportedException();
        public void CompleteProcessing(string substrateId, SubstrateProcessingState result) => throw new NotSupportedException();
        public void MarkLost(string substrateId) => throw new NotSupportedException();
        public void Remove(string substrateId) => throw new NotSupportedException();
        public SubstrateSnapshot Get(string substrateId) => throw new NotSupportedException();
        public bool TryGet(string substrateId, out SubstrateSnapshot? substrate) { substrate = null; return false; }
        public MaterialLocationState GetLocationState(string locationId) => MaterialLocationState.Unoccupied;
    }

    private sealed class ExternalProcessJobManager : IProcessJobManager
    {
        public void Create(ProcessJobDefinition definition) => throw new NotSupportedException();
        public void Allocate(string id) => throw new NotSupportedException();
        public void CompleteSetup(string id) => throw new NotSupportedException();
        public void Start(string id) => throw new NotSupportedException();
        public void Pause(string id) => throw new NotSupportedException();
        public void ConfirmPaused(string id) => throw new NotSupportedException();
        public void Resume(string id) => throw new NotSupportedException();
        public void Stop(string id) => throw new NotSupportedException();
        public void ConfirmStopped(string id) => throw new NotSupportedException();
        public void Abort(string id) => throw new NotSupportedException();
        public void ConfirmAborted(string id) => throw new NotSupportedException();
        public void Complete(string id) => throw new NotSupportedException();
        public void Delete(string id) => throw new NotSupportedException();
        public ProcessJobSnapshot Get(string id) => throw new NotSupportedException();
    }
}
