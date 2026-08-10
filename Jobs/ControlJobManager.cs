using Dreamine.Gem300.Abstractions.Interfaces;
using Dreamine.Gem300.Abstractions.Model;
using Dreamine.Gem300.Abstractions.States;

namespace Dreamine.Gem300.Jobs;

/// <summary>\if KO E94-0314의 직렬 Control Job Queue와 상태 전이를 관리합니다. \endif \if EN Manages the E94-0314 serial control-job queue and state transitions. \endif</summary>
public sealed class ControlJobManager : IControlJobManager
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _jobs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _processJobOwners = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _queue = new();
    private readonly IProcessJobManager _processJobs;
    private readonly IGem300EventJournal _events;
    /// <summary>\if KO Process Job과 이벤트 경계로 관리자를 만듭니다. \endif \if EN Creates the manager with process-job and event boundaries. \endif</summary>
    public ControlJobManager(IProcessJobManager processJobs, IGem300EventJournal events) { _processJobs = processJobs ?? throw new ArgumentNullException(nameof(processJobs)); _events = events ?? throw new ArgumentNullException(nameof(events)); }
    /// <inheritdoc />
    public void Create(ControlJobDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition); foreach (var id in definition.ProcessJobIds) _ = _processJobs.Get(id);
        lock (_gate)
        {
            if (_jobs.ContainsKey(definition.Id)) throw new InvalidOperationException("The control job already exists.");
            if (definition.ProcessJobIds.Any(_processJobOwners.ContainsKey)) throw new InvalidOperationException("A process job is already assigned to another control job.");
            _jobs.Add(definition.Id, new(definition)); foreach (var processId in definition.ProcessJobIds) _processJobOwners.Add(processId, definition.Id); _queue.AddLast(definition.Id);
        }
        Changed(definition.Id);
    }
    /// <inheritdoc />
    public void Select(string id)
    {
        lock (_gate)
        {
            var entry = Job(id); Require(entry.State == ControlJobState.Queued && _queue.First?.Value == id, "Only the queue-head job can be selected.");
            Require(!_jobs.Values.Any(static value => value.State is ControlJobState.Selected or ControlJobState.WaitingForStart or ControlJobState.Executing or ControlJobState.Paused), "Another control job is active.");
            _queue.RemoveFirst(); entry.State = ControlJobState.Selected;
        }
        Changed(id);
    }
    /// <inheritdoc />
    public void Ready(string id) => Update(id, entry => { Require(entry.State == ControlJobState.Selected, "The control job is not selected."); entry.CurrentIndex = 0; entry.State = entry.Definition.ManualStart ? ControlJobState.WaitingForStart : ControlJobState.Executing; });
    /// <inheritdoc />
    public void Start(string id) => Move(id, ControlJobState.WaitingForStart, ControlJobState.Executing);
    /// <inheritdoc />
    public void Pause(string id) => Move(id, ControlJobState.Executing, ControlJobState.Paused);
    /// <inheritdoc />
    public void Resume(string id) => Move(id, ControlJobState.Paused, ControlJobState.Executing);
    /// <inheritdoc />
    public void Advance(string id)
    {
        Update(id, entry =>
        {
            Require(entry.State == ControlJobState.Executing, "The control job is not executing.");
            var current = _processJobs.Get(entry.Definition.ProcessJobIds[entry.CurrentIndex]); Require(IsPostActive(current.State), "The current process job is not post-active.");
            Require(entry.CurrentIndex + 1 < entry.Definition.ProcessJobIds.Count, "There is no next process job."); entry.CurrentIndex++;
        });
    }
    /// <inheritdoc />
    public void Complete(string id)
    {
        Update(id, entry => { Require(entry.State is ControlJobState.Executing or ControlJobState.Paused, "The control job is not active."); Require(entry.Definition.ProcessJobIds.All(processId => IsPostActive(_processJobs.Get(processId).State)), "All process jobs must be post-active."); entry.State = ControlJobState.Completed; });
    }
    /// <inheritdoc />
    public void Abort(string id) => Update(id, entry => { Require(entry.State is ControlJobState.Selected or ControlJobState.WaitingForStart or ControlJobState.Executing or ControlJobState.Paused, "The control job cannot abort."); entry.State = ControlJobState.Completed; });
    /// <inheritdoc />
    public void Delete(string id)
    {
        lock (_gate) { var entry = Job(id); Require(entry.State is ControlJobState.Queued or ControlJobState.Completed, "Only queued or completed jobs can be deleted."); if (entry.State == ControlJobState.Queued) _queue.Remove(id); foreach (var processId in entry.Definition.ProcessJobIds) _processJobOwners.Remove(processId); _jobs.Remove(id); }
        Changed(id);
    }
    /// <inheritdoc />
    public ControlJobSnapshot Get(string id) { lock (_gate) { var entry = Job(id); return new(entry.Definition, entry.State, entry.CurrentIndex); } }
    private static bool IsPostActive(ProcessJobState state) => state is ProcessJobState.ProcessComplete or ProcessJobState.Stopped or ProcessJobState.Aborted;
    private void Move(string id, ControlJobState expected, ControlJobState next) => Update(id, entry => { Require(entry.State == expected, $"Expected {expected}, but state is {entry.State}."); entry.State = next; });
    private void Update(string id, Action<Entry> update) { lock (_gate) update(Job(id)); Changed(id); }
    private Entry Job(string id) { ArgumentException.ThrowIfNullOrWhiteSpace(id); return _jobs.TryGetValue(id, out var value) ? value : throw new KeyNotFoundException("The control job is not registered."); }
    private void Changed(string id) => _events.Record(Gem300EventKind.ControlJobChanged, id);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Entry(ControlJobDefinition definition) { public ControlJobDefinition Definition { get; } = definition; public ControlJobState State { get; set; } = ControlJobState.Queued; public int CurrentIndex { get; set; } = -1; }
}
