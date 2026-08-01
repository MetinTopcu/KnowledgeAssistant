?# Domain / Enums

Closed vocabularies owned by the business: `DocumentStatus`, `IngestionStage`,
`ChunkingStrategy`, `SensitivityLabel`.

**Why it exists:** a named set is self-documenting where a `string` or `int` is
not, and it lets the compiler catch an unhandled case in a switch expression.
Grouping them keeps the domain's vocabulary discoverable in one place.

**Rule:** if a value needs behaviour, extra data, or is likely to be extended by
configuration rather than a code change, it is a **smart enum / value object**,
not an `enum` — put it in `ValueObjects/`.
