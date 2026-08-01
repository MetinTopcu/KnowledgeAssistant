?# Application / Queries

**Read-only** use cases: `SearchKnowledgeBase`, `GetDocumentById`,
`ListIngestionJobs`. Same vertical-slice layout as `Commands/`.

**Why it exists:** reads vastly outnumber writes in a knowledge assistant, and
they have genuinely different constraints — projection instead of hydration,
caching, pagination, and relaxed consistency.

**Rule:** a query must never mutate state; that invariant is what makes caching
and read-replica routing safe. Query handlers may bypass the repository and
project straight into a DTO — loading a full aggregate to read three fields is
waste the write side needs and the read side does not.
