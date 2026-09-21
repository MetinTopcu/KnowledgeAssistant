?# Api / Endpoints

Minimal API route groups, registered as `IEndpointRouteBuilder` extensions and
grouped with `MapGroup`.

**This folder is empty, and that is the recorded decision.** `Controllers/` and
`Endpoints/` overlap, and shipping both as the primary style guarantees an
inconsistent API where the pattern depends on who wrote it. Option 1 was chosen
(see ARCHITECTURE.md §6); the operational routes that would have lived here are
registered in `Api/Observability/HealthEndpointRegistration.cs`, because one
file beats a folder convention for three routes. The alternatives are kept
below so the next person can see what was weighed:

1. **Controllers primary — chosen.** Controllers carry
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
