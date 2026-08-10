# Contributing

Keep object, carrier, substrate, Process Job, and Control Job modules independently testable. Do not invent wire numbers or infer unavailable normative behavior.

Run Release build/test/pack and the executable workflow sample. Every transition change needs invalid-state, duplicate, cancellation/failure cleanup, and concurrency coverage. Breaking ownership/state-model changes must first be classified in `docs/API_REVIEW.md`. Never commit licensed standards, customer/internal material, secrets, captures, or build output.

No existing standalone GitHub Actions convention can resolve the sibling project graph from a single repository checkout. Add CI only after defining coordinated checkout or published-package consumption.
