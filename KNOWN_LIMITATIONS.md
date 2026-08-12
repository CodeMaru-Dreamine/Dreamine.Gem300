# Known limitations

## Standards and evidence

- E39/E40/E87/E90/E94 behavior is revision-scoped, process-local domain behavior only. It is not a current-revision conformance or certification claim.
- E39.1, E40.1, E87.1, E90.1, and E94.1 standard wire bindings are `BLOCKED_STANDARD`. No message number, W-bit, body, ACK, or service-error value is guessed.
- E116/E116.1 Equipment Performance, E42 Recipe, and E139 Recipe and Parameter Management claims are `BLOCKED_STANDARD`; no placeholder API is published for them.
- External interoperability and field verification are `NOT_RUN`. Domain tests and the in-memory QuickStart must not be reported as external GEM300 interoperability.

## Process-local integrity boundary

- Persistence, restart recovery, crash recovery, distributed transactions, and cross-process ownership are `INTENTIONALLY_EXCLUDED`. Objects, leases, jobs, workflow ownership, and event records are in memory.
- The event journal is bounded and process-local. `(JournalId, Sequence)` identifies a record within one journal lifetime; `Sequence` alone is not globally durable. `GetHealth()` reports retention and drops.
- The shared event publisher does not turn a committed mutation into a thrown journal failure. Applications must monitor `EventHealth`; a successful domain mutation does not guarantee its observation event was retained.
- Carrier acceptance/removal atomicity requires the built-in `CarrierManager` and `SubstrateTracker` created with one shared domain gate, normally by `Gem300Runtime`.
- `ProcessJobManager` requires the built-in `SubstrateTracker`, and `ControlJobManager` requires the built-in `ProcessJobManager`. Existing interface-typed constructors remain, but arbitrary external manager implementations are unsupported and fail fast.
- `Gem300WorkflowCoordinator` retains its interface-based constructor for compatibility. It rejects mismatched built-in graph identity at construction, and safe carrier/workflow operations fail fast when the required built-in ownership boundary is unavailable.

## Model and workflow limits

- A non-empty safe carrier workflow requires the additive five-argument `CarrierArrivalPlan` with explicit application-level slot/substrate assignments. The legacy four-argument constructor remains constructible but does not infer assignments from ordering or location names.
- `Gem300ObjectService.RegisterProjection` is opt-in. Applications select each key and bind reads/actions to manager source-of-truth state. Projected keys block raw attribute mutation and raw removal; release them with `UnregisterProjection`.
- Substrate leases retain references; they are not slot assignments or execution reservations. Sequential Process Jobs may retain one substrate, while substrate processing state prevents simultaneous processing.
- A Process Job retains the accepted Process Program snapshot. Recipe replacement after creation does not rewrite that job's retained identity.
- A processor that stops or aborts a Process Job is not treated as success: the coordinator does not promote the substrate or Control Job to successful completion.
- `ControlJobState` has no distinct aborted terminal. `Abort` currently terminates at `Completed`; adding a distinct value is deferred because it is a breaking enum change.
- Host/equipment reconnect resynchronization is not implemented without the relevant `.1` mappings.
