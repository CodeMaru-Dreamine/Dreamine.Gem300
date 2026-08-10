using Dreamine.Gem300.Abstractions.Interfaces;
using Dreamine.Gem300.Abstractions.Model;
using Dreamine.Gem300.Abstractions.States;

namespace Dreamine.Gem300;

/// <summary>\if KO 검증된 모듈 경계를 조합하는 Experimental Carrier→Job→반출 조정자입니다. 표준 wire 서비스를 구현하지 않습니다. \endif \if EN Provides an experimental carrier-to-job-to-removal coordinator over verified module boundaries; it does not implement standard wire services. \endif</summary>
public sealed class Gem300WorkflowCoordinator
{
    private readonly object _gate = new();
    private readonly Dictionary<string, string[]> _carrierMaterials = new(StringComparer.Ordinal);
    private readonly ICarrierManager _carriers;
    private readonly ISubstrateTracker _substrates;
    private readonly IProcessJobManager _processJobs;
    private readonly IControlJobManager _controlJobs;
    /// <summary>\if KO 독립 모듈들로 조정자를 만듭니다. \endif \if EN Creates the coordinator from independent modules. \endif</summary>
    public Gem300WorkflowCoordinator(ICarrierManager carriers, ISubstrateTracker substrates, IProcessJobManager processJobs, IControlJobManager controlJobs)
    { _carriers = carriers ?? throw new ArgumentNullException(nameof(carriers)); _substrates = substrates ?? throw new ArgumentNullException(nameof(substrates)); _processJobs = processJobs ?? throw new ArgumentNullException(nameof(processJobs)); _controlJobs = controlJobs ?? throw new ArgumentNullException(nameof(controlJobs)); }

    /// <summary>\if KO 계획을 검증한 뒤 Carrier ID·Slot Map을 승인하고 기판을 등록하여 접근을 시작합니다. \endif \if EN Validates a plan, accepts carrier ID and slot map, registers substrates, and begins access. \endif</summary>
    public void AcceptCarrier(CarrierArrivalPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var occupied = plan.SlotMap.Count(static state => state is CarrierSlotState.CorrectlyOccupied or CarrierSlotState.NotEmpty);
        if (plan.SlotMap.Any(static state => state is CarrierSlotState.DoubleSlotted or CarrierSlotState.CrossSlotted) || occupied != plan.Substrates.Count) throw new InvalidOperationException("The slot map is inconsistent with the substrate plan.");
        foreach (var material in plan.Substrates)
        {
            if (_substrates.GetLocationState(material.SourceLocation) == MaterialLocationState.Occupied) throw new InvalidOperationException("A source location is occupied.");
            if (_substrates.TryGet(material.SubstrateId, out _)) throw new InvalidOperationException("A substrate ID is already registered.");
        }
        lock (_gate) if (_carrierMaterials.ContainsKey(plan.CarrierId)) throw new InvalidOperationException("The carrier is already coordinated.");
        _carriers.Bind(plan.PortId, plan.CarrierId, plan.SlotMap.Count); _carriers.BeginLoad(plan.PortId); _carriers.CompleteLoad(plan.PortId);
        _carriers.WaitForIdDecision(plan.CarrierId); _carriers.AcceptId(plan.CarrierId); _carriers.WaitForSlotMapDecision(plan.CarrierId, plan.SlotMap); _carriers.AcceptSlotMap(plan.CarrierId);
        foreach (var material in plan.Substrates) _substrates.Register(material.SubstrateId, material.SourceLocation, material.DestinationLocation);
        _carriers.BeginAccess(plan.CarrierId);
        lock (_gate) _carrierMaterials.Add(plan.CarrierId, plan.Substrates.Select(static value => value.SubstrateId).ToArray());
    }

    /// <summary>\if KO Control Job의 Process Job을 순서대로 실행합니다. 처리기 실패·취소 시 현재 상태를 중단 완료로 정리하고 예외를 다시 전달합니다. \endif \if EN Executes a control job's process jobs in order; handler failure or cancellation aborts active state and is rethrown. \endif</summary>
    public async Task ExecuteControlJobAsync(string controlJobId, Func<ProcessJobDefinition, CancellationToken, ValueTask> processor, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(controlJobId); ArgumentNullException.ThrowIfNull(processor); cancellationToken.ThrowIfCancellationRequested();
        var control = _controlJobs.Get(controlJobId); _controlJobs.Select(controlJobId); _controlJobs.Ready(controlJobId); if (control.Definition.ManualStart) _controlJobs.Start(controlJobId);
        string? activeJob = null;
        try
        {
            for (var index = 0; index < control.Definition.ProcessJobIds.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested(); activeJob = control.Definition.ProcessJobIds[index]; var process = _processJobs.Get(activeJob);
                _processJobs.Allocate(activeJob); _processJobs.CompleteSetup(activeJob); if (process.Definition.ManualStart) _processJobs.Start(activeJob);
                foreach (var material in process.Definition.MaterialIds) _substrates.BeginProcessing(material);
                await processor(process.Definition, cancellationToken).ConfigureAwait(false);
                foreach (var material in process.Definition.MaterialIds) if (_substrates.Get(material).ProcessingState == SubstrateProcessingState.InProcess) _substrates.CompleteProcessing(material, SubstrateProcessingState.Processed);
                _processJobs.Complete(activeJob);
                if (index + 1 < control.Definition.ProcessJobIds.Count) _controlJobs.Advance(controlJobId);
                activeJob = null;
            }
            _controlJobs.Complete(controlJobId);
        }
        catch (Exception exception)
        {
            try
            {
                if (activeJob is not null) AbortProcess(activeJob);
                _controlJobs.Abort(controlJobId);
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException("Workflow execution and deterministic abort cleanup both failed.", exception, cleanupException);
            }
            throw;
        }
    }

    /// <summary>\if KO 모든 연계 기판이 목적지에서 최종 처리 상태인지 확인하고 객체와 Carrier를 반출합니다. \endif \if EN Verifies that all linked substrates are terminal at destination, then removes them and unloads the carrier. \endif</summary>
    public void ReleaseCarrier(string carrierId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(carrierId); string[] materials;
        lock (_gate) materials = _carrierMaterials.TryGetValue(carrierId, out var value) ? (string[])value.Clone() : throw new KeyNotFoundException("The carrier is not coordinated.");
        foreach (var id in materials)
        {
            var substrate = _substrates.Get(id);
            if (substrate.TransportState != SubstrateTransportState.AtDestination || substrate.ProcessingState is SubstrateProcessingState.NeedsProcessing or SubstrateProcessingState.InProcess) throw new InvalidOperationException("Every substrate must be terminal at destination.");
        }
        var carrier = _carriers.GetCarrier(carrierId); _carriers.CompleteAccess(carrierId); _carriers.PrepareUnload(carrierId);
        foreach (var id in materials) _substrates.Remove(id);
        _carriers.BeginUnload(carrier.PortId); _carriers.CompleteUnload(carrier.PortId);
        lock (_gate) _carrierMaterials.Remove(carrierId);
    }

    private void AbortProcess(string id)
    {
        var process = _processJobs.Get(id);
        foreach (var material in process.Definition.MaterialIds)
        {
            var substrate = _substrates.Get(material);
            if (substrate.ProcessingState == SubstrateProcessingState.InProcess) _substrates.CompleteProcessing(material, SubstrateProcessingState.Aborted);
        }
        _processJobs.Abort(id); _processJobs.ConfirmAborted(id);
    }
}
