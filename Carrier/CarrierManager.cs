using Dreamine.Gem300.Abstractions.Interfaces;
using Dreamine.Gem300.Abstractions.Model;
using Dreamine.Gem300.Abstractions.States;
using Dreamine.Gem300.Infrastructure;

namespace Dreamine.Gem300.Carrier;

/// <summary>\if KO E87-0312의 포트별 상태와 Carrier 병렬 상태를 원자적으로 관리합니다. \endif \if EN Atomically manages E87-0312 per-port and orthogonal carrier states. \endif</summary>
public sealed class CarrierManager : ICarrierManager
{
    private readonly object _gate = new();
    private readonly Dictionary<string, PortEntry> _ports = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CarrierEntry> _carriers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _workflowPortOwners = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _workflowCarrierOwners = new(StringComparer.Ordinal);
    private readonly Gem300EventPublisher _eventPublisher;
    private readonly Gem300DomainGate _domainGate;

    /// <summary>\if KO 이벤트 저널로 Carrier 관리자를 만듭니다. \endif \if EN Creates the carrier manager with an event journal. \endif</summary>
    public CarrierManager(IGem300EventJournal events) : this(new Gem300EventPublisher(events ?? throw new ArgumentNullException(nameof(events))), new()) { }
    internal CarrierManager(Gem300EventPublisher eventPublisher, Gem300DomainGate? domainGate = null)
    { _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher)); _domainGate = domainGate ?? new(); }
    internal Gem300DomainGate DomainGate => _domainGate;

    /// <summary>\if KO 이 관리자가 사용하는 비차단 이벤트 게시기 상태입니다. \endif \if EN Gets the non-throwing event-publisher health used by this manager. \endif</summary>
    public Gem300EventPublisherHealth EventHealth => _eventPublisher.GetHealth();

    /// <inheritdoc />
    public void RegisterLoadPort(string portId, LoadPortAccessMode accessMode = LoadPortAccessMode.Automatic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portId); if (!Enum.IsDefined(accessMode)) throw new ArgumentOutOfRangeException(nameof(accessMode));
        lock (_domainGate.SyncRoot) lock (_gate) if (!_ports.TryAdd(portId, new(portId, accessMode))) throw new InvalidOperationException("The load port is already registered.");
        ChangedPort(portId);
    }

    /// <inheritdoc />
    public void SetInService(string portId) => UpdatePort(portId, port => { EnsurePortNotWorkflowOwned(port.Id); Require(port.Transfer == LoadPortTransferState.OutOfService, "The port is already in service."); port.Transfer = LoadPortTransferState.ReadyToLoad; });
    /// <inheritdoc />
    public void SetOutOfService(string portId) => UpdatePort(portId, port => { EnsurePortNotWorkflowOwned(port.Id); Require(port.CarrierId is null && port.Reservation == LoadPortReservationState.NotReserved, "An occupied or reserved port cannot leave service."); port.Transfer = LoadPortTransferState.OutOfService; });
    /// <inheritdoc />
    public void ChangeAccessMode(string portId, LoadPortAccessMode accessMode)
    {
        if (!Enum.IsDefined(accessMode)) throw new ArgumentOutOfRangeException(nameof(accessMode));
        UpdatePort(portId, port => { EnsurePortNotWorkflowOwned(port.Id); Require(port.CarrierId is null && port.Reservation == LoadPortReservationState.NotReserved && port.Transfer != LoadPortTransferState.TransferBlocked, "Access mode cannot change during reservation or transfer."); port.Access = accessMode; });
    }
    /// <inheritdoc />
    public void Reserve(string portId) => UpdatePort(portId, port => { EnsurePortNotWorkflowOwned(port.Id); Require(port.Transfer == LoadPortTransferState.ReadyToLoad && port.Reservation == LoadPortReservationState.NotReserved, "The port cannot be reserved."); port.Reservation = LoadPortReservationState.Reserved; });
    /// <inheritdoc />
    public void CancelReservation(string portId) => UpdatePort(portId, port => { EnsurePortNotWorkflowOwned(port.Id); Require(port.CarrierId is null && port.Reservation == LoadPortReservationState.Reserved, "The reservation cannot be cancelled."); port.Reservation = LoadPortReservationState.NotReserved; });

    /// <inheritdoc />
    public void Bind(string portId, string carrierId, int capacity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(carrierId); if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        lock (_domainGate.SyncRoot) lock (_gate)
        {
            var port = Port(portId); EnsurePortNotWorkflowOwned(portId); EnsureCarrierNotWorkflowOwned(carrierId);
            Require(port.Transfer == LoadPortTransferState.ReadyToLoad && port.CarrierId is null && port.Association == CarrierAssociationState.NotAssociated, "The load port cannot bind a carrier.");
            if (_carriers.ContainsKey(carrierId)) throw new InvalidOperationException("The carrier ID is already registered.");
            port.CarrierId = carrierId; port.Association = CarrierAssociationState.Associated; port.Reservation = LoadPortReservationState.Reserved;
            _carriers.Add(carrierId, new(carrierId, portId, capacity));
        }
        ChangedPort(portId); ChangedCarrier(carrierId);
    }

    /// <inheritdoc />
    public void BeginLoad(string portId) => UpdatePort(portId, port => { EnsurePortNotWorkflowOwned(port.Id); Require(port.Transfer == LoadPortTransferState.ReadyToLoad && port.CarrierId is not null, "The port is not ready to load an associated carrier."); port.Transfer = LoadPortTransferState.TransferBlocked; port.Operation = TransferOperation.Loading; });
    /// <inheritdoc />
    public void CompleteLoad(string portId) => UpdatePort(portId, port => { EnsurePortNotWorkflowOwned(port.Id); Require(port.Transfer == LoadPortTransferState.TransferBlocked && port.Operation == TransferOperation.Loading && port.CarrierId is not null, "No load transfer is active."); port.Operation = TransferOperation.None; port.Reservation = LoadPortReservationState.NotReserved; });
    /// <inheritdoc />
    public void WaitForIdDecision(string carrierId) => UpdateCarrier(carrierId, carrier => { Require(carrier.IdStatus == CarrierIdStatus.IdNotRead, "Carrier ID cannot enter host-wait."); carrier.IdStatus = CarrierIdStatus.WaitingForHost; });
    /// <inheritdoc />
    public void AcceptId(string carrierId) => UpdateCarrier(carrierId, carrier => { Require(carrier.IdStatus is CarrierIdStatus.IdNotRead or CarrierIdStatus.WaitingForHost, "Carrier ID cannot be accepted."); carrier.IdStatus = CarrierIdStatus.VerificationOk; });
    /// <inheritdoc />
    public void RejectId(string carrierId) => UpdateCarrier(carrierId, carrier => { Require(carrier.IdStatus is CarrierIdStatus.IdNotRead or CarrierIdStatus.WaitingForHost, "Carrier ID cannot be rejected."); carrier.IdStatus = CarrierIdStatus.VerificationFailed; });

    /// <inheritdoc />
    public void WaitForSlotMapDecision(string carrierId, IEnumerable<CarrierSlotState> slotMap)
    {
        ArgumentNullException.ThrowIfNull(slotMap); var values = slotMap.ToArray();
        if (values.Any(static value => !Enum.IsDefined(value))) throw new ArgumentException("Slot-map values must be defined.", nameof(slotMap));
        UpdateCarrier(carrierId, carrier => { Require(carrier.SlotMapStatus == CarrierSlotMapStatus.SlotMapNotRead, "Slot map is already read."); if (values.Length != carrier.Capacity) throw new ArgumentException("Slot-map length must equal carrier capacity.", nameof(slotMap)); carrier.SlotMap = (CarrierSlotState[])values.Clone(); carrier.SlotMapStatus = CarrierSlotMapStatus.WaitingForHost; });
    }

    /// <inheritdoc />
    public void AcceptSlotMap(string carrierId) => UpdateCarrier(carrierId, carrier => { Require(carrier.SlotMapStatus == CarrierSlotMapStatus.WaitingForHost, "Slot map is not awaiting a decision."); carrier.SlotMapStatus = CarrierSlotMapStatus.VerificationOk; });
    /// <inheritdoc />
    public void RejectSlotMap(string carrierId) => UpdateCarrier(carrierId, carrier => { Require(carrier.SlotMapStatus == CarrierSlotMapStatus.WaitingForHost, "Slot map is not awaiting a decision."); carrier.SlotMapStatus = CarrierSlotMapStatus.VerificationFailed; });
    /// <inheritdoc />
    public void BeginAccess(string carrierId) => UpdateCarrier(carrierId, carrier => { Require(carrier.IdStatus == CarrierIdStatus.VerificationOk && carrier.SlotMapStatus == CarrierSlotMapStatus.VerificationOk && carrier.AccessingStatus == CarrierAccessingStatus.NotAccessed, "Only a verified carrier can enter access."); carrier.AccessingStatus = CarrierAccessingStatus.InAccess; });
    /// <inheritdoc />
    public void CompleteAccess(string carrierId) => UpdateCarrier(carrierId, carrier => { Require(carrier.AccessingStatus == CarrierAccessingStatus.InAccess, "Carrier access is not active."); carrier.AccessingStatus = CarrierAccessingStatus.CarrierComplete; });
    /// <inheritdoc />
    public void StopAccess(string carrierId) => UpdateCarrier(carrierId, carrier => { Require(carrier.AccessingStatus == CarrierAccessingStatus.InAccess, "Carrier access is not active."); carrier.AccessingStatus = CarrierAccessingStatus.CarrierStopped; });

    /// <inheritdoc />
    public void PrepareUnload(string carrierId)
    {
        string portId;
        lock (_domainGate.SyncRoot) lock (_gate)
        {
            var carrier = Carrier(carrierId); EnsureCarrierNotWorkflowOwned(carrierId); portId = carrier.PortId; EnsurePortNotWorkflowOwned(portId);
            Require(carrier.AccessingStatus is CarrierAccessingStatus.CarrierComplete or CarrierAccessingStatus.CarrierStopped || carrier.IdStatus == CarrierIdStatus.VerificationFailed || carrier.SlotMapStatus == CarrierSlotMapStatus.VerificationFailed, "Carrier is not complete, stopped, or rejected.");
            var port = Port(portId); Require(port.Transfer == LoadPortTransferState.TransferBlocked, "The port cannot become ready to unload."); port.Transfer = LoadPortTransferState.ReadyToUnload;
        }
        ChangedPort(portId);
    }

    /// <inheritdoc />
    public void BeginUnload(string portId) => UpdatePort(portId, port => { EnsurePortNotWorkflowOwned(port.Id); Require(port.Transfer == LoadPortTransferState.ReadyToUnload && port.CarrierId is not null, "The port is not ready to unload."); port.Transfer = LoadPortTransferState.TransferBlocked; port.Operation = TransferOperation.Unloading; });

    /// <inheritdoc />
    public void CompleteUnload(string portId)
    {
        string carrierId;
        lock (_domainGate.SyncRoot) lock (_gate)
        {
            var port = Port(portId); EnsurePortNotWorkflowOwned(portId); Require(port.Transfer == LoadPortTransferState.TransferBlocked && port.Operation == TransferOperation.Unloading, "No unload transfer is active.");
            if (port.CarrierId is not { } presentCarrierId) throw new InvalidOperationException("No carrier is associated.");
            EnsureCarrierNotWorkflowOwned(presentCarrierId); carrierId = presentCarrierId; _carriers.Remove(carrierId); ResetPort(port);
        }
        ChangedCarrier(carrierId); ChangedPort(portId);
    }

    /// <inheritdoc />
    public LoadPortSnapshot GetLoadPort(string portId)
    {
        lock (_domainGate.SyncRoot) lock (_gate) return Snapshot(Port(portId));
    }

    /// <inheritdoc />
    public CarrierSnapshot GetCarrier(string carrierId)
    {
        lock (_domainGate.SyncRoot) lock (_gate) return Snapshot(Carrier(carrierId));
    }

    /// <summary>\if KO Load Port 스냅샷을 ID 순서로 반환합니다. \endif \if EN Returns load-port snapshots in stable ID order. \endif</summary>
    public IReadOnlyList<LoadPortSnapshot> GetLoadPorts()
    {
        lock (_domainGate.SyncRoot) lock (_gate) return _ports.Values.OrderBy(static value => value.Id, StringComparer.Ordinal).Select(Snapshot).ToArray();
    }

    /// <summary>\if KO Carrier 스냅샷을 ID 순서로 반환합니다. \endif \if EN Returns carrier snapshots in stable ID order. \endif</summary>
    public IReadOnlyList<CarrierSnapshot> GetCarriers()
    {
        lock (_domainGate.SyncRoot) lock (_gate) return _carriers.Values.OrderBy(static value => value.Id, StringComparer.Ordinal).Select(Snapshot).ToArray();
    }

    internal void StageCoordinatedArrival(string ownerId, CarrierArrivalPlan plan)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId); ArgumentNullException.ThrowIfNull(plan);
        lock (_domainGate.SyncRoot) lock (_gate)
        {
            var port = Port(plan.PortId); EnsurePortNotWorkflowOwned(plan.PortId); EnsureCarrierNotWorkflowOwned(plan.CarrierId);
            Require(port.Transfer == LoadPortTransferState.ReadyToLoad && port.CarrierId is null && port.Association == CarrierAssociationState.NotAssociated, "The load port is not ready for an unassociated carrier.");
            if (_carriers.ContainsKey(plan.CarrierId)) throw new InvalidOperationException("The carrier ID is already registered.");
            _workflowPortOwners.Add(plan.PortId, ownerId); _workflowCarrierOwners.Add(plan.CarrierId, ownerId);
        }
    }

    internal void CancelStagedArrival(string ownerId, CarrierArrivalPlan plan)
    {
        lock (_domainGate.SyncRoot) lock (_gate)
        {
            if (_workflowCarrierOwners.TryGetValue(plan.CarrierId, out var carrierOwner) && StringComparer.Ordinal.Equals(carrierOwner, ownerId) && !_carriers.ContainsKey(plan.CarrierId)) _workflowCarrierOwners.Remove(plan.CarrierId);
            if (_workflowPortOwners.TryGetValue(plan.PortId, out var portOwner) && StringComparer.Ordinal.Equals(portOwner, ownerId) && Port(plan.PortId).CarrierId is null) _workflowPortOwners.Remove(plan.PortId);
        }
    }

    internal void ValidateCoordinatedArrivalCore(string ownerId, CarrierArrivalPlan plan)
    {
        lock (_gate)
        {
            EnsureWorkflowOwner(ownerId, plan.PortId, plan.CarrierId); var port = Port(plan.PortId);
            Require(port.Transfer == LoadPortTransferState.ReadyToLoad && port.CarrierId is null && port.Association == CarrierAssociationState.NotAssociated && !_carriers.ContainsKey(plan.CarrierId), "The staged carrier arrival is no longer valid.");
        }
    }

    internal void CommitCoordinatedArrivalCore(string ownerId, CarrierArrivalPlan plan)
    {
        lock (_gate)
        {
            EnsureWorkflowOwner(ownerId, plan.PortId, plan.CarrierId); var port = Port(plan.PortId);
            var carrier = new CarrierEntry(plan.CarrierId, plan.PortId, plan.SlotMap.Count)
            {
                IdStatus = CarrierIdStatus.VerificationOk, SlotMapStatus = CarrierSlotMapStatus.VerificationOk,
                AccessingStatus = CarrierAccessingStatus.InAccess, SlotMap = plan.SlotMap.ToArray()
            };
            _carriers.Add(plan.CarrierId, carrier); port.CarrierId = plan.CarrierId; port.Association = CarrierAssociationState.Associated;
            port.Reservation = LoadPortReservationState.NotReserved; port.Transfer = LoadPortTransferState.TransferBlocked; port.Operation = TransferOperation.None;
        }
    }

    internal void RollbackCoordinatedArrivalCore(string ownerId, CarrierArrivalPlan plan)
    {
        lock (_gate)
        {
            if (!_workflowCarrierOwners.TryGetValue(plan.CarrierId, out var owner) || !StringComparer.Ordinal.Equals(owner, ownerId)) return;
            if (_carriers.Remove(plan.CarrierId) && _ports.TryGetValue(plan.PortId, out var port) && StringComparer.Ordinal.Equals(port.CarrierId, plan.CarrierId)) ResetPort(port);
        }
    }

    internal void ValidateCoordinatedReleaseCore(string ownerId, string carrierId)
    {
        lock (_gate)
        {
            var carrier = Carrier(carrierId); EnsureWorkflowOwner(ownerId, carrier.PortId, carrierId); var port = Port(carrier.PortId);
            Require(carrier.AccessingStatus == CarrierAccessingStatus.InAccess, "Carrier access is not active.");
            Require(port.Transfer == LoadPortTransferState.TransferBlocked && port.Operation == TransferOperation.None && StringComparer.Ordinal.Equals(port.CarrierId, carrierId), "The carrier port is not ready for coordinated release.");
        }
    }

    internal string CommitCoordinatedReleaseCore(string ownerId, string carrierId)
    {
        lock (_gate)
        {
            var carrier = Carrier(carrierId); EnsureWorkflowOwner(ownerId, carrier.PortId, carrierId); var portId = carrier.PortId; var port = Port(portId);
            _carriers.Remove(carrierId); ResetPort(port); _workflowCarrierOwners.Remove(carrierId); _workflowPortOwners.Remove(portId); return portId;
        }
    }

    internal void CompleteCoordinatedRelease(string carrierId)
    {
        string portId;
        lock (_domainGate.SyncRoot)
        {
            string owner; lock (_gate) owner = _workflowCarrierOwners.TryGetValue(carrierId, out var value) ? value : throw new InvalidOperationException("The carrier has no workflow owner.");
            ValidateCoordinatedReleaseCore(owner, carrierId); portId = CommitCoordinatedReleaseCore(owner, carrierId);
        }
        PublishCoordinatedChange(carrierId, portId);
    }

    internal void PublishCoordinatedChange(string carrierId, string portId) { ChangedCarrier(carrierId); ChangedPort(portId); }

    private void UpdatePort(string id, Action<PortEntry> update)
    {
        lock (_domainGate.SyncRoot) lock (_gate) update(Port(id));
        ChangedPort(id);
    }

    private void UpdateCarrier(string id, Action<CarrierEntry> update)
    {
        lock (_domainGate.SyncRoot) lock (_gate) { EnsureCarrierNotWorkflowOwned(id); update(Carrier(id)); }
        ChangedCarrier(id);
    }

    private void EnsurePortNotWorkflowOwned(string id) { if (_workflowPortOwners.ContainsKey(id)) throw new InvalidOperationException("The load port is owned by an active coordinated workflow."); }
    private void EnsureCarrierNotWorkflowOwned(string id) { if (_workflowCarrierOwners.ContainsKey(id)) throw new InvalidOperationException("The carrier is owned by an active coordinated workflow."); }
    private void EnsureWorkflowOwner(string ownerId, string portId, string carrierId)
    {
        if (!_workflowPortOwners.TryGetValue(portId, out var portOwner) || !_workflowCarrierOwners.TryGetValue(carrierId, out var carrierOwner) || !StringComparer.Ordinal.Equals(portOwner, ownerId) || !StringComparer.Ordinal.Equals(carrierOwner, ownerId))
            throw new InvalidOperationException("The coordinated workflow does not own the carrier and load port.");
    }

    private PortEntry Port(string id) { ArgumentException.ThrowIfNullOrWhiteSpace(id); return _ports.TryGetValue(id, out var value) ? value : throw new KeyNotFoundException("The load port is not registered."); }
    private CarrierEntry Carrier(string id) { ArgumentException.ThrowIfNullOrWhiteSpace(id); return _carriers.TryGetValue(id, out var value) ? value : throw new KeyNotFoundException("The carrier is not registered."); }
    private static LoadPortSnapshot Snapshot(PortEntry value) => new(value.Id, value.Transfer, value.Access, value.Reservation, value.Association, value.CarrierId);
    private static CarrierSnapshot Snapshot(CarrierEntry value) => new(value.Id, value.PortId, value.IdStatus, value.SlotMapStatus, value.AccessingStatus, value.SlotMap);
    private static void ResetPort(PortEntry port) { port.CarrierId = null; port.Association = CarrierAssociationState.NotAssociated; port.Reservation = LoadPortReservationState.NotReserved; port.Transfer = LoadPortTransferState.ReadyToLoad; port.Operation = TransferOperation.None; }
    private void ChangedPort(string id) => _eventPublisher.TryRecord(Gem300EventKind.LoadPortChanged, id);
    private void ChangedCarrier(string id) => _eventPublisher.TryRecord(Gem300EventKind.CarrierChanged, id);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private enum TransferOperation { None, Loading, Unloading }
    private sealed class PortEntry(string id, LoadPortAccessMode access) { public string Id { get; } = id; public LoadPortTransferState Transfer { get; set; } = LoadPortTransferState.OutOfService; public LoadPortAccessMode Access { get; set; } = access; public LoadPortReservationState Reservation { get; set; } public CarrierAssociationState Association { get; set; } public string? CarrierId { get; set; } public TransferOperation Operation { get; set; } }
    private sealed class CarrierEntry(string id, string portId, int capacity) { public string Id { get; } = id; public string PortId { get; } = portId; public int Capacity { get; } = capacity; public CarrierIdStatus IdStatus { get; set; } = CarrierIdStatus.IdNotRead; public CarrierSlotMapStatus SlotMapStatus { get; set; } = CarrierSlotMapStatus.SlotMapNotRead; public CarrierAccessingStatus AccessingStatus { get; set; } = CarrierAccessingStatus.NotAccessed; public CarrierSlotState[] SlotMap { get; set; } = Enumerable.Repeat(CarrierSlotState.Undefined, capacity).ToArray(); }
}
