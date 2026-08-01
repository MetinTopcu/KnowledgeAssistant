?# Domain / ValueObjects

Immutable concepts compared **by value, not identity**: `DocumentUri`,
`ChunkRange`, `Embedding`, `ContentHash`, `TenantId`.

**Why it exists:** it is where primitive obsession goes to die. A `TenantId`
that cannot be silently swapped with any other `Guid`, and an `Embedding` that
validates its own dimension count on construction, remove whole categories of
bug that no test would otherwise catch.

**Rule:** validate in the constructor and stay immutable — a value object must
be impossible to construct in an invalid state. Prefer `record` for structural
equality, but implement equality explicitly when semantics differ (e.g. a
case-insensitive URI).
