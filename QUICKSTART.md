# Quick start

Run the buildable in-memory Carrier → Substrate → Process Job → Control Job workflow:

```powershell
dotnet run --project samples/Dreamine.Gem300.QuickStart
```

The sample validates a carrier/slot plan, registers material, executes one ordered job, moves material to its destination, and releases the carrier. It uses a process-local event journal and in-memory services.

This coordinator is Experimental and has no GEM300 SECS-II wire mapping, persistence, crash recovery, or external interoperability claim. See [KNOWN_LIMITATIONS.md](KNOWN_LIMITATIONS.md).
