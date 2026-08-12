using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Dreamine.Gem.Abstractions.Model;
using Dreamine.Gem.Abstractions.States;
using Dreamine.Gem300.Abstractions.Interfaces;
using Dreamine.Gem300.Abstractions.Model;
using Dreamine.Gem300.Abstractions.States;
using Dreamine.Gem300.Infrastructure;
using Dreamine.Secs.Abstractions.Model;

namespace Dreamine.Gem300.ObjectServices;

/// <summary>\if KO E39 ObjType·ObjID와 공개 RO/RW 속성을 관리하는 스레드 안전 저장소입니다. Wire 서비스는 구현하지 않습니다. \endif \if EN Provides a thread-safe E39 ObjType/ObjID and public RO/RW attribute store without wire services. \endif</summary>
public sealed class Gem300ObjectService : IGem300ObjectService
{
    private const int DefaultActionCapacity = 256;
    private readonly object _lifetimeGate = new();
    private readonly ConcurrentDictionary<Gem300ObjectKey, Entry> _objects = new();
    private readonly ConcurrentQueue<Gem300ObjectKey> _pendingEvents = new();
    private readonly Gem300EventPublisher _eventPublisher;
    private readonly TimeProvider _timeProvider;
    private readonly int _actionCapacity;
    private int _drainingEvents;

    /// <summary>\if KO 이벤트 저널로 객체 서비스를 만듭니다. \endif \if EN Creates the object service with an event journal. \endif</summary>
    public Gem300ObjectService(IGem300EventJournal events, TimeProvider? timeProvider = null)
        : this(new Gem300EventPublisher(events ?? throw new ArgumentNullException(nameof(events)), timeProvider), timeProvider, DefaultActionCapacity) { }

    /// <summary>\if KO 이벤트 저널, 시간 공급자 및 객체별 동작 용량으로 객체 서비스를 만듭니다. \endif \if EN Creates the object service with an event journal, time provider, and per-object action capacity. \endif</summary>
    public Gem300ObjectService(IGem300EventJournal events, TimeProvider? timeProvider, int actionCapacity)
        : this(new Gem300EventPublisher(events ?? throw new ArgumentNullException(nameof(events)), timeProvider), timeProvider, actionCapacity) { }

    internal Gem300ObjectService(Gem300EventPublisher eventPublisher, TimeProvider? timeProvider = null, int actionCapacity = DefaultActionCapacity)
    {
        if (actionCapacity <= 0) throw new ArgumentOutOfRangeException(nameof(actionCapacity));
        _eventPublisher = eventPublisher ?? throw new ArgumentNullException(nameof(eventPublisher)); _timeProvider = timeProvider ?? TimeProvider.System; _actionCapacity = actionCapacity;
    }

    /// <summary>\if KO 이 서비스가 사용하는 비차단 이벤트 게시기 상태입니다. \endif \if EN Gets the non-throwing event-publisher health used by this service. \endif</summary>
    public Gem300EventPublisherHealth EventHealth => _eventPublisher.GetHealth();

    /// <inheritdoc />
    public void Register(Gem300ObjectKey key, IEnumerable<Gem300AttributeDefinition> attributes)
    {
        ArgumentNullException.ThrowIfNull(key); ArgumentNullException.ThrowIfNull(attributes);
        var values = attributes.ToArray();
        if (values.Any(static value => value is null) || values.Select(static value => value.Name).Distinct(StringComparer.Ordinal).Count() != values.Length) throw new ArgumentException("Attribute names must be unique.", nameof(attributes));
        if (values.Any(value => value.Name is "ObjType" or "ObjID")) throw new ArgumentException("ObjType and ObjID are maintained by the object service.", nameof(attributes));
        var dictionary = values.ToDictionary(static value => value.Name, static value => new AttributeEntry(value), StringComparer.Ordinal);
        dictionary.Add("ObjType", new(new("ObjType", new SecsAsciiItem(key.ObjectType), false)));
        dictionary.Add("ObjID", new(new("ObjID", new SecsAsciiItem(key.ObjectId), false)));
        lock (_lifetimeGate)
        {
            if (!_objects.TryAdd(key, new(dictionary))) throw new InvalidOperationException("The object is already registered.");
            EnqueueChanged(key);
        }
        DrainEvents();
    }

