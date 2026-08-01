?# Api / Endpoints

Minimal API route groups, registered as `IEndpointRouteBuilder` extensions and
grouped with `MapGroup`.

**A decision you owe the team — `Controllers/` and `Endpoints/` overlap.**
Both were requested, so both exist, but shipping both as the primary style
guarantees an inconsistent API where the pattern depends on who wrote it.
Choose one of these and write it down:

1. **Controllers primary (this scaffold's recommendation).** Controllers carry
   the versioned business surface; `Endpoints/` holds only lightweight
   operational routes — `/health`, `/ready`, `/version`, diagnostics — where the
   filter pipeline and model binding of MVC are pure overhead. Best fit for an
   enterprise API using versioning, conventions, and rich OpenAPI metadata.

2. **Minimal API primary.** `Endpoints/` becomes the whole surface and
   `Controllers/` is deleted. Lower per-request overhead, better AOT story,
   less ceremony.

**Rule:** whichever you choose, the endpoint body stays as thin as a controller
action — resolve `ISender`, dispatch, map the result. A lambda is not a licence
to inline business logic.
