using Dreamine.Gem300.Abstractions.Interfaces;
using Dreamine.Gem300.Abstractions.Model;
using Dreamine.Gem300.Abstractions.States;

namespace Dreamine.Gem300.Carrier;

/// <summary>\if KO E87-0312의 포트별 상태와 Carrier 병렬 상태를 원자적으로 관리합니다. \endif \if EN Atomically manages E87-0312 per-port and orthogonal carrier states. \endif</summary>
public sealed class CarrierManager : ICarrierManager
{
    private readonly object _gate = new();
    private readonly Dictionary<string, PortEntry> _ports = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CarrierEntry> _carriers = new(StringComparer.Ordinal);
    private readonly IGem300EventJournal _events;
    /// <summary>\if KO 이벤트 저널로 Carrier 관리자를 만듭니다. \endif \if EN Creates the carrier manager with an event journal. \endif</summary>
    public CarrierManager(IGem300EventJournal events) => _events = events ?? throw new ArgumentNullException(nameof(events));
    /// <inheritdoc />
    public void RegisterLoadPort(string portId, LoadPortAccessMode accessMode = LoadPortAccessMode.Automatic)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portId);
        lock (_gate) if (!_ports.TryAdd(portId, new(portId, accessMode))) throw new InvalidOperationException("The load port is already registered.");
        ChangedPort(portId);
    }
    /// <inheritdoc />
    public void SetInService(string portId)
    {
        lock (_gate) { var port = Port(portId); Require(port.Transfer == LoadPortTransferState.OutOfService, "The port is already in service."); port.Transfer = LoadPortTransferState.ReadyToLoad; }
        ChangedPort(portId);
    }
    /// <inheritdoc />
    public void SetOutOfService(string portId)
    {
        lock (_gate) { var port = Port(portId); Require(port.CarrierId is null && port.Reservation == LoadPortReservationState.NotReserved, "An occupied or reserved port cannot leave service."); port.Transfer = LoadPortTransferState.OutOfService; }
        ChangedPort(portId);
    }
    /// <inheritdoc />
    public void ChangeAccessMode(string portId, LoadPortAccessMode accessMode)
    {
        lock (_gate) { var port = Port(portId); Require(port.CarrierId is null && port.Reservation == LoadPortReservationState.NotReserved && port.Transfer != LoadPortTransferState.TransferBlocked, "Access mode cannot change during reservation or transfer."); port.Access = accessMode; }
        ChangedPort(portId);
    }
    /// <inheritdoc />
    public void Reserve(string portId)
    {
        lock (_gate) { var port = Port(portId); Require(port.Transfer == LoadPortTransferState.ReadyToLoad && port.Reservation == LoadPortReservationState.NotReserved, "The port cannot be reserved."); port.Reservation = LoadPortReservationState.Reserved; }
        ChangedPort(portId);
    }
    /// <inheritdoc />
    public void CancelReservation(string portId)
    {
        lock (_gate) { var port = Port(portId); Require(port.CarrierId is null && port.Reservation == LoadPortReservationState.Reserved, "The reservation cannot be cancelled."); port.Reservation = LoadPortReservationState.NotReserved; }
        ChangedPort(portId);
    }
    /// <inheritdoc />
    public void Bind(string portId, string carrierId, int capacity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(carrierId); if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        lock (_gate)
        {
            var port = Port(portId); Require(port.Transfer == LoadPortTransferState.ReadyToLoad && port.CarrierId is null && port.Association == CarrierAssociationState.NotAssociated, "The load port cannot bind a carrier.");
            if (_carriers.ContainsKey(carrierId)) throw new InvalidOperationException("The carrier ID is already registered.");
            port.CarrierId = carrierId; port.Association = CarrierAssociationState.Associated; port.Reservation = LoadPortReservationState.Reserved;
            _carriers.Add(carrierId, new(carrierId, portId, capacity));
        }
        ChangedPort(portId); ChangedCarrier(carrierId);
    }
    /// <inheritdoc />
    public void BeginLoad(string portId)
    {
        lock (_gate) { var port = Port(portId); Require(port.Transfer == LoadPortTransferState.ReadyToLoad && port.CarrierId is not null, "The port is not ready to load an associated carrier."); port.Transfer = LoadPortTransferState.TransferBlocked; port.Operation = TransferOperation.Loading; }
        ChangedPort(portId);
    }
    /// <inheritdoc />
    public void CompleteLoad(string portId)
    {
        lock (_gate) { var port = Port(portId); Require(port.Transfer == LoadPortTransferState.TransferBlocked && port.Operation == TransferOperation.Loading && port.CarrierId is not null, "No load transfer is active."); port.Operation = TransferOperation.None; port.Reservation = LoadPortReservationState.NotReserved; }
        ChangedPort(portId);
    }
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
        lock (_gate)
        {
            var carrier = Carrier(carrierId); portId = carrier.PortId;
            Require(carrier.AccessingStatus is CarrierAccessingStatus.CarrierComplete or CarrierAccessingStatus.CarrierStopped || carrier.IdStatus == CarrierIdStatus.VerificationFailed || carrier.SlotMapStatus == CarrierSlotMapStatus.VerificationFailed, "Carrier is not complete, stopped, or rejected.");
            var port = Port(portId); Require(port.Transfer == LoadPortTransferState.TransferBlocked, "The port cannot become ready to unload."); port.Transfer = LoadPortTransferState.ReadyToUnload;
        }
        ChangedPort(portId);
    }
    /// <inheritdoc />
    public void BeginUnload(string portId)
    {
        lock (_gate) { var port = Port(portId); Require(port.Transfer == LoadPortTransferState.ReadyToUnload && port.CarrierId is not null, "The port is not ready to unload."); port.Transfer = LoadPortTransferState.TransferBlocked; port.Operation = TransferOperation.Unloading; }
        ChangedPort(portId);
    }
    /// <inheritdoc />
    public void CompleteUnload(string portId)
    {
        string carrierId;
        lock (_gate)
        {
            var port = Port(portId); Require(port.Transfer == LoadPortTransferState.TransferBlocked && port.Operation == TransferOperation.Unloading, "No unload transfer is active.");
            if (port.CarrierId is not { } presentCarrierId) throw new InvalidOperationException("No carrier is associated.");
            carrierId = presentCarrierId; _carriers.Remove(carrierId); port.CarrierId = null; port.Association = CarrierAssociationState.NotAssociated; port.Reservation = LoadPortReservationState.NotReserved; port.Transfer = LoadPortTransferState.ReadyToLoad; port.Operation = TransferOperation.None;
        }
        ChangedCarrier(carrierId); ChangedPort(portId);
    }
    /// <inheritdoc />
    public LoadPortSnapshot GetLoadPort(string portId)
    {
        lock (_gate) { var value = Port(portId); return new(value.Id, value.Transfer, value.Access, value.Reservation, value.Association, value.CarrierId); }
    }
    /// <inheritdoc />
    public CarrierSnapshot GetCarrier(string carrierId)
    {
        lock (_gate) { var value = Carrier(carrierId); return new(value.Id, value.PortId, value.IdStatus, value.SlotMapStatus, value.AccessingStatus, value.SlotMap); }
    }
    private void UpdateCarrier(string id, Action<CarrierEntry> update) { lock (_gate) update(Carrier(id)); ChangedCarrier(id); }
    private PortEntry Port(string id) { ArgumentException.ThrowIfNullOrWhiteSpace(id); return _ports.TryGetValue(id, out var value) ? value : throw new KeyNotFoundException("The load port is not registered."); }
    private CarrierEntry Carrier(string id) { ArgumentException.ThrowIfNullOrWhiteSpace(id); return _carriers.TryGetValue(id, out var value) ? value : throw new KeyNotFoundException("The carrier is not registered."); }
    private void ChangedPort(string id) => _events.Record(Gem300EventKind.LoadPortChanged, id);
    private void ChangedCarrier(string id) => _events.Record(Gem300EventKind.CarrierChanged, id);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private enum TransferOperation { None, Loading, Unloading }
    private sealed class PortEntry(string id, LoadPortAccessMode access) { public string Id { get; } = id; public LoadPortTransferState Transfer { get; set; } = LoadPortTransferState.OutOfService; public LoadPortAccessMode Access { get; set; } = access; public LoadPortReservationState Reservation { get; set; } public CarrierAssociationState Association { get; set; } public string? CarrierId { get; set; } public TransferOperation Operation { get; set; } }
    private sealed class CarrierEntry(string id, string portId, int capacity) { public string Id { get; } = id; public string PortId { get; } = portId; public int Capacity { get; } = capacity; public CarrierIdStatus IdStatus { get; set; } = CarrierIdStatus.IdNotRead; public CarrierSlotMapStatus SlotMapStatus { get; set; } = CarrierSlotMapStatus.SlotMapNotRead; public CarrierAccessingStatus AccessingStatus { get; set; } = CarrierAccessingStatus.NotAccessed; public CarrierSlotState[] SlotMap { get; set; } = Enumerable.Repeat(CarrierSlotState.Undefined, capacity).ToArray(); }
}
