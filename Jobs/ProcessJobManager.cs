using Dreamine.Gem.Abstractions.Interfaces;
using Dreamine.Gem.Abstractions.Model;
using Dreamine.Gem300.Abstractions.Interfaces;
using Dreamine.Gem300.Abstractions.Model;
using Dreamine.Gem300.Abstractions.States;
using Dreamine.Gem300.Infrastructure;
using Dreamine.Gem300.Substrate;

namespace Dreamine.Gem300.Jobs;

/// <summary>\if KO E40-0312 Process Job leaf 상태와 Recipe·Material 존재 조건을 관리합니다. \endif \if EN Manages E40-0312 process-job leaf states and recipe/material existence conditions. \endif</summary>
public sealed class ProcessJobManager : IProcessJobManager, IProcessJobOwnershipStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _jobs = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _processJobOwners = new(StringComparer.Ordinal);
    private readonly ISubstrateLeaseStore _substrateLeases;
    private readonly SubstrateTracker _substrates;
    private readonly IGemProcessProgramService _programs;
    private readonly Gem300EventPublisher _eventPublisher;
    /// <summary>\if KO 기판, 공정 프로그램 및 이벤트 경계로 관리자를 만듭니다. \endif \if EN Creates the manager with substrate, process-program, and event boundaries. \endif</summary>
    public ProcessJobManager(ISubstrateTracker substrates, IGemProcessProgramService programs, IGem300EventJournal events)
        : this(substrates, programs, new Gem300EventPublisher(events ?? throw new ArgumentNullException(nameof(events)))) { }
    internal ProcessJobManager(ISubstrateTracker substrates, IGemProcessProgramService programs, Gem300EventPublisher eventPublisher)
    {
        ArgumentNullException.ThrowIfNull(substrates);
        _substrates = substrates as SubstrateTracker ?? throw new NotSupportedException("Atomic process-job reference leases require the built-in SubstrateTracker.");
        _substrateLeases = _substrates;
        _programs = programs ?? throw new ArgumentNullException(nameof(programs)); _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher));
    }
    internal SubstrateTracker SubstrateStore => _substrates;
    internal Gem300DomainGate DomainGate => _substrates.DomainGate;
    /// <summary>\if KO 이 관리자가 사용하는 비차단 이벤트 게시기 상태입니다. \endif \if EN Gets the non-throwing event-publisher health used by this manager. \endif</summary>
    public Gem300EventPublisherHealth EventHealth => _eventPublisher.GetHealth();
    /// <inheritdoc />
    public void Create(ProcessJobDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!_programs.TryGet(definition.RecipeId, out var program) || program is null || !StringComparer.Ordinal.Equals(program.Id, definition.RecipeId)) throw new InvalidOperationException("The process program is not registered under the requested recipe identity.");
        lock (_gate)
        {
            if (_jobs.ContainsKey(definition.Id)) throw new InvalidOperationException("The process job already exists.");
            _substrateLeases.Acquire(SubstrateOwner(definition.Id), definition.MaterialIds);
            try { _jobs.Add(definition.Id, new(definition, program)); }
            catch { _substrateLeases.Release(SubstrateOwner(definition.Id), definition.MaterialIds); throw; }
        }
        Changed(definition.Id);
    }
    /// <inheritdoc />
    public void Allocate(string id) => Move(id, ProcessJobState.Queued, ProcessJobState.SettingUp);
    /// <inheritdoc />
    public void CompleteSetup(string id) => Update(id, entry => { Require(entry.State == ProcessJobState.SettingUp, "Setup is not active."); entry.State = entry.Definition.ManualStart ? ProcessJobState.WaitingForStart : ProcessJobState.Processing; });
    /// <inheritdoc />
    public void Start(string id) => Move(id, ProcessJobState.WaitingForStart, ProcessJobState.Processing);
    /// <inheritdoc />
    public void Pause(string id) => Move(id, ProcessJobState.Processing, ProcessJobState.Pausing);
    /// <inheritdoc />
    public void ConfirmPaused(string id) => Move(id, ProcessJobState.Pausing, ProcessJobState.Paused);
    /// <inheritdoc />
    public void Resume(string id) => Move(id, ProcessJobState.Paused, ProcessJobState.Processing);
    /// <inheritdoc />
    public void Stop(string id) => Update(id, entry => { Require(entry.State is ProcessJobState.SettingUp or ProcessJobState.WaitingForStart or ProcessJobState.Processing or ProcessJobState.Pausing or ProcessJobState.Paused, "The process job cannot stop."); entry.State = ProcessJobState.Stopping; });
    /// <inheritdoc />
    public void ConfirmStopped(string id) => Move(id, ProcessJobState.Stopping, ProcessJobState.Stopped);
    /// <inheritdoc />
    public void Abort(string id) => Update(id, entry => { Require(entry.State is ProcessJobState.SettingUp or ProcessJobState.WaitingForStart or ProcessJobState.Processing or ProcessJobState.Pausing or ProcessJobState.Paused or ProcessJobState.Stopping, "The process job cannot abort."); entry.State = ProcessJobState.Aborting; });
    /// <inheritdoc />
    public void ConfirmAborted(string id) => Move(id, ProcessJobState.Aborting, ProcessJobState.Aborted);
    /// <inheritdoc />
    public void Complete(string id) => Move(id, ProcessJobState.Processing, ProcessJobState.ProcessComplete);
    /// <inheritdoc />
    public void Delete(string id)
    {
        lock (_gate)
        {
            var entry = Job(id); Require(entry.State is ProcessJobState.Queued or ProcessJobState.ProcessComplete or ProcessJobState.Stopped or ProcessJobState.Aborted, "Only queued or post-active jobs can be deleted.");
            Require(!_processJobOwners.ContainsKey(id), "A process job claimed by a control job cannot be deleted.");
            _substrateLeases.Release(SubstrateOwner(id), entry.Definition.MaterialIds); _jobs.Remove(id);
        }
        Changed(id);
    }
    /// <inheritdoc />
    public ProcessJobSnapshot Get(string id) { lock (_gate) { var entry = Job(id); return new(entry.Definition, entry.State, entry.Program); } }
    /// <summary>\if KO Process Job 스냅샷을 ID 순서로 반환합니다. \endif \if EN Returns process-job snapshots in stable ID order. \endif</summary>
    public IReadOnlyList<ProcessJobSnapshot> GetSnapshot()
    {
        lock (_gate) return _jobs.Values.OrderBy(static value => value.Definition.Id, StringComparer.Ordinal).Select(static entry => new ProcessJobSnapshot(entry.Definition, entry.State, entry.Program)).ToArray();
    }
    internal void Claim(string ownerId, IReadOnlyList<string> processJobIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId); ArgumentNullException.ThrowIfNull(processJobIds);
        lock (_gate)
        {
            foreach (var id in processJobIds) { _ = Job(id); if (_processJobOwners.ContainsKey(id)) throw new InvalidOperationException("A process job is already assigned to another control job."); }
            foreach (var id in processJobIds) _processJobOwners.Add(id, ownerId);
        }
    }
    internal void ReleaseClaim(string ownerId, IReadOnlyList<string> processJobIds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId); ArgumentNullException.ThrowIfNull(processJobIds);
        lock (_gate)
        {
            foreach (var id in processJobIds)
            {
                var entry = Job(id);
                if (!_processJobOwners.TryGetValue(id, out var owner) || !StringComparer.Ordinal.Equals(owner, ownerId)) throw new InvalidOperationException("The control job does not own every referenced process job.");
                if (!ProcessJobOwnershipStores.CanRelease(entry.State)) throw new InvalidOperationException("An active process job cannot be released by deleting its control job.");
            }
            foreach (var id in processJobIds) _processJobOwners.Remove(id);
        }
    }
    void IProcessJobOwnershipStore.Claim(string ownerId, IReadOnlyList<string> processJobIds) => Claim(ownerId, processJobIds);
    void IProcessJobOwnershipStore.Release(string ownerId, IReadOnlyList<string> processJobIds) => ReleaseClaim(ownerId, processJobIds);
    private void Move(string id, ProcessJobState expected, ProcessJobState next) => Update(id, entry => { Require(entry.State == expected, $"Expected {expected}, but state is {entry.State}."); entry.State = next; });
    private void Update(string id, Action<Entry> update) { lock (_gate) update(Job(id)); Changed(id); }
    private Entry Job(string id) { ArgumentException.ThrowIfNullOrWhiteSpace(id); return _jobs.TryGetValue(id, out var value) ? value : throw new KeyNotFoundException("The process job is not registered."); }
    private void Changed(string id) => _eventPublisher.TryRecord(Gem300EventKind.ProcessJobChanged, id);
    private static string SubstrateOwner(string id) => $"ProcessJob>{id}";
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Entry(ProcessJobDefinition definition, GemProcessProgram program) { public ProcessJobDefinition Definition { get; } = definition; public GemProcessProgram Program { get; } = program; public ProcessJobState State { get; set; } = ProcessJobState.Queued; }
}
