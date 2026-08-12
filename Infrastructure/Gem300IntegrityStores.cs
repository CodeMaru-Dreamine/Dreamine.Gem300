using System.Runtime.CompilerServices;
using Dreamine.Gem300.Abstractions.Interfaces;
using Dreamine.Gem300.Abstractions.States;

namespace Dreamine.Gem300.Infrastructure;

internal sealed class Gem300DomainGate
{
    public object SyncRoot { get; } = new();
}

internal interface IProcessJobOwnershipStore
{
    void Claim(string ownerId, IReadOnlyList<string> processJobIds);
    void Release(string ownerId, IReadOnlyList<string> processJobIds);
}

internal static class ProcessJobOwnershipStores
{
    private static readonly ConditionalWeakTable<object, FallbackProcessJobOwnershipStore> Stores = new();

    public static IProcessJobOwnershipStore For(IProcessJobManager manager) =>
        manager as IProcessJobOwnershipStore ?? Stores.GetValue(manager, key => new((IProcessJobManager)key));

    private sealed class FallbackProcessJobOwnershipStore(IProcessJobManager manager) : IProcessJobOwnershipStore
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, string> _owners = new(StringComparer.Ordinal);

        public void Claim(string ownerId, IReadOnlyList<string> processJobIds)
        {
            lock (_gate)
            {
                foreach (var id in processJobIds) _ = manager.Get(id);
                if (processJobIds.Any(_owners.ContainsKey)) throw new InvalidOperationException("A process job is already assigned to another control job.");
                foreach (var id in processJobIds) _owners.Add(id, ownerId);
            }
        }

        public void Release(string ownerId, IReadOnlyList<string> processJobIds)
        {
            lock (_gate)
            {
                foreach (var id in processJobIds)
                {
                    if (!_owners.TryGetValue(id, out var owner) || !StringComparer.Ordinal.Equals(owner, ownerId)) throw new InvalidOperationException("The control job does not own every referenced process job.");
                    if (!CanRelease(manager.Get(id).State)) throw new InvalidOperationException("An active process job cannot be released by deleting its control job.");
                }
                foreach (var id in processJobIds) _owners.Remove(id);
            }
        }
    }

    internal static bool CanRelease(ProcessJobState state) => state is ProcessJobState.Queued or ProcessJobState.ProcessComplete or ProcessJobState.Stopped or ProcessJobState.Aborted;
}

internal interface ISubstrateLeaseStore
{
    void Acquire(string ownerId, IReadOnlyList<string> substrateIds);
    void Release(string ownerId, IReadOnlyList<string> substrateIds);
}

internal static class SubstrateLeaseStores
{
    private static readonly ConditionalWeakTable<object, FallbackSubstrateLeaseStore> Stores = new();

    public static ISubstrateLeaseStore For(ISubstrateTracker tracker) =>
        tracker as ISubstrateLeaseStore ?? Stores.GetValue(tracker, key => new((ISubstrateTracker)key));

    private sealed class FallbackSubstrateLeaseStore(ISubstrateTracker tracker) : ISubstrateLeaseStore
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, HashSet<string>> _leases = new(StringComparer.Ordinal);

        public void Acquire(string ownerId, IReadOnlyList<string> substrateIds)
        {
            lock (_gate)
            {
                foreach (var id in substrateIds) _ = tracker.Get(id);
                foreach (var id in substrateIds)
                {
                    if (!_leases.TryGetValue(id, out var owners)) _leases.Add(id, owners = new(StringComparer.Ordinal));
                    if (!owners.Add(ownerId)) throw new InvalidOperationException("The substrate lease is already held by this owner.");
                }
            }
        }

        public void Release(string ownerId, IReadOnlyList<string> substrateIds)
        {
            lock (_gate)
            {
                EnsureOwned(ownerId, substrateIds, false);
                foreach (var id in substrateIds) { var owners = _leases[id]; owners.Remove(ownerId); if (owners.Count == 0) _leases.Remove(id); }
            }
        }

        private void EnsureOwned(string ownerId, IReadOnlyList<string> substrateIds, bool exclusive)
        {
            foreach (var id in substrateIds)
            {
                if (!_leases.TryGetValue(id, out var owners) || !owners.Contains(ownerId) || exclusive && owners.Count != 1) throw new InvalidOperationException("The substrate lease is missing or shared by another owner.");
            }
        }
    }

}
