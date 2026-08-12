using Dreamine.Gem300.Abstractions.Interfaces;
using Dreamine.Gem300.Abstractions.Model;
using Dreamine.Gem300.Abstractions.States;

namespace Dreamine.Gem300.Infrastructure;

/// <summary>\if KO 주입 시간과 제한 용량을 사용하는 스레드 안전 프로세스 내 이벤트 저널입니다. \endif \if EN Provides a thread-safe, process-local event journal with injected time and bounded capacity. \endif</summary>
public sealed class Gem300EventJournal : IGem300EventJournal
{
    private readonly object _gate = new();
    private readonly Queue<Gem300DomainEvent> _events = new();
    private readonly TimeProvider _timeProvider;
    private readonly int _capacity;
    private readonly Guid _journalId = Guid.NewGuid();
    private long _sequence;
    private long _droppedCount;
    /// <summary>\if KO 시간 공급자와 양의 용량으로 저널을 만듭니다. \endif \if EN Creates a journal with a time provider and positive capacity. \endif</summary>
    public Gem300EventJournal(TimeProvider? timeProvider = null, int capacity = 4096)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity)); _timeProvider = timeProvider ?? TimeProvider.System; _capacity = capacity;
    }
    /// <inheritdoc />
    public Gem300DomainEvent Record(Gem300EventKind kind, string aggregateId)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentException.ThrowIfNullOrWhiteSpace(aggregateId);
        var occurredAt = _timeProvider.GetUtcNow();
        lock (_gate)
        {
            return RecordCore(kind, kind.ToString(), aggregateId, occurredAt);
        }
    }
    /// <summary>\if KO 객체 유형과 ID를 각각 보존하여 객체 이벤트를 기록합니다. \endif \if EN Records an object event while preserving object type and ID separately. \endif</summary>
    public Gem300DomainEvent Record(Gem300EventKind kind, Gem300ObjectKey objectKey)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        ArgumentNullException.ThrowIfNull(objectKey);
        var occurredAt = _timeProvider.GetUtcNow();
        lock (_gate) return RecordCore(kind, objectKey.ObjectType, objectKey.ObjectId, occurredAt);
    }
    /// <inheritdoc />
    public IReadOnlyList<Gem300DomainEvent> GetSnapshot() { lock (_gate) return _events.ToArray(); }
    /// <summary>\if KO 지정 순서 이후의 이벤트를 안정적인 순서로 제한 조회합니다. \endif \if EN Queries a bounded, stably ordered event snapshot after a sequence. \endif</summary>
    public IReadOnlyList<Gem300DomainEvent> GetSnapshot(long afterSequence, int maxCount)
    {
        if (afterSequence < 0) throw new ArgumentOutOfRangeException(nameof(afterSequence));
        if (maxCount <= 0) throw new ArgumentOutOfRangeException(nameof(maxCount));
        lock (_gate) return _events.Where(value => value.Sequence > afterSequence).Take(maxCount).ToArray();
    }
    /// <summary>\if KO 저널의 용량, 보존 구간 및 드롭 누계를 조회합니다. \endif \if EN Gets journal capacity, retained range, and cumulative drop metadata. \endif</summary>
    public Gem300EventJournalHealth GetHealth()
    {
        lock (_gate)
        {
            return new(_journalId, _capacity, _events.Count, _sequence, _droppedCount, _events.Count == 0 ? null : _events.Peek().Sequence, _events.Count == 0 ? null : _events.Last().Sequence);
        }
    }

    private Gem300DomainEvent RecordCore(Gem300EventKind kind, string aggregateType, string aggregateId, DateTimeOffset occurredAt)
    {
        var nextSequence = checked(_sequence + 1);
        var item = new Gem300DomainEvent(_journalId, nextSequence, kind, aggregateType, aggregateId, occurredAt);
        _sequence = nextSequence;
        if (_events.Count == _capacity) { _events.Dequeue(); if (_droppedCount != long.MaxValue) _droppedCount++; }
        _events.Enqueue(item);
        return item;
    }
}
