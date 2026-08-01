?# Domain / Exceptions

Exceptions that represent a **violated business rule**:
`DocumentAlreadyIndexedException`, `InvalidChunkRangeException`.

**Why it exists:** they give a broken invariant a precise, catchable name, so the
API layer can map a rule violation to `409 Conflict` or `422` without
string-matching a generic message.

**Rule:** these signal *domain* rule violations only. Infrastructure faults
(timeouts, throttling, 5xx from Azure) are not domain exceptions and must not
appear here. Derive from a shared `DomainException` base so the global exception
middleware needs exactly one mapping rule.

**Design note:** exceptions should be exceptional. For predictable, expected
failures ("document not found", "validation failed") prefer a `Result<T>` from
`Common/` — using exceptions for ordinary control flow is both slow and noisy in
telemetry.
