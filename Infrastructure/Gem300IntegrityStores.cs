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

internal static class ProcessJobOwnershipPolicy
{
    internal static bool CanRelease(ProcessJobState state) => state is ProcessJobState.Queued or ProcessJobState.ProcessComplete or ProcessJobState.Stopped or ProcessJobState.Aborted;
}

internal interface ISubstrateLeaseStore
{
    void Acquire(string ownerId, IReadOnlyList<string> substrateIds);
    void Release(string ownerId, IReadOnlyList<string> substrateIds);
}
