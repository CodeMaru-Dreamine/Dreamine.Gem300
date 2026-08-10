using Dreamine.Gem.Abstractions.Interfaces;
using Dreamine.Gem300.Abstractions.Interfaces;
using Dreamine.Gem300.Abstractions.Model;
using Dreamine.Gem300.Abstractions.States;

namespace Dreamine.Gem300.Jobs;

/// <summary>\if KO E40-0312 Process Job leaf 상태와 Recipe·Material 존재 조건을 관리합니다. \endif \if EN Manages E40-0312 process-job leaf states and recipe/material existence conditions. \endif</summary>
public sealed class ProcessJobManager : IProcessJobManager
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _jobs = new(StringComparer.Ordinal);
    private readonly ISubstrateTracker _substrates;
    private readonly IGemProcessProgramService _programs;
    private readonly IGem300EventJournal _events;
    /// <summary>\if KO 기판, 공정 프로그램 및 이벤트 경계로 관리자를 만듭니다. \endif \if EN Creates the manager with substrate, process-program, and event boundaries. \endif</summary>
    public ProcessJobManager(ISubstrateTracker substrates, IGemProcessProgramService programs, IGem300EventJournal events)
    { _substrates = substrates ?? throw new ArgumentNullException(nameof(substrates)); _programs = programs ?? throw new ArgumentNullException(nameof(programs)); _events = events ?? throw new ArgumentNullException(nameof(events)); }
    /// <inheritdoc />
    public void Create(ProcessJobDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!_programs.TryGet(definition.RecipeId, out _)) throw new InvalidOperationException("The process program is not registered.");
        foreach (var materialId in definition.MaterialIds) _ = _substrates.Get(materialId);
        lock (_gate) if (!_jobs.TryAdd(definition.Id, new(definition))) throw new InvalidOperationException("The process job already exists.");
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
        lock (_gate) { var entry = Job(id); Require(entry.State is ProcessJobState.Queued or ProcessJobState.ProcessComplete or ProcessJobState.Stopped or ProcessJobState.Aborted, "Only queued or post-active jobs can be deleted."); _jobs.Remove(id); }
        Changed(id);
    }
    /// <inheritdoc />
    public ProcessJobSnapshot Get(string id) { lock (_gate) { var entry = Job(id); return new(entry.Definition, entry.State); } }
    private void Move(string id, ProcessJobState expected, ProcessJobState next) => Update(id, entry => { Require(entry.State == expected, $"Expected {expected}, but state is {entry.State}."); entry.State = next; });
    private void Update(string id, Action<Entry> update) { lock (_gate) update(Job(id)); Changed(id); }
    private Entry Job(string id) { ArgumentException.ThrowIfNullOrWhiteSpace(id); return _jobs.TryGetValue(id, out var value) ? value : throw new KeyNotFoundException("The process job is not registered."); }
    private void Changed(string id) => _events.Record(Gem300EventKind.ProcessJobChanged, id);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Entry(ProcessJobDefinition definition) { public ProcessJobDefinition Definition { get; } = definition; public ProcessJobState State { get; set; } = ProcessJobState.Queued; }
}
