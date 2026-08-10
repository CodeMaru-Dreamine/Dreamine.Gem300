# Public API Inventory

Assembly: `Dreamine.Gem300`

This inventory is generated from the compiled Release assembly. It is an audit artifact, not an additional compatibility promise.

Exported types: **9**

## Types

### `public sealed class Dreamine.Gem300.Carrier.CarrierManager`

- `CarrierManager(Dreamine.Gem300.Abstractions.Interfaces.IGem300EventJournal events)`
- `Dreamine.Gem300.Abstractions.Model.CarrierSnapshot GetCarrier(System.String carrierId)`
- `Dreamine.Gem300.Abstractions.Model.LoadPortSnapshot GetLoadPort(System.String portId)`
- `System.Void AcceptId(System.String carrierId)`
- `System.Void AcceptSlotMap(System.String carrierId)`
- `System.Void BeginAccess(System.String carrierId)`
- `System.Void BeginLoad(System.String portId)`
- `System.Void BeginUnload(System.String portId)`
- `System.Void Bind(System.String portId, System.String carrierId, System.Int32 capacity)`
- `System.Void CancelReservation(System.String portId)`
- `System.Void ChangeAccessMode(System.String portId, Dreamine.Gem300.Abstractions.States.LoadPortAccessMode accessMode)`
- `System.Void CompleteAccess(System.String carrierId)`
- `System.Void CompleteLoad(System.String portId)`
- `System.Void CompleteUnload(System.String portId)`
- `System.Void PrepareUnload(System.String carrierId)`
- `System.Void RegisterLoadPort(System.String portId, Dreamine.Gem300.Abstractions.States.LoadPortAccessMode accessMode)`
- `System.Void RejectId(System.String carrierId)`
- `System.Void RejectSlotMap(System.String carrierId)`
- `System.Void Reserve(System.String portId)`
- `System.Void SetInService(System.String portId)`
- `System.Void SetOutOfService(System.String portId)`
- `System.Void StopAccess(System.String carrierId)`
- `System.Void WaitForIdDecision(System.String carrierId)`
- `System.Void WaitForSlotMapDecision(System.String carrierId, System.Collections.Generic.IEnumerable<Dreamine.Gem300.Abstractions.States.CarrierSlotState> slotMap)`

### `public sealed class Dreamine.Gem300.Gem300AssemblyMarker`

- No declared public members.

### `public sealed class Dreamine.Gem300.Gem300Runtime`

- `Dreamine.Gem.Abstractions.Interfaces.IGemRuntime GemRuntime { get; }`
- `Dreamine.Gem300.Carrier.CarrierManager Carriers { get; }`
- `Dreamine.Gem300.Gem300WorkflowCoordinator Workflow { get; }`
- `Dreamine.Gem300.Infrastructure.Gem300EventJournal Events { get; }`
- `Dreamine.Gem300.Jobs.ControlJobManager ControlJobs { get; }`
- `Dreamine.Gem300.Jobs.ProcessJobManager ProcessJobs { get; }`
- `Dreamine.Gem300.ObjectServices.Gem300ObjectService Objects { get; }`
- `Dreamine.Gem300.Substrate.SubstrateTracker Substrates { get; }`
- `Gem300Runtime(Dreamine.Gem.Abstractions.Interfaces.IGemRuntime gemRuntime, Dreamine.Gem.Abstractions.Interfaces.IGemProcessProgramService processPrograms, System.TimeProvider timeProvider, System.Int32 eventCapacity)`

### `public sealed class Dreamine.Gem300.Gem300WorkflowCoordinator`

- `Gem300WorkflowCoordinator(Dreamine.Gem300.Abstractions.Interfaces.ICarrierManager carriers, Dreamine.Gem300.Abstractions.Interfaces.ISubstrateTracker substrates, Dreamine.Gem300.Abstractions.Interfaces.IProcessJobManager processJobs, Dreamine.Gem300.Abstractions.Interfaces.IControlJobManager controlJobs)`
- `System.Threading.Tasks.Task ExecuteControlJobAsync(System.String controlJobId, System.Func<Dreamine.Gem300.Abstractions.Model.ProcessJobDefinition, System.Threading.CancellationToken, System.Threading.Tasks.ValueTask> processor, System.Threading.CancellationToken cancellationToken)`
- `System.Void AcceptCarrier(Dreamine.Gem300.Abstractions.Model.CarrierArrivalPlan plan)`
- `System.Void ReleaseCarrier(System.String carrierId)`

### `public sealed class Dreamine.Gem300.Infrastructure.Gem300EventJournal`

- `Dreamine.Gem300.Abstractions.Model.Gem300DomainEvent Record(Dreamine.Gem300.Abstractions.States.Gem300EventKind kind, System.String aggregateId)`
- `Gem300EventJournal(System.TimeProvider timeProvider, System.Int32 capacity)`
- `System.Collections.Generic.IReadOnlyList<Dreamine.Gem300.Abstractions.Model.Gem300DomainEvent> GetSnapshot()`

### `public sealed class Dreamine.Gem300.Jobs.ControlJobManager`

