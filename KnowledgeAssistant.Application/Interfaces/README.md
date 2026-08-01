?# Application / Interfaces

The **outbound ports** this layer requires and Infrastructure implements:
`IBlobStorageService`, `IVectorSearchService`, `IChatCompletionService`,
`IDocumentRepository`, `IUnitOfWork`, `IDateTimeProvider`, `ICurrentUserService`.

**Why it exists:** this folder *is* the Dependency Inversion Principle made
physical. Application needs to store a blob but must not reference
`Azure.Storage.Blobs`, so it declares the interface it wants and Infrastructure
conforms. The dependency arrow points inward against the flow of control, which
is precisely what keeps Azure swappable and handlers unit-testable with a fake.

Contrast with `Abstractions/` — see that folder's README for the full split.

**Rule:** name and shape these in **your** domain's language, not the vendor's.
`IVectorSearchService.SearchAsync(query, topK)` is a port; an interface that
returns `SearchResults<SearchDocument>` is Azure's SDK wearing a disguise, and
it will leak the vendor straight through the boundary you built to contain it.
Every method returns `Task`/`ValueTask` and takes a `CancellationToken`.