    /// <summary>\if KO 애플리케이션이 명시한 객체 키를 manager 원본 상태 projection으로 예약합니다. 표준 객체 유형 이름을 추정하지 않습니다. \endif \if EN Reserves an application-declared object key as a projection over manager source-of-truth state without inferring standard object-type names. \endif</summary>
    public void RegisterProjection(Gem300ObjectKey key, Func<IReadOnlyDictionary<string, SecsItem>> projection)
    {
        ArgumentNullException.ThrowIfNull(key); ArgumentNullException.ThrowIfNull(projection);
        var dictionary = new Dictionary<string, AttributeEntry>(StringComparer.Ordinal)
        {
            ["ObjType"] = new(new("ObjType", new SecsAsciiItem(key.ObjectType), false)),
            ["ObjID"] = new(new("ObjID", new SecsAsciiItem(key.ObjectId), false))
        };
        lock (_lifetimeGate)
        {
            if (!_objects.TryAdd(key, new(dictionary, projection))) throw new InvalidOperationException("The object key is already registered or reserved.");
            EnqueueChanged(key);
        }
        DrainEvents();
    }

    /// <inheritdoc />
    public bool TryGetAttribute(Gem300ObjectKey key, string name, out SecsItem? value)
    {
        ArgumentNullException.ThrowIfNull(key); ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (_objects.TryGetValue(key, out var entry))
        {
            Func<IReadOnlyDictionary<string, SecsItem>>? projection;
            lock (entry.Gate)
            {
                if (!IsCurrent(key, entry)) { value = null; return false; }
                projection = entry.Projection;
                if (projection is null && entry.Attributes.TryGetValue(name, out var attribute)) { value = attribute.Value; return true; }
            }
            if (projection is not null) return GetProjectedAttributes(key, entry, projection).TryGetValue(name, out value);
        }
        value = null; return false;
    }

    /// <inheritdoc />
    public bool TrySetAttribute(Gem300ObjectKey key, string name, SecsItem value)
    {
        ArgumentNullException.ThrowIfNull(key); ArgumentException.ThrowIfNullOrWhiteSpace(name); ArgumentNullException.ThrowIfNull(value);
        if (!_objects.TryGetValue(key, out var entry)) return false;
        var changed = false;
        lock (entry.Gate)
        {
            if (!IsCurrent(key, entry) || entry.Projection is not null || !entry.Attributes.TryGetValue(name, out var attribute) || !attribute.Definition.Writable || !IsSchemaCompatible(attribute.Definition.InitialValue, value)) return false;
            attribute.Value = value; EnqueueChanged(key); changed = true;
        }
        if (changed) DrainEvents(); return changed;
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, SecsItem> GetAttributes(Gem300ObjectKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!_objects.TryGetValue(key, out var entry)) throw new KeyNotFoundException("The object is not registered.");
        Func<IReadOnlyDictionary<string, SecsItem>>? projection;
        lock (entry.Gate)
        {
            if (!IsCurrent(key, entry)) throw new KeyNotFoundException("The object is not registered.");
            projection = entry.Projection;
            if (projection is null) return new ReadOnlyDictionary<string, SecsItem>(entry.Attributes.OrderBy(static pair => pair.Key, StringComparer.Ordinal).ToDictionary(static pair => pair.Key, static pair => pair.Value.Value, StringComparer.Ordinal));
        }
        return GetProjectedAttributes(key, entry, projection);
    }

    /// <inheritdoc />
    public void RegisterAction(Gem300ObjectKey key, string actionName, Func<IReadOnlyDictionary<string, SecsItem>, CancellationToken, ValueTask<GemCommandResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(key); ArgumentException.ThrowIfNullOrWhiteSpace(actionName); ArgumentNullException.ThrowIfNull(handler);
        if (!_objects.TryGetValue(key, out var entry)) throw new KeyNotFoundException("The object is not registered.");
        lock (entry.Gate)
        {
            if (!IsCurrent(key, entry)) throw new KeyNotFoundException("The object is not registered.");
            if (entry.Actions.ContainsKey(actionName)) throw new InvalidOperationException("The object action is already registered.");
            if (entry.Actions.Count >= _actionCapacity) throw new InvalidOperationException("The per-object action capacity is reached.");
            entry.Actions.Add(actionName, handler);
        }
    }

