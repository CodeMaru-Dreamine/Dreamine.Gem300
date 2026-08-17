# Quick start

The checked-in sample references the published `Dreamine.Gem300` package by default, so it runs from a standalone clone. From the repository directory, run the buildable in-memory workflow:

```powershell
dotnet run --project samples/Dreamine.Gem300.QuickStart
```

Add `-p:UseLocalDreamineSources=true` only when validating the canonical full source workspace.

The sample uses one shared `GemProcessProgramService` instance for recipe
registration and `Gem300Runtime` consumption. It then:

1. registers and enables a load port;
2. accepts a carrier with an explicit application slot/substrate assignment;
3. creates a Process Job and a Control Job;
4. runs the ordered job and moves the substrate to its destination; and
5. deletes the jobs and atomically releases the coordinated carrier.

For an application that already owns a concrete `Dreamine.Gem.GemRuntime`, use
the factory that binds GEM300 to that runtime's Process Program store:

```csharp
var gem300 = Gem300Runtime.CreateFromGemRuntime(gemRuntime);
gemRuntime.ProcessPrograms.Put(new GemProcessProgram("RECIPE-1", [0x01]));
```

The focused in-memory domain path is `PASS`. This sample does not open an HSMS
connection and does not implement GEM300 SECS-II messages. The relevant `.1`
wire mappings are `BLOCKED_STANDARD`; external and field verification are
`NOT_RUN`. Persistence, restart recovery, and cross-process ownership are
`INTENTIONALLY_EXCLUDED`.

See [KNOWN_LIMITATIONS.md](KNOWN_LIMITATIONS.md) before production integration.