- `ControlJobManager(Dreamine.Gem300.Abstractions.Interfaces.IProcessJobManager processJobs, Dreamine.Gem300.Abstractions.Interfaces.IGem300EventJournal events)`
- `Dreamine.Gem300.Abstractions.Model.ControlJobSnapshot Get(System.String id)`
- `System.Void Abort(System.String id)`
- `System.Void Advance(System.String id)`
- `System.Void Complete(System.String id)`
- `System.Void Create(Dreamine.Gem300.Abstractions.Model.ControlJobDefinition definition)`
- `System.Void Delete(System.String id)`
- `System.Void Pause(System.String id)`
- `System.Void Ready(System.String id)`
- `System.Void Resume(System.String id)`
- `System.Void Select(System.String id)`
- `System.Void Start(System.String id)`

### `public sealed class Dreamine.Gem300.Jobs.ProcessJobManager`

- `Dreamine.Gem300.Abstractions.Model.ProcessJobSnapshot Get(System.String id)`
- `ProcessJobManager(Dreamine.Gem300.Abstractions.Interfaces.ISubstrateTracker substrates, Dreamine.Gem.Abstractions.Interfaces.IGemProcessProgramService programs, Dreamine.Gem300.Abstractions.Interfaces.IGem300EventJournal events)`
- `System.Void Abort(System.String id)`
- `System.Void Allocate(System.String id)`
- `System.Void Complete(System.String id)`
- `System.Void CompleteSetup(System.String id)`
- `System.Void ConfirmAborted(System.String id)`
- `System.Void ConfirmPaused(System.String id)`
- `System.Void ConfirmStopped(System.String id)`
- `System.Void Create(Dreamine.Gem300.Abstractions.Model.ProcessJobDefinition definition)`
- `System.Void Delete(System.String id)`
- `System.Void Pause(System.String id)`
- `System.Void Resume(System.String id)`
- `System.Void Start(System.String id)`
- `System.Void Stop(System.String id)`

### `public sealed class Dreamine.Gem300.ObjectServices.Gem300ObjectService`

- `Gem300ObjectService(Dreamine.Gem300.Abstractions.Interfaces.IGem300EventJournal events, System.TimeProvider timeProvider)`
- `System.Boolean Remove(Dreamine.Gem300.Abstractions.Model.Gem300ObjectKey key)`
- `System.Boolean TryGetAttribute(Dreamine.Gem300.Abstractions.Model.Gem300ObjectKey key, System.String name, Dreamine.Secs.Abstractions.Model.SecsItem& value)`
- `System.Boolean TrySetAttribute(Dreamine.Gem300.Abstractions.Model.Gem300ObjectKey key, System.String name, Dreamine.Secs.Abstractions.Model.SecsItem value)`
- `System.Collections.Generic.IReadOnlyDictionary<System.String, Dreamine.Secs.Abstractions.Model.SecsItem> GetAttributes(Dreamine.Gem300.Abstractions.Model.Gem300ObjectKey key)`
- `System.Threading.Tasks.ValueTask<Dreamine.Gem.Abstractions.Model.GemCommandResult> ExecuteActionAsync(Dreamine.Gem300.Abstractions.Model.Gem300ObjectKey key, System.String actionName, System.Collections.Generic.IReadOnlyDictionary<System.String, Dreamine.Secs.Abstractions.Model.SecsItem> parameters, System.TimeSpan timeout, System.Threading.CancellationToken cancellationToken)`
- `System.Void Register(Dreamine.Gem300.Abstractions.Model.Gem300ObjectKey key, System.Collections.Generic.IEnumerable<Dreamine.Gem300.Abstractions.Model.Gem300AttributeDefinition> attributes)`
- `System.Void RegisterAction(Dreamine.Gem300.Abstractions.Model.Gem300ObjectKey key, System.String actionName, System.Func<System.Collections.Generic.IReadOnlyDictionary<System.String, Dreamine.Secs.Abstractions.Model.SecsItem>, System.Threading.CancellationToken, System.Threading.Tasks.ValueTask<Dreamine.Gem.Abstractions.Model.GemCommandResult>> handler)`

### `public sealed class Dreamine.Gem300.Substrate.SubstrateTracker`

- `Dreamine.Gem300.Abstractions.Model.SubstrateSnapshot Get(System.String substrateId)`
- `Dreamine.Gem300.Abstractions.States.MaterialLocationState GetLocationState(System.String locationId)`
- `SubstrateTracker(Dreamine.Gem300.Abstractions.Interfaces.IGem300EventJournal events, System.TimeProvider timeProvider)`
- `System.Boolean TryGet(System.String substrateId, Dreamine.Gem300.Abstractions.Model.SubstrateSnapshot& substrate)`
- `System.Void BeginProcessing(System.String substrateId)`
- `System.Void CompleteProcessing(System.String substrateId, Dreamine.Gem300.Abstractions.States.SubstrateProcessingState result)`
- `System.Void ConfirmId(System.String substrateId)`
- `System.Void MarkLost(System.String substrateId)`
- `System.Void Move(System.String substrateId, System.String locationId)`
- `System.Void Register(System.String substrateId, System.String sourceLocation, System.String destinationLocation, System.Boolean idConfirmed)`
- `System.Void RejectId(System.String substrateId)`
- `System.Void Remove(System.String substrateId)`
