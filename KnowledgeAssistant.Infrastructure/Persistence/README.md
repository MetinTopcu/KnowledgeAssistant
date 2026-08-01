?# Infrastructure / Persistence

The durable **state-of-record** store: `DbContext` (or equivalent), entity type
configurations, migrations, repository implementations, and the `IUnitOfWork`
implementation.

**Why it exists — and why it is not the same as Search:** this holds the
authoritative record of documents, ingestion jobs, tenants, and audit history.
Azure AI Search holds a *derived, rebuildable projection* of that data. Conflating
them is the mistake to avoid: a search index is a query accelerator, not a
database. You must be able to delete the entire index and rebuild it from here.

**Rule:** persistence attributes and mappings live in this folder's
configuration classes, keeping Domain entities free of infrastructure concerns.
Repositories return **Domain entities**, never DTOs and never provider types.

**Note:** no ORM package is wired in this scaffold, since the stack you specified
did not name one. Add it here when the write model is settled.
