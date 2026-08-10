# Security policy

Report vulnerabilities privately. Do not attach credentials, customer names, proprietary data, licensed standards, or production object/job identifiers.

Treat carrier IDs, slot maps, object attributes/actions, recipe IDs, and job commands as untrusted application input. The library validates state and identifiers but does not authenticate callers, persist audit data, encrypt transport, or provide authorization. Integrators must add those controls outside the domain layer and bound event/object/job retention for their deployment.
