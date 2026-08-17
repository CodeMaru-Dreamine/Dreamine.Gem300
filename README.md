# Dreamine.Gem300

[![CI](https://github.com/CodeMaru-Dreamine/Dreamine.Gem300/actions/workflows/ci.yml/badge.svg)](https://github.com/CodeMaru-Dreamine/Dreamine.Gem300/actions/workflows/ci.yml)
[![Quality Gate](https://sonarcloud.io/api/project_badges/measure?project=CodeMaru-Dreamine_Dreamine.Gem300&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=CodeMaru-Dreamine_Dreamine.Gem300) [![Security Rating](https://sonarcloud.io/api/project_badges/measure?project=CodeMaru-Dreamine_Dreamine.Gem300&metric=security_rating)](https://sonarcloud.io/summary/new_code?id=CodeMaru-Dreamine_Dreamine.Gem300) [![Coverage](https://sonarcloud.io/api/project_badges/measure?project=CodeMaru-Dreamine_Dreamine.Gem300&metric=coverage)](https://sonarcloud.io/summary/new_code?id=CodeMaru-Dreamine_Dreamine.Gem300)

`Dreamine.Gem300` implements a hardened, process-local GEM300 domain boundary
for modern .NET applications.

[➡️ 한국어 문서 보기](https://github.com/CodeMaru-Dreamine/Dreamine.Gem300/blob/main/README_KO.md)

## Install and start

```powershell
dotnet add package Dreamine.Gem300
```

Choose this package for process-local carrier, substrate, Process Job, and Control Job workflows. It does **not** provide GEM300 `.1` wire mappings. Run the [package-first QuickStart](https://github.com/CodeMaru-Dreamine/Dreamine.Gem300/blob/main/QUICKSTART.md); use `-p:UseLocalDreamineSources=true` only when validating a full source workspace.

## Evidence status

| Capability | Status | Evidence |
|---|---|---|
| E39/E40/E87/E90/E94 in-memory domain boundary | `PASS` | Focused model, manager, workflow, integrity, concurrency, cancellation, and regression tests |
| E39.1/E40.1/E87.1/E90.1/E94.1 standard wire binding | `BLOCKED_STANDARD` | Required mapping originals were not available locally |
| E116/E116.1, E42, and E139 claims | `BLOCKED_STANDARD` | Required normative originals were unavailable |
| External interoperability and field evidence | `NOT_RUN` | No independent counterpart or production equipment evidence was executed |
| Durable persistence, restart recovery, and cross-process ownership | `INTENTIONALLY_EXCLUDED` | This productization gate is explicitly process-local and in-memory |

`PASS` applies only to the implemented base-revision domain boundary. It does
not mean current-revision conformance, certification, standard wire support,
or vendor interoperability.

## Implemented domain boundary

- E39-style object identity, typed RO/RW attributes, cancellable actions, and application-declared manager projections
- Projection-key reservation, source-of-truth reads, raw mutation/removal blocking, and typed application action routing
- E87 load-port and carrier state with atomic carrier/substrate acceptance and removal
- E90 substrate location occupancy, residence history, processing state, and reference leases
- E40 Process Job lifecycle with retained Process Program identity and material leases
- E94 serial Control Job queue with central Process Job ownership and shared execution claims
- Explicit application-level carrier slot/substrate assignments; ordering and location text are never inferred as slot indexes
- Stable snapshots and shared graph identity validation; incompatible built-in manager graphs fail fast
- A bounded process-local event journal with journal identity, drop/retention health, and a shared non-throwing publisher
- An Experimental carrier-to-process-to-removal coordinator whose failure cleanup moves state forward only; stopped or aborted Process Jobs are never promoted to successful substrate or Control Job completion

## Safe composition

When the application uses the concrete `Dreamine.Gem.GemRuntime`, create
GEM300 from that runtime so both layers use the same Process Program store:

```csharp
var gem300 = Gem300Runtime.CreateFromGemRuntime(gemRuntime);
gemRuntime.ProcessPrograms.Put(new GemProcessProgram("RECIPE-1", [0x01]));

gem300.Carriers.RegisterLoadPort("PORT-1");
gem300.Carriers.SetInService("PORT-1");
gem300.Workflow.AcceptCarrier(new CarrierArrivalPlan(
    "PORT-1",
    "CARRIER-1",
    [CarrierSlotState.CorrectlyOccupied],
    [new SubstrateArrivalPlan("SUBSTRATE-1", "SOURCE-1", "DESTINATION-1")],
    [new CarrierSubstrateSlotAssignment(0, "SUBSTRATE-1")]));
```

The existing `Gem300Runtime(IGemRuntime, IGemProcessProgramService, ...)`
constructor is retained for compatible composition. The caller must pass the
same logical Process Program service used by the GEM layer. The QuickStart
shares one service instance explicitly; production concrete runtimes should
prefer `CreateFromGemRuntime`.

The explicit slot association and object projection keys are application
integration metadata. They are not invented `.1` wire mappings.

## Documentation

- [Quick start](https://github.com/CodeMaru-Dreamine/Dreamine.Gem300/blob/main/QUICKSTART.md)
- [Known limitations](https://github.com/CodeMaru-Dreamine/Dreamine.Gem300/blob/main/KNOWN_LIMITATIONS.md)
- [Public API review](https://github.com/CodeMaru-Dreamine/Dreamine.Gem300/blob/main/docs/API_REVIEW.md)
- [SEMI requirements trace](https://github.com/CodeMaru-Dreamine/Dreamine.Gem300/blob/main/docs/SEMI_REQUIREMENTS_TRACE.md)

## License

MIT.
