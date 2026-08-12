using Dreamine.Gem300.Abstractions.Interfaces;
using Dreamine.Gem300.Abstractions.Model;
using Dreamine.Gem300.Abstractions.States;

namespace Dreamine.Gem300.Infrastructure;

/// <summary>\if KO 도메인 변경 이후 저널 장애를 호출자에게 다시 던지지 않고 상태로 관찰 가능하게 만드는 게시기입니다. \endif \if EN Publishes post-mutation events without rethrowing journal failures and exposes their health. \endif</summary>
public sealed class Gem300EventPublisher
{
    private readonly object _gate = new();
    private readonly IGem300EventJournal _journal;
    private readonly TimeProvider _timeProvider;
    private long _failureCount;
    private string? _lastError;
    private DateTimeOffset? _lastFailureAt;

    /// <summary>\if KO 기존 저널과 선택적 시간 공급자로 게시기를 만듭니다. \endif \if EN Creates a publisher over an existing journal and optional time provider. \endif</summary>
    public Gem300EventPublisher(IGem300EventJournal journal, TimeProvider? timeProvider = null)
    {
        _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>\if KO 문자열 Aggregate 이벤트를 비차단 방식으로 기록합니다. \endif \if EN Records a string-aggregate event without propagating journal failures. \endif</summary>
    public bool TryRecord(Gem300EventKind kind, string aggregateId) => TryRecordCore(() => _journal.Record(kind, aggregateId));

    /// <summary>\if KO 객체 유형과 ID를 모두 보존하여 이벤트를 비차단 방식으로 기록합니다. \endif \if EN Records an object event without propagating failures while preserving both type and ID. \endif</summary>
    public bool TryRecord(Gem300EventKind kind, Gem300ObjectKey objectKey)
    {
        ArgumentNullException.ThrowIfNull(objectKey);
        return _journal is Gem300EventJournal concrete
            ? TryRecordCore(() => concrete.Record(kind, objectKey))
            : TryRecordCore(() => _journal.Record(kind, $"{objectKey.ObjectType}>{objectKey.ObjectId}"));
    }

    /// <summary>\if KO 누적 기록 실패 상태를 안정적으로 조회합니다. \endif \if EN Gets a stable snapshot of cumulative recording failures. \endif</summary>
    public Gem300EventPublisherHealth GetHealth()
    {
        lock (_gate) return new(_failureCount, _lastError, _lastFailureAt);
    }

    private bool TryRecordCore(Func<Gem300DomainEvent> record)
    {
        try { _ = record(); return true; }
        catch (Exception exception)
        {
            string error;
            DateTimeOffset? failureAt;
            try { error = $"{exception.GetType().Name}: {exception.Message}"; } catch { error = exception.GetType().Name; }
            try { failureAt = _timeProvider.GetUtcNow(); } catch { failureAt = null; }
            lock (_gate)
            {
                if (_failureCount != long.MaxValue) _failureCount++;
                _lastError = error; _lastFailureAt = failureAt;
            }
            return false;
        }
    }
}
