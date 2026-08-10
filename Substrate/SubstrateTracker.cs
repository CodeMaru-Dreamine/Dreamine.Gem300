using Dreamine.Gem300.Abstractions.Interfaces;
using Dreamine.Gem300.Abstractions.Model;
using Dreamine.Gem300.Abstractions.States;

namespace Dreamine.Gem300.Substrate;

/// <summary>\if KO E90-0312의 기판 병렬 상태, 위치 점유 및 체류 이력을 원자적으로 관리합니다. \endif \if EN Atomically manages E90-0312 substrate orthogonal states, occupancy, and residence history. \endif</summary>
public sealed class SubstrateTracker : ISubstrateTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _substrates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _occupancy = new(StringComparer.Ordinal);
    private readonly IGem300EventJournal _events;
    private readonly TimeProvider _timeProvider;
    /// <summary>\if KO 이벤트 저널과 시간 공급자로 추적기를 만듭니다. \endif \if EN Creates the tracker with an event journal and time provider. \endif</summary>
    public SubstrateTracker(IGem300EventJournal events, TimeProvider? timeProvider = null) { _events = events ?? throw new ArgumentNullException(nameof(events)); _timeProvider = timeProvider ?? TimeProvider.System; }
    /// <inheritdoc />
    public void Register(string substrateId, string sourceLocation, string destinationLocation, bool idConfirmed = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(substrateId); ArgumentException.ThrowIfNullOrWhiteSpace(sourceLocation); ArgumentException.ThrowIfNullOrWhiteSpace(destinationLocation);
        lock (_gate)
        {
            if (_substrates.ContainsKey(substrateId)) throw new InvalidOperationException("The substrate ID is already registered.");
            if (_occupancy.ContainsKey(sourceLocation)) throw new InvalidOperationException("The source location is occupied.");
            var entry = new Entry(substrateId, sourceLocation, destinationLocation, idConfirmed ? SubstrateIdStatus.Confirmed : SubstrateIdStatus.NotConfirmed, _timeProvider.GetUtcNow());
            _substrates.Add(substrateId, entry); _occupancy.Add(sourceLocation, substrateId);
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
        ArgumentException.ThrowIfNullOrWhiteSpace(locationId);
        lock (_gate)
        {
            var entry = Substrate(substrateId); Require(entry.Processing != SubstrateProcessingState.Lost, "A lost substrate cannot move."); Require(!StringComparer.Ordinal.Equals(entry.CurrentLocation, locationId), "The substrate is already at this location.");
            if (_occupancy.TryGetValue(locationId, out var occupant) && !StringComparer.Ordinal.Equals(occupant, substrateId)) throw new InvalidOperationException("The destination location is occupied.");
            var now = _timeProvider.GetUtcNow(); entry.CloseCurrent(now); _occupancy.Remove(entry.CurrentLocation); entry.CurrentLocation = locationId; _occupancy[locationId] = substrateId; entry.History.Add(new(locationId, now, null));
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
        lock (_gate)
        {
            var entry = Substrate(substrateId); Require(entry.Processing != SubstrateProcessingState.Lost, "The substrate is already lost."); entry.Processing = SubstrateProcessingState.Lost; entry.CloseCurrent(_timeProvider.GetUtcNow()); _occupancy.Remove(entry.CurrentLocation);
        }
        Changed(substrateId);
    }
    /// <inheritdoc />
    public void Remove(string substrateId)
    {
        lock (_gate)
        {
            var entry = Substrate(substrateId); Require(entry.Transport == SubstrateTransportState.AtDestination || entry.Processing == SubstrateProcessingState.Lost, "Only a destination or lost substrate can be removed.");
            if (entry.Processing is SubstrateProcessingState.NeedsProcessing or SubstrateProcessingState.InProcess) throw new InvalidOperationException("Active processing prevents removal.");
            entry.CloseCurrent(_timeProvider.GetUtcNow()); _occupancy.Remove(entry.CurrentLocation); _substrates.Remove(substrateId);
        }
        Changed(substrateId);
    }
    /// <inheritdoc />
    public SubstrateSnapshot Get(string substrateId)
    {
        lock (_gate) { var entry = Substrate(substrateId); return new(entry.Id, entry.Source, entry.Destination, entry.CurrentLocation, entry.Transport, entry.Processing, entry.IdStatus, entry.History); }
    }
    /// <inheritdoc />
    public bool TryGet(string substrateId, out SubstrateSnapshot? substrate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(substrateId);
        lock (_gate)
        {
            if (!_substrates.TryGetValue(substrateId, out var entry)) { substrate = null; return false; }
            substrate = new(entry.Id, entry.Source, entry.Destination, entry.CurrentLocation, entry.Transport, entry.Processing, entry.IdStatus, entry.History);
            return true;
        }
    }
    /// <inheritdoc />
    public MaterialLocationState GetLocationState(string locationId) { ArgumentException.ThrowIfNullOrWhiteSpace(locationId); lock (_gate) return _occupancy.ContainsKey(locationId) ? MaterialLocationState.Occupied : MaterialLocationState.Unoccupied; }
    private void Update(string id, Action<Entry> action) { lock (_gate) action(Substrate(id)); Changed(id); }
    private Entry Substrate(string id) { ArgumentException.ThrowIfNullOrWhiteSpace(id); return _substrates.TryGetValue(id, out var value) ? value : throw new KeyNotFoundException("The substrate is not registered."); }
    private void Changed(string id) => _events.Record(Gem300EventKind.SubstrateChanged, id);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Entry
    {
        public Entry(string id, string source, string destination, SubstrateIdStatus idStatus, DateTimeOffset now) { Id = id; Source = source; Destination = destination; CurrentLocation = source; IdStatus = idStatus; History.Add(new(source, now, null)); }
        public string Id { get; } public string Source { get; } public string Destination { get; } public string CurrentLocation { get; set; }
        public SubstrateTransportState Transport { get; set; } = SubstrateTransportState.AtSource; public SubstrateProcessingState Processing { get; set; } = SubstrateProcessingState.NeedsProcessing; public SubstrateIdStatus IdStatus { get; set; }
        public List<SubstrateLocationHistory> History { get; } = [];
        public void CloseCurrent(DateTimeOffset now) { var current = History[^1]; if (current.TimeOut is null) History[^1] = new(current.LocationId, current.TimeIn, now); }
    }
}
