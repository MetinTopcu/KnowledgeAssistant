?# Domain / Entities

Objects with **identity and a lifecycle** — `Document`, `KnowledgeBase`,
`IngestionJob`, `DocumentChunk`. Each aggregate root owns its invariants and is
the only legal entry point for mutating the objects inside its boundary.

**Why it exists:** this is the enterprise model, the part of the system that
would still be true if you deleted ASP.NET, Azure, and the database. Isolating
it is the entire point of Clean Architecture — it is the layer with the longest
half-life and the highest cost of churn.

**Rule:** no attributes from a persistence framework, no `JsonPropertyName`, no
`SearchableField`. If an entity needs to know how it is stored or serialised,
the mapping belongs in Infrastructure instead. Constructors and factory methods
enforce invariants; public setters are how those invariants get bypassed.
