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

**Note:** no ORM package is wired, and this folder is deliberately empty. No
part of this service owns durable write state — a document's bytes live in Blob
Storage and its metadata in a rebuildable Search projection — so an ORM would be
an abstraction with nothing to map. Add it here when a write model exists.
