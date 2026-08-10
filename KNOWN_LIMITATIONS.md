# Known limitations

- Current modules are in-memory domain behavior. Standard SECS-II wire mapping, ACK/service-error values, and external interoperability are not implemented.
- The workflow coordinator is Experimental and is not transactional or durable across module/process failure.
- Cross-manager Process Job ownership does not yet prevent every removed-object reference; the required contract change is documented in `docs/API_REVIEW.md`.
- Control Job abort currently terminates at `Completed`; an explicit aborted state is deferred as a breaking change.
- Equipment Performance Tracking and unavailable `.1` mapping documents remain blocked rather than guessed.
- Domain unit tests must not be reported as external GEM300 interoperability.
