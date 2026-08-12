using Dreamine.Gem300.Abstractions.Interfaces;
using Dreamine.Gem300.Abstractions.Model;
using Dreamine.Gem300.Abstractions.States;
using Dreamine.Gem300.Carrier;
using Dreamine.Gem300.Infrastructure;
using Dreamine.Gem300.Jobs;
using Dreamine.Gem300.Substrate;

namespace Dreamine.Gem300;

/// <summary>\if KO 검증된 모듈 경계를 조합하는 Experimental Carrier→Job→반출 조정자입니다. 표준 wire 서비스를 구현하지 않습니다. \endif \if EN Provides an experimental carrier-to-job-to-removal coordinator over verified module boundaries; it does not implement standard wire services. \endif</summary>
public sealed class Gem300WorkflowCoordinator
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Coordination> _carrierMaterials = new(StringComparer.Ordinal);
    private readonly HashSet<string> _executingControlJobs = new(StringComparer.Ordinal);
    private readonly string _executionOwner = Guid.NewGuid().ToString("N");
    private readonly ICarrierManager _carriers;
    private readonly ISubstrateTracker _substrates;
    private readonly IProcessJobManager _processJobs;
    private readonly IControlJobManager _controlJobs;
    private readonly CarrierManager? _transactionalCarriers;
    private readonly SubstrateTracker? _transactionalSubstrates;
    private readonly ProcessJobManager? _transactionalProcessJobs;
    private readonly ControlJobManager? _transactionalControlJobs;
    private readonly Gem300DomainGate? _domainGate;

    /// <summary>\if KO 독립 모듈들로 조정자를 만듭니다. \endif \if EN Creates the coordinator from independent modules. \endif</summary>
    public Gem300WorkflowCoordinator(ICarrierManager carriers, ISubstrateTracker substrates, IProcessJobManager processJobs, IControlJobManager controlJobs)
    {
        _carriers = carriers ?? throw new ArgumentNullException(nameof(carriers)); _substrates = substrates ?? throw new ArgumentNullException(nameof(substrates));
        _processJobs = processJobs ?? throw new ArgumentNullException(nameof(processJobs)); _controlJobs = controlJobs ?? throw new ArgumentNullException(nameof(controlJobs));
        _transactionalCarriers = carriers as CarrierManager; _transactionalSubstrates = substrates as SubstrateTracker;
        _transactionalProcessJobs = processJobs as ProcessJobManager; _transactionalControlJobs = controlJobs as ControlJobManager;
        if (_transactionalCarriers is not null && _transactionalSubstrates is not null && ReferenceEquals(_transactionalCarriers.DomainGate, _transactionalSubstrates.DomainGate)) _domainGate = _transactionalCarriers.DomainGate;
        if (_transactionalProcessJobs is not null && (_transactionalSubstrates is null || !ReferenceEquals(_transactionalProcessJobs.SubstrateStore, _transactionalSubstrates) || !ReferenceEquals(_transactionalProcessJobs.DomainGate, _transactionalSubstrates.DomainGate)))
            throw new InvalidOperationException("The ProcessJobManager and coordinator must use the exact same SubstrateTracker and transaction boundary.");
        if (_transactionalControlJobs is not null && (_transactionalProcessJobs is null || !ReferenceEquals(_transactionalControlJobs.ProcessJobStore, _transactionalProcessJobs)))
            throw new InvalidOperationException("The ControlJobManager and coordinator must use the exact same ProcessJobManager.");
    }

    /// <summary>\if KO 명시적 애플리케이션 Slot 연결을 검증한 뒤 Carrier와 기판을 하나의 built-in 트랜잭션으로 수락합니다. \endif \if EN Validates explicit application slot associations and accepts the carrier and substrates in one built-in transaction. \endif</summary>
    public void AcceptCarrier(CarrierArrivalPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan); ValidateArrivalPlan(plan);
        var carriers = RequireTransactionalCarriers(); var substrates = RequireTransactionalSubstrates(); var domainGate = _domainGate!;
        var ownerId = CarrierOwner(plan.CarrierId); var materialIds = plan.Substrates.Select(static value => value.SubstrateId).ToArray();
        carriers.StageCoordinatedArrival(ownerId, plan);
        IReadOnlyList<SubstrateTracker.PreparedSubstrateArrival>? prepared = null;
        var substratesCommitted = false; var carrierCommitted = false; var coordinationCommitted = false;
        try
        {
            prepared = substrates.PrepareArrival(plan.Substrates);
            lock (domainGate.SyncRoot)
            {
                lock (_gate) if (_carrierMaterials.ContainsKey(plan.CarrierId)) throw new InvalidOperationException("The carrier is already coordinated.");
                carriers.ValidateCoordinatedArrivalCore(ownerId, plan); substrates.ValidateArrivalCore(ownerId, prepared);
                substrates.CommitArrivalCore(ownerId, prepared); substratesCommitted = true;
                carriers.CommitCoordinatedArrivalCore(ownerId, plan); carrierCommitted = true;
                lock (_gate)
                {
                    _carrierMaterials.Add(plan.CarrierId, new(materialIds, plan.SlotAssignments)); coordinationCommitted = true;
                }
            }
        }
        catch
        {
            lock (domainGate.SyncRoot)
            {
                if (coordinationCommitted) lock (_gate) _carrierMaterials.Remove(plan.CarrierId);
                if (carrierCommitted) carriers.RollbackCoordinatedArrivalCore(ownerId, plan);
                if (substratesCommitted) substrates.RollbackArrivalCore(ownerId, materialIds);
            }
            carriers.CancelStagedArrival(ownerId, plan); throw;
        }
        substrates.PublishChanges(materialIds); carriers.PublishCoordinatedChange(plan.CarrierId, plan.PortId);
    }

    /// <summary>\if KO Control Job의 Process Job을 단일 실행 claim 아래 순서대로 실행합니다. 실패 정리는 현재 상태에서 전진 가능한 전이만 적용합니다. \endif \if EN Executes a control job's process jobs under a single execution claim and performs only forward-valid cleanup transitions. \endif</summary>
    public async Task ExecuteControlJobAsync(string controlJobId, Func<ProcessJobDefinition, CancellationToken, ValueTask> processor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controlJobId); ArgumentNullException.ThrowIfNull(processor); cancellationToken.ThrowIfCancellationRequested();
        var controls = _transactionalControlJobs ?? throw new NotSupportedException("Safe workflow execution requires the built-in ControlJobManager.");
        if (_transactionalProcessJobs is null) throw new NotSupportedException("Safe workflow execution requires the built-in ProcessJobManager.");
        lock (_gate) if (!_executingControlJobs.Add(controlJobId)) throw new InvalidOperationException("The control job is already executing through this coordinator.");
        var executionClaimed = false;
        string? activeJob = null;
        try
        {
            controls.ClaimWorkflowExecution(controlJobId, _executionOwner); executionClaimed = true;
            var control = _controlJobs.Get(controlJobId);
            _controlJobs.Select(controlJobId); _controlJobs.Ready(controlJobId); if (control.Definition.ManualStart) _controlJobs.Start(controlJobId);
            for (var index = 0; index < control.Definition.ProcessJobIds.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested(); activeJob = control.Definition.ProcessJobIds[index]; var process = _processJobs.Get(activeJob);
                _processJobs.Allocate(activeJob); _processJobs.CompleteSetup(activeJob); if (process.Definition.ManualStart) _processJobs.Start(activeJob);
                foreach (var material in process.Definition.MaterialIds) _substrates.BeginProcessing(material);
                await processor(process.Definition, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                var processState = _processJobs.Get(activeJob).State;
                if (processState == ProcessJobState.Processing) { _processJobs.Complete(activeJob); processState = ProcessJobState.ProcessComplete; }
                if (processState != ProcessJobState.ProcessComplete) throw new InvalidOperationException("The processor ended the process job without a successful completion outcome.");
                foreach (var material in process.Definition.MaterialIds) if (_substrates.Get(material).ProcessingState == SubstrateProcessingState.InProcess) _substrates.CompleteProcessing(material, SubstrateProcessingState.Processed);
                activeJob = null;
                var controlState = _controlJobs.Get(controlJobId).State;
                if (controlState != ControlJobState.Executing) throw new InvalidOperationException("The processor changed the control job while workflow execution was active.");
                if (index + 1 < control.Definition.ProcessJobIds.Count) _controlJobs.Advance(controlJobId);
            }
            _controlJobs.Complete(controlJobId);
        }
        catch (Exception exception)
        {
            try
            {
                if (executionClaimed)
                {
                    if (activeJob is not null) CleanupProcess(activeJob);
                    CleanupControl(controlJobId);
                }
            }
            catch (Exception cleanupException) { exception.Data["Gem300WorkflowCleanupFailure"] = cleanupException.ToString(); }
            throw;
        }
        finally
        {
            if (executionClaimed) controls.ReleaseWorkflowExecution(controlJobId, _executionOwner);
            lock (_gate) _executingControlJobs.Remove(controlJobId);
        }
    }

    /// <summary>\if KO 모든 연계 기판이 목적지에서 최종 처리 상태인지 확인하고 built-in 트랜잭션으로 객체와 Carrier를 반출합니다. \endif \if EN Verifies terminal destination state and removes substrates and carrier in one built-in transaction. \endif</summary>
    public void ReleaseCarrier(string carrierId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(carrierId);
        var carriers = RequireTransactionalCarriers(); var substrates = RequireTransactionalSubstrates(); var domainGate = _domainGate!;
        Coordination coordination; lock (_gate) coordination = _carrierMaterials.TryGetValue(carrierId, out var value) ? value : throw new KeyNotFoundException("The carrier is not coordinated.");
        var materials = coordination.MaterialIds; var removedAt = materials.Count == 0 ? default : substrates.CaptureTime(); var ownerId = CarrierOwner(carrierId); string portId;
        lock (domainGate.SyncRoot)
        {
            lock (_gate) if (!_carrierMaterials.TryGetValue(carrierId, out var current) || !ReferenceEquals(current, coordination)) throw new InvalidOperationException("The carrier coordination changed during release.");
            foreach (var id in materials)
            {
                var substrate = _substrates.Get(id);
                if (substrate.TransportState != SubstrateTransportState.AtDestination || substrate.ProcessingState is SubstrateProcessingState.NeedsProcessing or SubstrateProcessingState.InProcess) throw new InvalidOperationException("Every substrate must be terminal at destination.");
            }
            substrates.ValidateRemoveOwnedCore(ownerId, materials); carriers.ValidateCoordinatedReleaseCore(ownerId, carrierId);
            substrates.RemoveOwnedCore(ownerId, materials, removedAt); portId = carriers.CommitCoordinatedReleaseCore(ownerId, carrierId);
            lock (_gate) _carrierMaterials.Remove(carrierId);
        }
        substrates.PublishChanges(materials); carriers.PublishCoordinatedChange(carrierId, portId);
    }

    /// <summary>\if KO 현재 조정 중인 Carrier ID를 안정적인 순서로 반환합니다. \endif \if EN Returns currently coordinated carrier IDs in stable order. \endif</summary>
    public IReadOnlyList<string> GetCoordinatedCarrierIds()
    {
        if (_domainGate is { } domainGate) lock (domainGate.SyncRoot) lock (_gate) return _carrierMaterials.Keys.Order(StringComparer.Ordinal).ToArray();
        lock (_gate) return _carrierMaterials.Keys.Order(StringComparer.Ordinal).ToArray();
    }

    /// <summary>\if KO Carrier 계획에 애플리케이션이 명시한 Slot↔Substrate 연결의 안정적인 스냅샷을 반환합니다. \endif \if EN Returns a stable snapshot of application-declared slot/substrate associations for a coordinated carrier. \endif</summary>
    public IReadOnlyList<CarrierSubstrateSlotAssignment> GetCoordinatedSlotAssignments(string carrierId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(carrierId);
        if (_domainGate is { } domainGate) lock (domainGate.SyncRoot) lock (_gate) return SlotAssignments(carrierId);
        lock (_gate) return SlotAssignments(carrierId);
    }

    private IReadOnlyList<CarrierSubstrateSlotAssignment> SlotAssignments(string carrierId) => _carrierMaterials.TryGetValue(carrierId, out var value) ? value.SlotAssignments.ToArray() : throw new KeyNotFoundException("The carrier is not coordinated.");

    private void CleanupProcess(string id)
    {
        var process = _processJobs.Get(id);
        foreach (var material in process.Definition.MaterialIds)
            if (_substrates.Get(material).ProcessingState == SubstrateProcessingState.InProcess) _substrates.CompleteProcessing(material, SubstrateProcessingState.Aborted);
        var state = _processJobs.Get(id).State;
        if (state is ProcessJobState.SettingUp or ProcessJobState.WaitingForStart or ProcessJobState.Processing or ProcessJobState.Pausing or ProcessJobState.Paused or ProcessJobState.Stopping)
        { _processJobs.Abort(id); state = ProcessJobState.Aborting; }
        if (state == ProcessJobState.Aborting) _processJobs.ConfirmAborted(id);
    }

    private void CleanupControl(string id)
    {
        var state = _controlJobs.Get(id).State;
        if (state is ControlJobState.Selected or ControlJobState.WaitingForStart or ControlJobState.Executing or ControlJobState.Paused) _controlJobs.Abort(id);
    }

    private CarrierManager RequireTransactionalCarriers() => _transactionalCarriers is not null && _domainGate is not null ? _transactionalCarriers : throw new NotSupportedException("Safe carrier workflows require built-in CarrierManager and SubstrateTracker instances created with one shared transaction boundary (for example Gem300Runtime).");
    private SubstrateTracker RequireTransactionalSubstrates() => _transactionalSubstrates is not null && _domainGate is not null ? _transactionalSubstrates : throw new NotSupportedException("Safe carrier workflows require built-in CarrierManager and SubstrateTracker instances created with one shared transaction boundary (for example Gem300Runtime).");

    private static void ValidateArrivalPlan(CarrierArrivalPlan plan)
    {
        if (plan.SlotMap.Any(static state => !Enum.IsDefined(state))) throw new ArgumentException("Slot-map values must be defined.", nameof(plan));
        var occupied = plan.SlotMap.Count(static state => state is CarrierSlotState.CorrectlyOccupied or CarrierSlotState.NotEmpty);
        if (plan.SlotMap.Any(static state => state is CarrierSlotState.DoubleSlotted or CarrierSlotState.CrossSlotted) || occupied != plan.Substrates.Count) throw new InvalidOperationException("The slot map is inconsistent with the substrate plan.");
        if (plan.Substrates.Count != 0 && !plan.HasExplicitSlotAssignments) throw new InvalidOperationException("A safe built-in workflow requires explicit application-level slot/substrate assignments; ordering is never treated as a slot mapping.");
    }

    private static bool IsPostActive(ProcessJobState state) => state is ProcessJobState.ProcessComplete or ProcessJobState.Stopped or ProcessJobState.Aborted;
    private static string CarrierOwner(string id) => $"Carrier>{id}";

    private sealed class Coordination(IReadOnlyList<string> materialIds, IReadOnlyList<CarrierSubstrateSlotAssignment> slotAssignments)
    {
        public IReadOnlyList<string> MaterialIds { get; } = materialIds.ToArray();
        public IReadOnlyList<CarrierSubstrateSlotAssignment> SlotAssignments { get; } = slotAssignments.ToArray();
    }
}
