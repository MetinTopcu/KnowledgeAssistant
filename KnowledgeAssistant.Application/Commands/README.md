?# Application / Commands

**State-changing** use cases: `IngestDocument`, `ReindexKnowledgeBase`,
`DeleteDocument`. One folder per feature, each holding the command, its handler,
and its validator co-located:

```
Commands/
  IngestDocument/
    IngestDocumentCommand.cs
    IngestDocumentCommandHandler.cs
    IngestDocumentCommandValidator.cs
```

**Why it exists:** the write side has different needs from the read side — it
loads aggregates, enforces invariants, and commits transactions. Separating it
from `Queries/` is the C and Q in CQRS, and it lets the two evolve and scale
independently.

**Why vertical slices:** everything that changes together sits together. Adding
a use case creates a folder; deleting one deletes a folder. Compare that to the
horizontal alternative, where a single feature is smeared across four
directories.

**Rule:** commands return void, an identifier, or a `Result` — never a full read
model. Returning the freshly written entity is the most common way CQRS quietly
collapses back into CRUD.
