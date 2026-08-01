?# Application / DTOs

The data shapes that **cross the boundary outward** — what queries return and
what the API serialises: `DocumentSummaryDto`, `SearchResultDto`,
`CitationDto`.

**Why it exists:** returning Domain entities from a use case couples your HTTP
contract to your enterprise model. Rename a private field for domain-modelling
reasons and you have silently shipped a breaking API change — and quite possibly
serialised data the caller was never meant to see.

**Rule:** flat, immutable, behaviour-free (`record` types are ideal). Mapping
happens in the handler or via a projection; the Domain never knows a DTO exists.
