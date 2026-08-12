using Dreamine.Gem300.Abstractions.Interfaces;
using Dreamine.Gem300.Abstractions.Model;
using Dreamine.Gem300.Abstractions.States;
using Dreamine.Gem300.Infrastructure;

namespace Dreamine.Gem300.Substrate;

/// <summary>\if KO E90-0312의 기판 병렬 상태, 위치 점유 및 체류 이력을 원자적으로 관리합니다. \endif \if EN Atomically manages E90-0312 substrate orthogonal states, occupancy, and residence history. \endif</summary>
public sealed class SubstrateTracker : ISubstrateTracker, ISubstrateLeaseStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _substrates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _occupancy = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _leases = new(StringComparer.Ordinal);
    private readonly Gem300EventPublisher _eventPublisher;
    private readonly TimeProvider _timeProvider;
    private readonly Gem300DomainGate _domainGate;

    /// <summary>\if KO 이벤트 저널과 시간 공급자로 추적기를 만듭니다. \endif \if EN Creates the tracker with an event journal and time provider. \endif</summary>
    public SubstrateTracker(IGem300EventJournal events, TimeProvider? timeProvider = null)
        : this(new Gem300EventPublisher(events ?? throw new ArgumentNullException(nameof(events)), timeProvider), timeProvider, new()) { }

    internal SubstrateTracker(Gem300EventPublisher eventPublisher, TimeProvider? timeProvider = null, Gem300DomainGate? domainGate = null)
    {
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
        _timeProvider = timeProvider ?? TimeProvider.System; _domainGate = domainGate ?? new();
    }

    internal Gem300DomainGate DomainGate => _domainGate;
    /// <summary>\if KO 이 관리자가 사용하는 비차단 이벤트 게시기 상태입니다. \endif \if EN Gets the non-throwing event-publisher health used by this manager. \endif</summary>
    public Gem300EventPublisherHealth EventHealth => _eventPublisher.GetHealth();

    /// <inheritdoc />
    public void Register(string substrateId, string sourceLocation, string destinationLocation, bool idConfirmed = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(substrateId); ArgumentException.ThrowIfNullOrWhiteSpace(sourceLocation); ArgumentException.ThrowIfNullOrWhiteSpace(destinationLocation);
        var now = _timeProvider.GetUtcNow();
        lock (_domainGate.SyncRoot) lock (_gate)
        {
            if (_substrates.ContainsKey(substrateId)) throw new InvalidOperationException("The substrate ID is already registered.");
            if (_occupancy.ContainsKey(sourceLocation)) throw new InvalidOperationException("The source location is occupied.");
            _substrates.Add(substrateId, new(substrateId, sourceLocation, destinationLocation, idConfirmed ? SubstrateIdStatus.Confirmed : SubstrateIdStatus.NotConfirmed, now));
            _occupancy.Add(sourceLocation, substrateId);
        }
        Changed(substrateId);
    }

    /// <inheritdoc />
    public void ConfirmId(string substrateId) => Update(substrateId, entry => { Require(entry.IdStatus is SubstrateIdStatus.NotConfirmed or SubstrateIdStatus.WaitingForHost, "The substrate ID cannot be confirmed."); entry.IdStatus = SubstrateIdStatus.Confirmed; });
    /// <inheritdoc />
    public void RejectId(string substrateId) => Update(substrateId, entry => { Require(entry.IdStatus is SubstrateIdStatus.NotConfirmed or SubstrateIdStatus.WaitingForHost, "The substrate ID cannot be rejected."); entry.IdStatus = SubstrateIdStatus.ConfirmationFailed; });

    /// <inheritdoc />
    public void Move(string substrateId, string locationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(substrateId); ArgumentException.ThrowIfNullOrWhiteSpace(locationId);
        var now = _timeProvider.GetUtcNow();
        lock (_domainGate.SyncRoot) lock (_gate)
        {
            var entry = Substrate(substrateId); Require(entry.Processing != SubstrateProcessingState.Lost, "A lost substrate cannot move."); Require(!StringComparer.Ordinal.Equals(entry.CurrentLocation, locationId), "The substrate is already at this location.");
            if (_occupancy.TryGetValue(locationId, out var occupant) && !StringComparer.Ordinal.Equals(occupant, substrateId)) throw new InvalidOperationException("The destination location is occupied.");
            entry.CloseCurrent(now); _occupancy.Remove(entry.CurrentLocation); entry.CurrentLocation = locationId; _occupancy[locationId] = substrateId; entry.History.Add(new(locationId, now, null));
            entry.Transport = StringComparer.Ordinal.Equals(locationId, entry.Source) ? SubstrateTransportState.AtSource : StringComparer.Ordinal.Equals(locationId, entry.Destination) ? SubstrateTransportState.AtDestination : SubstrateTransportState.AtWork;
        }
        Changed(substrateId);
    }

    /// <inheritdoc />
    public void BeginProcessing(string substrateId) => Update(substrateId, entry => { Require(entry.IdStatus == SubstrateIdStatus.Confirmed && entry.Processing == SubstrateProcessingState.NeedsProcessing, "The substrate cannot begin processing."); entry.Processing = SubstrateProcessingState.InProcess; });
    /// <inheritdoc />
    public void CompleteProcessing(string substrateId, SubstrateProcessingState result)
    {
        if (!Enum.IsDefined(result) || result is SubstrateProcessingState.NeedsProcessing or SubstrateProcessingState.InProcess or SubstrateProcessingState.Lost) throw new ArgumentOutOfRangeException(nameof(result));
        Update(substrateId, entry => { Require(entry.Processing == SubstrateProcessingState.InProcess || result == SubstrateProcessingState.Skipped && entry.Processing == SubstrateProcessingState.NeedsProcessing, "The substrate is not in a compatible processing state."); entry.Processing = result; });
    }

    /// <inheritdoc />
    public void MarkLost(string substrateId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(substrateId); var now = _timeProvider.GetUtcNow();
        lock (_domainGate.SyncRoot) lock (_gate)
        {
            var entry = Substrate(substrateId); Require(entry.Processing != SubstrateProcessingState.Lost, "The substrate is already lost."); entry.CloseCurrent(now); entry.Processing = SubstrateProcessingState.Lost; _occupancy.Remove(entry.CurrentLocation);
        }
        Changed(substrateId);
    }

    /// <inheritdoc />
    public void Remove(string substrateId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(substrateId); var now = _timeProvider.GetUtcNow();
        lock (_domainGate.SyncRoot) lock (_gate)
        {
            var entry = Substrate(substrateId); EnsureRemovable(entry);
            if (_leases.TryGetValue(substrateId, out var owners) && owners.Count != 0) throw new InvalidOperationException("A referenced substrate cannot be removed while a lease is active.");
            entry.CloseCurrent(now); _occupancy.Remove(entry.CurrentLocation); _substrates.Remove(substrateId);
        }
        Changed(substrateId);
    }

    /// <inheritdoc />
    public SubstrateSnapshot Get(string substrateId)
    {
        lock (_domainGate.SyncRoot) lock (_gate) { var entry = Substrate(substrateId); return Snapshot(entry); }
    }

    /// <inheritdoc />
    public bool TryGet(string substrateId, out SubstrateSnapshot? substrate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(substrateId);
        lock (_domainGate.SyncRoot) lock (_gate)
        {
            if (!_substrates.TryGetValue(substrateId, out var entry)) { substrate = null; return false; }
            substrate = Snapshot(entry); return true;
        }
    }

    /// <inheritdoc />
    public MaterialLocationState GetLocationState(string locationId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(locationId);
        lock (_domainGate.SyncRoot) lock (_gate) return _occupancy.ContainsKey(locationId) ? MaterialLocationState.Occupied : MaterialLocationState.Unoccupied;
    }

    /// <summary>\if KO 기판 스냅샷을 ID 순서로 반환합니다. \endif \if EN Returns substrate snapshots in stable ID order. \endif</summary>
    public IReadOnlyList<SubstrateSnapshot> GetSnapshot()
    {
        lock (_domainGate.SyncRoot) lock (_gate) return _substrates.Values.OrderBy(static value => value.Id, StringComparer.Ordinal).Select(Snapshot).ToArray();
    }

    /// <summary>\if KO 기판의 현재 lease 소유자를 안정적인 순서로 반환합니다. \endif \if EN Returns current substrate-lease owners in stable order. \endif</summary>
    public IReadOnlyList<string> GetLeaseOwners(string substrateId)
    {
        lock (_domainGate.SyncRoot) lock (_gate) { _ = Substrate(substrateId); return _leases.TryGetValue(substrateId, out var owners) ? owners.Order(StringComparer.Ordinal).ToArray() : []; }
    }

    void ISubstrateLeaseStore.Acquire(string ownerId, IReadOnlyList<string> substrateIds)
    {
        ValidateLeaseArguments(ownerId, substrateIds);
        lock (_domainGate.SyncRoot) lock (_gate)
        {
            foreach (var id in substrateIds)
            {
                _ = Substrate(id);
                if (_leases.TryGetValue(id, out var existing))
                {
                    if (existing.Contains(ownerId)) throw new InvalidOperationException("The substrate lease is already held by this owner.");
                }
            }
            foreach (var id in substrateIds)
            {
                if (!_leases.TryGetValue(id, out var owners)) _leases.Add(id, owners = new(StringComparer.Ordinal));
                owners.Add(ownerId);
            }
        }
    }

    void ISubstrateLeaseStore.Release(string ownerId, IReadOnlyList<string> substrateIds)
    {
        ValidateLeaseArguments(ownerId, substrateIds);
        lock (_domainGate.SyncRoot) lock (_gate)
        {
            EnsureOwned(ownerId, substrateIds, false);
            foreach (var id in substrateIds) { var owners = _leases[id]; owners.Remove(ownerId); if (owners.Count == 0) _leases.Remove(id); }
        }
    }

    internal IReadOnlyList<PreparedSubstrateArrival> PrepareArrival(IReadOnlyList<SubstrateArrivalPlan> plans)
    {
        ArgumentNullException.ThrowIfNull(plans);
        var prepared = new PreparedSubstrateArrival[plans.Count];
        for (var index = 0; index < plans.Count; index++) prepared[index] = new(plans[index], _timeProvider.GetUtcNow());
        return prepared;
    }

    internal void ValidateArrivalCore(string ownerId, IReadOnlyList<PreparedSubstrateArrival> prepared)
    {
        ValidateLeaseArguments(ownerId, prepared.Select(static item => item.Plan.SubstrateId).ToArray());
        lock (_gate)
        {
            foreach (var item in prepared)
            {
                if (_substrates.ContainsKey(item.Plan.SubstrateId)) throw new InvalidOperationException("A substrate ID is already registered.");
                if (_occupancy.ContainsKey(item.Plan.SourceLocation)) throw new InvalidOperationException("A source location is occupied.");
            }
        }
    }

    internal void CommitArrivalCore(string ownerId, IReadOnlyList<PreparedSubstrateArrival> prepared)
    {
        lock (_gate)
        {
            foreach (var item in prepared)
            {
                var plan = item.Plan; _substrates.Add(plan.SubstrateId, new(plan.SubstrateId, plan.SourceLocation, plan.DestinationLocation, SubstrateIdStatus.Confirmed, item.RegisteredAt));
                _occupancy.Add(plan.SourceLocation, plan.SubstrateId); _leases.Add(plan.SubstrateId, new(StringComparer.Ordinal) { ownerId });
            }
        }
    }

    internal void RollbackArrivalCore(string ownerId, IReadOnlyList<string> substrateIds)
    {
        lock (_gate)
        {
            foreach (var id in substrateIds)
            {
                if (!_substrates.TryGetValue(id, out var entry) || !_leases.TryGetValue(id, out var owners) || owners.Count != 1 || !owners.Contains(ownerId)) continue;
                _occupancy.Remove(entry.CurrentLocation); _leases.Remove(id); _substrates.Remove(id);
            }
        }
    }

    internal void ValidateRemoveOwnedCore(string ownerId, IReadOnlyList<string> substrateIds)
    {
        ValidateLeaseArguments(ownerId, substrateIds);
        lock (_gate) { EnsureOwned(ownerId, substrateIds, true); foreach (var id in substrateIds) EnsureRemovable(Substrate(id)); }
    }

    internal void RemoveOwnedCore(string ownerId, IReadOnlyList<string> substrateIds, DateTimeOffset removedAt)
    {
        lock (_gate)
        {
            foreach (var id in substrateIds)
            {
                var entry = Substrate(id); entry.CloseCurrent(removedAt); _occupancy.Remove(entry.CurrentLocation); _substrates.Remove(id); _leases.Remove(id);
            }
        }
    }

    internal DateTimeOffset CaptureTime() => _timeProvider.GetUtcNow();
    internal void PublishChanges(IEnumerable<string> ids) { foreach (var id in ids) Changed(id); }

    private void Update(string id, Action<Entry> action)
    {
        lock (_domainGate.SyncRoot) lock (_gate) action(Substrate(id));
        Changed(id);
    }

    private Entry Substrate(string id) { ArgumentException.ThrowIfNullOrWhiteSpace(id); return _substrates.TryGetValue(id, out var value) ? value : throw new KeyNotFoundException("The substrate is not registered."); }
    private void Changed(string id) => _eventPublisher.TryRecord(Gem300EventKind.SubstrateChanged, id);
    private static SubstrateSnapshot Snapshot(Entry entry) => new(entry.Id, entry.Source, entry.Destination, entry.CurrentLocation, entry.Transport, entry.Processing, entry.IdStatus, entry.History);
    private static void ValidateLeaseArguments(string ownerId, IReadOnlyList<string> substrateIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId); ArgumentNullException.ThrowIfNull(substrateIds);
        if (substrateIds.Any(string.IsNullOrWhiteSpace) || substrateIds.Distinct(StringComparer.Ordinal).Count() != substrateIds.Count) throw new ArgumentException("Substrate IDs must be unique and non-blank.", nameof(substrateIds));
    }

    private void EnsureOwned(string ownerId, IReadOnlyList<string> substrateIds, bool exclusive)
    {
        foreach (var id in substrateIds)
            if (!_leases.TryGetValue(id, out var owners) || !owners.Contains(ownerId) || exclusive && owners.Count != 1) throw new InvalidOperationException("The substrate lease is missing or shared by another owner.");
    }

    private static void EnsureRemovable(Entry entry)
    {
        Require(entry.Transport == SubstrateTransportState.AtDestination || entry.Processing == SubstrateProcessingState.Lost, "Only a destination or lost substrate can be removed.");
        if (entry.Processing is SubstrateProcessingState.NeedsProcessing or SubstrateProcessingState.InProcess) throw new InvalidOperationException("Active processing prevents removal.");
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    internal sealed class PreparedSubstrateArrival(SubstrateArrivalPlan plan, DateTimeOffset registeredAt)
    {
        public SubstrateArrivalPlan Plan { get; } = plan ?? throw new ArgumentNullException(nameof(plan));
        public DateTimeOffset RegisteredAt { get; } = registeredAt;
    }

    private sealed class Entry
    {
        public Entry(string id, string source, string destination, SubstrateIdStatus idStatus, DateTimeOffset now) { Id = id; Source = source; Destination = destination; CurrentLocation = source; IdStatus = idStatus; History.Add(new(source, now, null)); }
        public string Id { get; } public string Source { get; } public string Destination { get; } public string CurrentLocation { get; set; }
        public SubstrateTransportState Transport { get; set; } = SubstrateTransportState.AtSource; public SubstrateProcessingState Processing { get; set; } = SubstrateProcessingState.NeedsProcessing; public SubstrateIdStatus IdStatus { get; set; }
        public List<SubstrateLocationHistory> History { get; } = [];
        public void CloseCurrent(DateTimeOffset now) { var current = History[^1]; if (current.TimeOut is null) History[^1] = new(current.LocationId, current.TimeIn, now); }
    }
}
