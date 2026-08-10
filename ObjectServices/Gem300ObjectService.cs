using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using Dreamine.Gem.Abstractions.Model;
using Dreamine.Gem300.Abstractions.Interfaces;
using Dreamine.Gem300.Abstractions.Model;
using Dreamine.Gem300.Abstractions.States;
using Dreamine.Secs.Abstractions.Model;

namespace Dreamine.Gem300.ObjectServices;

/// <summary>\if KO E39 ObjType·ObjID와 공개 RO/RW 속성을 관리하는 스레드 안전 저장소입니다. Wire 서비스는 구현하지 않습니다. \endif \if EN Provides a thread-safe E39 ObjType/ObjID and public RO/RW attribute store without wire services. \endif</summary>
public sealed class Gem300ObjectService : IGem300ObjectService
{
    private readonly ConcurrentDictionary<Gem300ObjectKey, Entry> _objects = new();
    private readonly IGem300EventJournal _events;
    private readonly TimeProvider _timeProvider;
    /// <summary>\if KO 이벤트 저널로 객체 서비스를 만듭니다. \endif \if EN Creates the object service with an event journal. \endif</summary>
    public Gem300ObjectService(IGem300EventJournal events, TimeProvider? timeProvider = null) { _events = events ?? throw new ArgumentNullException(nameof(events)); _timeProvider = timeProvider ?? TimeProvider.System; }
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
        if (!_objects.TryAdd(key, new(dictionary))) throw new InvalidOperationException("The object is already registered.");
        _events.Record(Gem300EventKind.ObjectChanged, key.ObjectId);
    }
    /// <inheritdoc />
    public bool TryGetAttribute(Gem300ObjectKey key, string name, out SecsItem? value)
    {
        ArgumentNullException.ThrowIfNull(key); ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (_objects.TryGetValue(key, out var entry)) lock (entry.Gate) if (entry.Attributes.TryGetValue(name, out var attribute)) { value = attribute.Value; return true; }
        value = null; return false;
    }
    /// <inheritdoc />
    public bool TrySetAttribute(Gem300ObjectKey key, string name, SecsItem value)
    {
        ArgumentNullException.ThrowIfNull(key); ArgumentException.ThrowIfNullOrWhiteSpace(name); ArgumentNullException.ThrowIfNull(value);
        if (!_objects.TryGetValue(key, out var entry)) return false;
        lock (entry.Gate)
        {
            if (!entry.Attributes.TryGetValue(name, out var attribute) || !attribute.Definition.Writable) return false;
            attribute.Value = value;
        }
        _events.Record(Gem300EventKind.ObjectChanged, key.ObjectId); return true;
    }
    /// <inheritdoc />
    public IReadOnlyDictionary<string, SecsItem> GetAttributes(Gem300ObjectKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (!_objects.TryGetValue(key, out var entry)) throw new KeyNotFoundException("The object is not registered.");
        lock (entry.Gate) return new ReadOnlyDictionary<string, SecsItem>(entry.Attributes.ToDictionary(static pair => pair.Key, static pair => pair.Value.Value, StringComparer.Ordinal));
    }
    /// <inheritdoc />
    public void RegisterAction(Gem300ObjectKey key, string actionName, Func<IReadOnlyDictionary<string, SecsItem>, CancellationToken, ValueTask<GemCommandResult>> handler)
    {
        ArgumentNullException.ThrowIfNull(key); ArgumentException.ThrowIfNullOrWhiteSpace(actionName); ArgumentNullException.ThrowIfNull(handler);
        if (!_objects.TryGetValue(key, out var entry)) throw new KeyNotFoundException("The object is not registered.");
        lock (entry.Gate) if (!entry.Actions.TryAdd(actionName, handler)) throw new InvalidOperationException("The object action is already registered.");
    }
    /// <inheritdoc />
    public async ValueTask<GemCommandResult> ExecuteActionAsync(Gem300ObjectKey key, string actionName, IReadOnlyDictionary<string, SecsItem> parameters, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key); ArgumentException.ThrowIfNullOrWhiteSpace(actionName); ArgumentNullException.ThrowIfNull(parameters); if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        if (!_objects.TryGetValue(key, out var entry)) throw new KeyNotFoundException("The object is not registered.");
        Func<IReadOnlyDictionary<string, SecsItem>, CancellationToken, ValueTask<GemCommandResult>> handler;
        lock (entry.Gate) if (!entry.Actions.TryGetValue(actionName, out handler!)) return new(Dreamine.Gem.Abstractions.States.GemCommandStatus.NotAllowed, "Unknown object action.");
        var parameterSnapshot = new ReadOnlyDictionary<string, SecsItem>(new Dictionary<string, SecsItem>(parameters, StringComparer.Ordinal));
        try
        {
            var result = await handler(parameterSnapshot, cancellationToken).AsTask().WaitAsync(timeout, _timeProvider, cancellationToken).ConfigureAwait(false) ?? new(Dreamine.Gem.Abstractions.States.GemCommandStatus.Failed, "The object action returned no result.");
            _events.Record(Gem300EventKind.ObjectChanged, key.ObjectId); return result;
        }
        catch (TimeoutException) { return new(Dreamine.Gem.Abstractions.States.GemCommandStatus.Failed, "The object action timed out."); }
    }
    /// <inheritdoc />
    public bool Remove(Gem300ObjectKey key)
    {
        ArgumentNullException.ThrowIfNull(key); var removed = _objects.TryRemove(key, out _); if (removed) _events.Record(Gem300EventKind.ObjectChanged, key.ObjectId); return removed;
    }
    private sealed class Entry(Dictionary<string, AttributeEntry> attributes) { public object Gate { get; } = new(); public Dictionary<string, AttributeEntry> Attributes { get; } = attributes; public Dictionary<string, Func<IReadOnlyDictionary<string, SecsItem>, CancellationToken, ValueTask<GemCommandResult>>> Actions { get; } = new(StringComparer.Ordinal); }
    private sealed class AttributeEntry(Gem300AttributeDefinition definition) { public Gem300AttributeDefinition Definition { get; } = definition; public SecsItem Value { get; set; } = definition.InitialValue; }
}
