using Dreamine.Communication.Abstractions.Enums;
using Dreamine.Gem.Abstractions.Interfaces;
using Dreamine.Gem.Abstractions.Model;
using Dreamine.Gem.Services;
using Dreamine.Gem300;
using Dreamine.Gem300.Abstractions.Model;
using Dreamine.Gem300.Abstractions.States;
using Dreamine.Secs.Abstractions.Interfaces;

var programs = new GemProcessProgramService();
var runtime = new Gem300Runtime(new SampleGemRuntime(), programs);

runtime.Carriers.RegisterLoadPort("PORT-1");
runtime.Carriers.SetInService("PORT-1");
programs.Put(new GemProcessProgram("RECIPE-1", [0x01]));

runtime.Workflow.AcceptCarrier(new CarrierArrivalPlan("PORT-1", "CARRIER-1",
    [CarrierSlotState.CorrectlyOccupied],
    [new SubstrateArrivalPlan("SUBSTRATE-1", "PORT-1:SLOT-1", "OUTPUT-1")],
    [new CarrierSubstrateSlotAssignment(0, "SUBSTRATE-1")]));
runtime.ProcessJobs.Create(new ProcessJobDefinition("PJ-1", "RECIPE-1", ["SUBSTRATE-1"]));
runtime.ControlJobs.Create(new ControlJobDefinition("CJ-1", ["PJ-1"]));

await runtime.Workflow.ExecuteControlJobAsync("CJ-1", (job, _) =>
{
    foreach (var materialId in job.MaterialIds) runtime.Substrates.Move(materialId, "OUTPUT-1");
    return ValueTask.CompletedTask;
});
runtime.ControlJobs.Delete("CJ-1");
runtime.ProcessJobs.Delete("PJ-1");
runtime.Workflow.ReleaseCarrier("CARRIER-1");

Console.WriteLine($"Workflow completed with {runtime.Events.GetSnapshot().Count} domain event(s).");

file sealed class SampleGemRuntime : IGemRuntime
{
    public ISecsConnection SecsConnection { get; } = new SampleSecsConnection();
}

file sealed class SampleSecsConnection : ISecsConnection
{
    public string ProviderKey => "gem300-quickstart";
    public ConnectionState State => ConnectionState.Disconnected;
    public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
