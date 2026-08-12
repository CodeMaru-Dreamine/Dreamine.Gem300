using Dreamine.Gem300.Abstractions.Interfaces;
using Dreamine.Gem300.Abstractions.Model;
using Dreamine.Gem300.Abstractions.States;
using Dreamine.Gem300.Infrastructure;

namespace Dreamine.Gem300.Jobs;

/// <summary>\if KO E94-0314의 직렬 Control Job Queue와 상태 전이를 관리합니다. \endif \if EN Manages the E94-0314 serial control-job queue and state transitions. \endif</summary>
public sealed class ControlJobManager : IControlJobManager
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _jobs = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _queue = new();
    private readonly Dictionary<string, string> _workflowExecutions = new(StringComparer.Ordinal);
    private readonly IProcessJobManager _processJobs;
    private readonly ProcessJobManager _processJobStore;
    private readonly IProcessJobOwnershipStore _ownership;
    private readonly Gem300EventPublisher _eventPublisher;
    /// <summary>\if KO Process Job과 이벤트 경계로 관리자를 만듭니다. \endif \if EN Creates the manager with process-job and event boundaries. \endif</summary>
    public ControlJobManager(IProcessJobManager processJobs, IGem300EventJournal events)
        : this(processJobs, new Gem300EventPublisher(events ?? throw new ArgumentNullException(nameof(events)))) { }
    internal ControlJobManager(IProcessJobManager processJobs, Gem300EventPublisher eventPublisher)
    {
        ArgumentNullException.ThrowIfNull(processJobs);
        _processJobStore = processJobs as ProcessJobManager ?? throw new NotSupportedException("Atomic control-job ownership requires the built-in ProcessJobManager.");
        _processJobs = _processJobStore; _ownership = _processJobStore; _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
    }
    internal ProcessJobManager ProcessJobStore => _processJobStore;
    /// <summary>\if KO 이 관리자가 사용하는 비차단 이벤트 게시기 상태입니다. \endif \if EN Gets the non-throwing event-publisher health used by this manager. \endif</summary>
    public Gem300EventPublisherHealth EventHealth => _eventPublisher.GetHealth();
    /// <inheritdoc />
    public void Create(ControlJobDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        lock (_gate)
        {
            if (_jobs.ContainsKey(definition.Id)) throw new InvalidOperationException("The control job already exists.");
            _ownership.Claim(definition.Id, definition.ProcessJobIds);
            try { _jobs.Add(definition.Id, new(definition)); _queue.AddLast(definition.Id); }
            catch { _ownership.Release(definition.Id, definition.ProcessJobIds); throw; }
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
        lock (_gate)
        {
            var entry = Job(id); Require(entry.State is ControlJobState.Queued or ControlJobState.Completed, "Only queued or completed jobs can be deleted.");
            _ownership.Release(entry.Definition.Id, entry.Definition.ProcessJobIds);
            if (entry.State == ControlJobState.Queued) _queue.Remove(id); _jobs.Remove(id);
        }
        Changed(id);
    }
    /// <inheritdoc />
    public ControlJobSnapshot Get(string id) { lock (_gate) { var entry = Job(id); return new(entry.Definition, entry.State, entry.CurrentIndex); } }
    /// <summary>\if KO Control Job 스냅샷을 ID 순서로 반환합니다. \endif \if EN Returns control-job snapshots in stable ID order. \endif</summary>
    public IReadOnlyList<ControlJobSnapshot> GetSnapshot()
    {
        lock (_gate) return _jobs.Values.OrderBy(static value => value.Definition.Id, StringComparer.Ordinal).Select(static entry => new ControlJobSnapshot(entry.Definition, entry.State, entry.CurrentIndex)).ToArray();
    }
    internal void ClaimWorkflowExecution(string id, string ownerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        lock (_gate)
        {
            _ = Job(id);
            if (!_workflowExecutions.TryAdd(id, ownerId)) throw new InvalidOperationException("The control job already has an active workflow execution.");
        }
    }
    internal void ReleaseWorkflowExecution(string id, string ownerId)
    {
        lock (_gate) if (_workflowExecutions.TryGetValue(id, out var owner) && StringComparer.Ordinal.Equals(owner, ownerId)) _workflowExecutions.Remove(id);
    }
    private static bool IsPostActive(ProcessJobState state) => state is ProcessJobState.ProcessComplete or ProcessJobState.Stopped or ProcessJobState.Aborted;
    private void Move(string id, ControlJobState expected, ControlJobState next) => Update(id, entry => { Require(entry.State == expected, $"Expected {expected}, but state is {entry.State}."); entry.State = next; });
    private void Update(string id, Action<Entry> update) { lock (_gate) update(Job(id)); Changed(id); }
    private Entry Job(string id) { ArgumentException.ThrowIfNullOrWhiteSpace(id); return _jobs.TryGetValue(id, out var value) ? value : throw new KeyNotFoundException("The control job is not registered."); }
    private void Changed(string id) => _eventPublisher.TryRecord(Gem300EventKind.ControlJobChanged, id);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Entry(ControlJobDefinition definition) { public ControlJobDefinition Definition { get; } = definition; public ControlJobState State { get; set; } = ControlJobState.Queued; public int CurrentIndex { get; set; } = -1; }
}
