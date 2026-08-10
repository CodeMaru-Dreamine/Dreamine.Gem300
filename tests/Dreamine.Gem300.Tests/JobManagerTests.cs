using Dreamine.Gem.Abstractions.Model;
using Dreamine.Gem300.Abstractions.Model;
using Dreamine.Gem300.Abstractions.States;
using Dreamine.Gem300.Infrastructure;
using Dreamine.Gem300.Jobs;
using Dreamine.Gem300.Substrate;
using Xunit;

namespace Dreamine.Gem300.Tests;

public sealed class JobManagerTests
{
    [Fact]
    public void AutomaticProcessJobTraversesPauseAndComplete()
    {
        var manager = CreateProcessManager(out _, out _); manager.Create(new("P1", "R1", new[] { "S1" })); manager.Allocate("P1"); manager.CompleteSetup("P1"); manager.Pause("P1"); manager.ConfirmPaused("P1"); manager.Resume("P1"); manager.Complete("P1");
        Assert.Equal(ProcessJobState.ProcessComplete, manager.Get("P1").State); manager.Delete("P1"); Assert.Throws<KeyNotFoundException>(() => manager.Get("P1"));
    }

    [Fact]
    public void ManualProcessJobWaitsForStartAndCanAbort()
    {
        var manager = CreateProcessManager(out _, out _); manager.Create(new("P1", "R1", new[] { "S1" }, true)); manager.Allocate("P1"); manager.CompleteSetup("P1");
        Assert.Equal(ProcessJobState.WaitingForStart, manager.Get("P1").State); manager.Start("P1"); manager.Abort("P1"); manager.ConfirmAborted("P1"); Assert.Equal(ProcessJobState.Aborted, manager.Get("P1").State);
    }

    [Fact]
    public void ProcessJobRejectsMissingRecipeOrMaterial()
    {
        var manager = CreateProcessManager(out var substrates, out _);
        Assert.Throws<InvalidOperationException>(() => manager.Create(new("P0", "MISSING", new[] { "S1" })));
        Assert.Throws<KeyNotFoundException>(() => manager.Create(new("P2", "R1", new[] { "S2" })));
        Assert.Equal(SubstrateProcessingState.NeedsProcessing, substrates.Get("S1").ProcessingState);
    }

    [Fact]
    public void StopAndAbortHaveDistinctPostActiveStates()
    {
        var manager = CreateProcessManager(out _, out _); manager.Create(new("P1", "R1", new[] { "S1" })); manager.Allocate("P1"); manager.CompleteSetup("P1"); manager.Stop("P1"); manager.ConfirmStopped("P1");
        Assert.Equal(ProcessJobState.Stopped, manager.Get("P1").State);
    }

    [Fact]
    public void ControlJobEnforcesQueueHeadAndOrderedProcessJobs()
    {
        var process = CreateProcessManager(out _, out _); CreateCompletedProcess(process, "P1"); CreateCompletedProcess(process, "P2"); CreateCompletedProcess(process, "P3");
        var controls = new ControlJobManager(process, new Gem300EventJournal()); controls.Create(new("C1", new[] { "P1", "P2" })); controls.Create(new("C2", new[] { "P3" }));
        Assert.Throws<InvalidOperationException>(() => controls.Select("C2")); controls.Select("C1"); controls.Ready("C1"); controls.Advance("C1"); controls.Complete("C1");
        Assert.Equal(ControlJobState.Completed, controls.Get("C1").State); controls.Delete("C1"); controls.Select("C2");
    }

    [Fact]
    public void ProcessJobCannotBelongToTwoControlJobs()
    {
        var process = CreateProcessManager(out _, out _); CreateCompletedProcess(process, "P1"); var controls = new ControlJobManager(process, new Gem300EventJournal()); controls.Create(new("C1", new[] { "P1" }));
        Assert.Throws<InvalidOperationException>(() => controls.Create(new("C2", new[] { "P1" })));
    }

    [Fact]
    public void ManualControlJobSupportsPauseResumeAndAbort()
    {
        var process = CreateProcessManager(out _, out _); CreateCompletedProcess(process, "P1"); var controls = new ControlJobManager(process, new Gem300EventJournal()); controls.Create(new("C1", new[] { "P1" }, true)); controls.Select("C1"); controls.Ready("C1");
        Assert.Equal(ControlJobState.WaitingForStart, controls.Get("C1").State); controls.Start("C1"); controls.Pause("C1"); controls.Resume("C1"); controls.Abort("C1"); Assert.Equal(ControlJobState.Completed, controls.Get("C1").State);
    }

    private static ProcessJobManager CreateProcessManager(out SubstrateTracker substrates, out FakeProcessPrograms programs)
    {
        var events = new Gem300EventJournal(); substrates = new(events); substrates.Register("S1", "SRC", "DST"); programs = new(); programs.Put(new GemProcessProgram("R1", new byte[] { 1 })); return new(substrates, programs, events);
    }
    private static void CreateCompletedProcess(ProcessJobManager manager, string id) { manager.Create(new(id, "R1", new[] { "S1" })); manager.Allocate(id); manager.CompleteSetup(id); manager.Complete(id); }
}
