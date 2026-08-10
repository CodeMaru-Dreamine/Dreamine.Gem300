using Dreamine.Gem.Abstractions.Model;
using Dreamine.Gem.Services;
using Dreamine.Gem300;
using Dreamine.Gem300.Abstractions.Model;
using Dreamine.Gem300.Abstractions.States;
using Dreamine.Gem300.Carrier;
using Dreamine.Gem300.Infrastructure;
using Dreamine.Gem300.Jobs;
using Dreamine.Gem300.Substrate;

var events = new Gem300EventJournal();
var carriers = new CarrierManager(events);
var substrates = new SubstrateTracker(events);
var programs = new GemProcessProgramService();
var processJobs = new ProcessJobManager(substrates, programs, events);
var controlJobs = new ControlJobManager(processJobs, events);
var workflow = new Gem300WorkflowCoordinator(carriers, substrates, processJobs, controlJobs);

carriers.RegisterLoadPort("PORT-1");
carriers.SetInService("PORT-1");
programs.Put(new GemProcessProgram("RECIPE-1", [0x01]));

workflow.AcceptCarrier(new CarrierArrivalPlan("PORT-1", "CARRIER-1",
    [CarrierSlotState.CorrectlyOccupied],
    [new SubstrateArrivalPlan("SUBSTRATE-1", "PORT-1:SLOT-1", "OUTPUT-1")]));
processJobs.Create(new ProcessJobDefinition("PJ-1", "RECIPE-1", ["SUBSTRATE-1"]));
controlJobs.Create(new ControlJobDefinition("CJ-1", ["PJ-1"]));

await workflow.ExecuteControlJobAsync("CJ-1", (job, _) =>
{
    foreach (var materialId in job.MaterialIds) substrates.Move(materialId, "OUTPUT-1");
    return ValueTask.CompletedTask;
});
workflow.ReleaseCarrier("CARRIER-1");

Console.WriteLine($"Workflow completed with {events.GetSnapshot().Count} domain event(s).");
