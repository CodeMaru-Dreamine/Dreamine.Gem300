# Dreamine.Gem300

Dreamine.Gem300 provides initial, independently testable domain modules for
GEM300 equipment behavior.

[➡️ 한국어 문서 보기](https://github.com/CodeMaru-Dreamine/Dreamine.Gem300/blob/main/README_KO.md)

## Implemented domain boundary

- E39-style object keys, fundamental `ObjType`/`ObjID`, typed RO/RW
  attributes, and cancellable object actions
- E87 load-port transfer, access mode, reservation, association, carrier ID,
  slot-map, and carrier-access states
- E90 substrate transport, processing, ID-confirmation, location occupancy,
  and residence history
- E40 process-job lifecycle, recipe/material existence checks, pause, stop,
  abort, completion, and deletion
- E94 serial control-job queue, ordered process-job ownership, selection,
  manual start, pause, completion, abort, and deletion
- Bounded, injected-time domain-event journal
- Experimental carrier-to-process-to-removal workflow with deterministic abort
  cleanup and cancellation propagation

All feature modules are exposed through focused interfaces in
`Dreamine.Gem300.Abstractions`; they do not mutate one another's internals.

## Standards and limits

The normative local evidence used in this pass is E39-0703 (reapproved 1109),
E40-0312, E87-0312, E90-0312, and E94-0314. Newer revisions are listed in the
[requirements trace](./docs/SEMI_REQUIREMENTS_TRACE.md), so this package does
**not** claim current-revision conformance, certification, or vendor
interoperability.

E39.1, E40.1, E87.1, E90.1, and E94.1 originals were not locally available.
Consequently, no standard SECS-II wire mapping, ACK number, or service error
code is guessed. E116/E116.1 originals were also unavailable, so Equipment
Performance Tracking is explicitly blocked rather than represented by an
unsupported public API. E84 handoff, persistent recovery, and connection-state
resynchronization are outside this first pass.

## Composition

```csharp
var gem300 = new Gem300Runtime(gemRuntime, gemRuntime.ProcessPrograms);
gem300.Carriers.RegisterLoadPort("PORT-1");
gem300.Carriers.SetInService("PORT-1");
```

The concrete example assumes `gemRuntime` is `Dreamine.Gem.GemRuntime`; the
abstractions remain dependent only on provider-neutral contracts.

## License

MIT.
