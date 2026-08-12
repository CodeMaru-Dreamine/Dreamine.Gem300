# Public API review

Review date: 2026-08-12. Baseline: 1.0.0 source. The generated surface is in
[PUBLIC_API.md](PUBLIC_API.md).

## Result

- Dependencies remain acyclic and implementation types do not leak through `Dreamine.Gem300.Abstractions` interfaces.
- Existing public interfaces and constructors remain present. Public hardening members and models are additive.
- Correctness rules are stricter where continuing with an arbitrary manager graph, ambiguous slot plan, mismatched recipe identity, or raw projection mutation could corrupt state.
- The implemented base-standard domain boundary has focused automated evidence `PASS`; standard wire remains `BLOCKED_STANDARD`, and external/field evidence remains `NOT_RUN`.

## Additive concrete surface

| Area | Additive public surface | Purpose |
|---|---|---|
| Runtime | `CreateFromGemRuntime`, `EventPublisher`, `EventHealth` | Reuse the concrete GEM runtime's Process Program store and expose shared publisher health |
| Object service | action-capacity constructor, `EventHealth`, `RegisterProjection`, `UnregisterProjection`, `UnregisterAction`, `GetObjectKeys` | Reserve projection keys, route typed application actions, manage action lifetime, and query stable identity snapshots |
| Event infrastructure | `Gem300EventPublisher`, typed-object `Record`, bounded `GetSnapshot`, `GetHealth` | Preserve aggregate identity, expose drops, and keep observation failures non-throwing |
| Carrier/Substrate/Jobs | `EventHealth`, stable aggregate snapshots, substrate lease-owner query | Expose stable in-process diagnostics without adding wire list/query semantics |
| Process Job snapshot | retained `ProcessProgram` | Keep the Process Program accepted under the requested recipe identity |
| Workflow | coordinated carrier IDs and explicit slot-assignment snapshots | Make the application-declared integration plan observable without inventing `.1` data |

The abstraction package separately adds event-health/identity models and
explicit slot-assignment models. Existing legacy constructors remain available.

## Behavior tightening

- Projected object keys are reserved before an aggregate is exposed. Reads use the manager projection; raw writes and raw removal are blocked; actions run through registered application handlers.
- Writable generic attributes preserve their original recursive `SecsItem` schema. Action parameters are copied, bounded by timeout/cancellation, and detached safely when an object generation is removed.
- Carrier/substrate acceptance and removal commit under one shared graph gate. Direct carrier unload cannot bypass coordinated ownership.
- Process Job creation validates the returned Process Program identity and atomically acquires substrate reference leases. Control Job creation atomically claims Process Jobs.
- Concrete manager graphs must share the same built-in Substrate and Process Job stores. Mismatches fail at composition rather than allowing split-brain ownership.
- Existing `ProcessJobManager(ISubstrateTracker, ...)` and `ControlJobManager(IProcessJobManager, ...)` signatures remain, but arbitrary external implementations are no longer accepted because they cannot provide the required atomic integrity store. This is an intentional behavior compatibility tightening.
- The interface-based workflow constructor remains. Safe carrier acceptance/release and workflow execution fail fast when the required built-in transaction/ownership graph is unavailable.
- Processor-return, cancellation, Stop, and Abort paths are distinguished. A stopped or aborted Process Job cannot be promoted to successful substrate or Control Job completion.
- All built-in modules share one non-throwing event publisher in `Gem300Runtime`; journal failure is visible through health without making an already committed mutation appear to fail.

## Compatibility decision

No existing interface member, public constructor, or enum value was removed.
Additive concrete APIs were chosen instead of changing the published
interfaces. The fail-fast constructor behavior above is accepted because the
previous arbitrary-manager composition could not uphold the package's declared
cross-module invariants.

## Deferred breaking candidate

| Classification | Proposal | Reason |
|---|---|---|
| Source- and binary-breaking | Add an explicit aborted terminal `ControlJobState` | The current base-revision model maps abort to `Completed`; adding a value changes exhaustive consumer logic |

Persistence, restart recovery, and cross-process ownership are
`INTENTIONALLY_EXCLUDED`. E39.1/E40.1/E87.1/E90.1/E94.1 wire APIs are
`BLOCKED_STANDARD`; E116/E116.1, E42, and E139 claims are also
`BLOCKED_STANDARD`.
