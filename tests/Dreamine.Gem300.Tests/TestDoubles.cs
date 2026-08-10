using Dreamine.Communication.Abstractions.Enums;
using Dreamine.Gem.Abstractions.Interfaces;
using Dreamine.Gem.Abstractions.Model;
using Dreamine.Secs.Abstractions.Interfaces;
using Dreamine.Secs.Abstractions.Model;

namespace Dreamine.Gem300.Tests;

internal sealed class FakeProcessPrograms : IGemProcessProgramService
{
    private readonly Dictionary<string, GemProcessProgram> _programs = new(StringComparer.Ordinal);
    public void Put(GemProcessProgram program) => _programs[program.Id] = program;
    public bool TryGet(string id, out GemProcessProgram? program) => _programs.TryGetValue(id, out program);
    public bool Delete(string id) => _programs.Remove(id);
    public IReadOnlyList<string> GetIds() => _programs.Keys.Order(StringComparer.Ordinal).ToArray();
}

internal sealed class FakeGemRuntime : IGemRuntime
{
    public ISecsConnection SecsConnection { get; } = new FakeConnection();
    private sealed class FakeConnection : ISecsConnection
    {
        public string ProviderKey => "test"; public ConnectionState State => ConnectionState.Connected;
        public Task ConnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = new();
    private DateTimeOffset _now = DateTimeOffset.UnixEpoch;
    public override DateTimeOffset GetUtcNow() { lock (_gate) return _now; }
    public override long GetTimestamp() { lock (_gate) return _now.UtcTicks; }
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state); lock (_gate) { _timers.Add(timer); timer.ChangeCore(dueTime, period); } return timer;
    }
    public void Advance(TimeSpan amount)
    {
        DateTimeOffset target; lock (_gate) target = _now + amount;
        while (true)
        {
            ManualTimer? timer;
            lock (_gate) { timer = _timers.Where(value => !value.Disposed && value.Due <= target).OrderBy(value => value.Due).FirstOrDefault(); if (timer is null) { _now = target; return; } _now = timer.Due; timer.ScheduleNext(); }
            timer.Invoke();
        }
    }
    private sealed class ManualTimer(ManualTimeProvider owner, TimerCallback callback, object? state) : ITimer
    {
        public DateTimeOffset Due { get; private set; } = DateTimeOffset.MaxValue; public TimeSpan Period { get; private set; } = Timeout.InfiniteTimeSpan; public bool Disposed { get; private set; }
        public bool Change(TimeSpan dueTime, TimeSpan period) { lock (owner._gate) { if (Disposed) return false; ChangeCore(dueTime, period); return true; } }
        public void ChangeCore(TimeSpan dueTime, TimeSpan period) { Due = dueTime == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : owner._now + dueTime; Period = period; }
        public void ScheduleNext() => Due = Period == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : Due + Period;
        public void Invoke() { if (!Disposed) callback(state); }
        public void Dispose() { lock (owner._gate) { Disposed = true; owner._timers.Remove(this); } }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
