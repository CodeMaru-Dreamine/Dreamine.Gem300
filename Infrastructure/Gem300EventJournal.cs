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
    private long _sequence;
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
        lock (_gate)
        {
            var item = new Gem300DomainEvent(checked(++_sequence), kind, aggregateId, _timeProvider.GetUtcNow());
            if (_events.Count == _capacity) _events.Dequeue(); _events.Enqueue(item); return item;
        }
    }
    /// <inheritdoc />
    public IReadOnlyList<Gem300DomainEvent> GetSnapshot() { lock (_gate) return _events.ToArray(); }
}
