# Public API review

Review date: 2026-08-10. Baseline: 1.0.0 source. See [PUBLIC_API.md](PUBLIC_API.md).

## Result

- Dependencies are acyclic and implementation types do not leak into abstraction interfaces.
- Object-action parameter dictionaries are copied before asynchronous callbacks.
- Load-port modes, slot states, substrate completion results, and event kinds reject undefined numeric enum values.
- State stores use private synchronization and publish immutable snapshots. Event sequence allocation is monotonic within one process.
- No public signature or binary surface changed; changes are non-breaking validation/race hardening.

## Next-version proposals

| Classification | Proposal | Reason |
|---|---|---|
| Source- and binary-breaking | Add an explicit aborted terminal state for Control Jobs | Mapping abort to completed loses intent, but changing the enum is not safe in 1.0 hardening. |
| Source- and binary-breaking | Prevent Process Job deletion through a repository-level ownership contract | The present independent managers cannot atomically validate cross-manager references. |
| Source- and binary-breaking | Make workflow persistence and compensation explicit | The coordinator is in-memory and not transactional across module boundaries. |

The workflow coordinator remains Experimental. Passing domain tests does not establish GEM300 wire interoperability.