    /// <summary>\if KO 객체 동작을 명시적으로 제거합니다. 실행 중인 호출은 캡처한 처리기를 계속 사용합니다. \endif \if EN Explicitly removes an object action; an in-flight invocation continues with its captured handler. \endif</summary>
    public bool UnregisterAction(Gem300ObjectKey key, string actionName)
    {
        ArgumentNullException.ThrowIfNull(key); ArgumentException.ThrowIfNullOrWhiteSpace(actionName);
        if (!_objects.TryGetValue(key, out var entry)) return false;
        lock (entry.Gate) return IsCurrent(key, entry) && entry.Actions.Remove(actionName);
    }

    /// <inheritdoc />
    public async ValueTask<GemCommandResult> ExecuteActionAsync(Gem300ObjectKey key, string actionName, IReadOnlyDictionary<string, SecsItem> parameters, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key); ArgumentException.ThrowIfNullOrWhiteSpace(actionName); ArgumentNullException.ThrowIfNull(parameters); if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (!_objects.TryGetValue(key, out var entry)) throw new KeyNotFoundException("The object is not registered.");
        Func<IReadOnlyDictionary<string, SecsItem>, CancellationToken, ValueTask<GemCommandResult>> handler;
        lock (entry.Gate)
        {
            if (!IsCurrent(key, entry)) throw new KeyNotFoundException("The object is not registered.");
            if (!entry.Actions.TryGetValue(actionName, out handler!)) return new(GemCommandStatus.NotAllowed, "Unknown object action.");
        }
        var parameterSnapshot = new ReadOnlyDictionary<string, SecsItem>(new Dictionary<string, SecsItem>(parameters, StringComparer.Ordinal));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, entry.Lifetime.Token);
        GemCommandResult result;
        try
        {
            result = await handler(parameterSnapshot, linked.Token).AsTask().WaitAsync(timeout, _timeProvider, linked.Token).ConfigureAwait(false) ?? new(GemCommandStatus.Failed, "The object action returned no result.");
        }
        catch (TimeoutException)
        {
            CancelWithoutThrow(linked);
            return new(GemCommandStatus.Failed, "The object action timed out.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && entry.Lifetime.IsCancellationRequested)
        {
            return new(GemCommandStatus.Failed, "The object was removed while its action was executing.");
        }

        lock (_lifetimeGate)
        {
            if (!IsCurrent(key, entry)) return new(GemCommandStatus.Failed, "The object was replaced while its action was executing.");
            EnqueueChanged(key);
        }
        DrainEvents(); return result;
    }

    /// <inheritdoc />
    public bool Remove(Gem300ObjectKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        Entry? removed = null;
        lock (_lifetimeGate)
        {
            while (_objects.TryGetValue(key, out var entry))
            {
                lock (entry.Gate)
                {
                    if (entry.Projection is not null) throw new InvalidOperationException("A projected object key must be released with UnregisterProjection; raw removal is blocked.");
                    if (!((ICollection<KeyValuePair<Gem300ObjectKey, Entry>>)_objects).Remove(new(key, entry))) continue;
                    removed = entry; EnqueueChanged(key); break;
                }
            }
        }
        if (removed is null) return false;
        CancelWithoutThrow(removed.Lifetime); DrainEvents(); return true;
    }

    /// <summary>\if KO projection 예약을 명시적으로 해제합니다. detach 이후 취소 callback과 이벤트를 모든 객체 lock 밖에서 실행합니다. \endif \if EN Explicitly releases a projection reservation; cancellation callbacks and events run outside all object locks after detach. \endif</summary>
    public bool UnregisterProjection(Gem300ObjectKey key)
    {
        ArgumentNullException.ThrowIfNull(key); Entry? removed = null;
        lock (_lifetimeGate)
        {
            while (_objects.TryGetValue(key, out var entry))
            {
                lock (entry.Gate)
                {
                    if (entry.Projection is null) return false;
                    if (!((ICollection<KeyValuePair<Gem300ObjectKey, Entry>>)_objects).Remove(new(key, entry))) continue;
                    removed = entry; EnqueueChanged(key); break;
                }
            }
        }
        if (removed is null) return false;
        CancelWithoutThrow(removed.Lifetime); DrainEvents(); return true;
    }

    /// <summary>\if KO 등록 객체 키를 유형과 ID 순서로 반환합니다. \endif \if EN Returns registered object keys ordered by type and ID. \endif</summary>
    public IReadOnlyList<Gem300ObjectKey> GetObjectKeys()
    {
        lock (_lifetimeGate) return _objects.Keys.OrderBy(static key => key.ObjectType, StringComparer.Ordinal).ThenBy(static key => key.ObjectId, StringComparer.Ordinal).ToArray();
    }

    private IReadOnlyDictionary<string, SecsItem> GetProjectedAttributes(Gem300ObjectKey key, Entry entry, Func<IReadOnlyDictionary<string, SecsItem>> projection)
    {
        var projected = projection() ?? throw new InvalidOperationException("The object projection returned no attributes.");
        if (projected.Keys.Any(static name => name is "ObjType" or "ObjID") || projected.Any(static pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value is null)) throw new InvalidOperationException("Projected attributes must be non-null and cannot replace ObjType or ObjID.");
        lock (entry.Gate) if (!IsCurrent(key, entry) || !ReferenceEquals(entry.Projection, projection)) throw new KeyNotFoundException("The projected object was replaced while it was being read.");
        var values = projected.ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal);
        values.Add("ObjType", new SecsAsciiItem(key.ObjectType)); values.Add("ObjID", new SecsAsciiItem(key.ObjectId));
        return new ReadOnlyDictionary<string, SecsItem>(values.OrderBy(static pair => pair.Key, StringComparer.Ordinal).ToDictionary(static pair => pair.Key, static pair => pair.Value, StringComparer.Ordinal));
    }
    private bool IsCurrent(Gem300ObjectKey key, Entry entry) => _objects.TryGetValue(key, out var current) && ReferenceEquals(current, entry);
    private void EnqueueChanged(Gem300ObjectKey key) => _pendingEvents.Enqueue(key);
    private void DrainEvents()
    {
        while (Interlocked.CompareExchange(ref _drainingEvents, 1, 0) == 0)
        {
            try { while (_pendingEvents.TryDequeue(out var key)) _eventPublisher.TryRecord(Gem300EventKind.ObjectChanged, key); }
            finally { Volatile.Write(ref _drainingEvents, 0); }
            if (_pendingEvents.IsEmpty) return;
        }
    }
    private static void CancelWithoutThrow(CancellationTokenSource source) { try { source.Cancel(); } catch { } }
    private static bool IsSchemaCompatible(SecsItem expected, SecsItem candidate)
    {
        if (expected.Format != candidate.Format) return false;
        if (expected is not SecsListItem expectedList) return true;
        if (candidate is not SecsListItem candidateList || expectedList.Items.Count != candidateList.Items.Count) return false;
        for (var index = 0; index < expectedList.Items.Count; index++) if (!IsSchemaCompatible(expectedList.Items[index], candidateList.Items[index])) return false;
        return true;
    }

    private sealed class Entry(Dictionary<string, AttributeEntry> attributes, Func<IReadOnlyDictionary<string, SecsItem>>? projection = null)
    {
        public object Gate { get; } = new();
        public Dictionary<string, AttributeEntry> Attributes { get; } = attributes;
        public Dictionary<string, Func<IReadOnlyDictionary<string, SecsItem>, CancellationToken, ValueTask<GemCommandResult>>> Actions { get; } = new(StringComparer.Ordinal);
        public CancellationTokenSource Lifetime { get; } = new();
        public Func<IReadOnlyDictionary<string, SecsItem>>? Projection { get; } = projection;
    }

    private sealed class AttributeEntry(Gem300AttributeDefinition definition)
    {
        public Gem300AttributeDefinition Definition { get; } = definition;
        public SecsItem Value { get; set; } = definition.InitialValue;
    }
}
