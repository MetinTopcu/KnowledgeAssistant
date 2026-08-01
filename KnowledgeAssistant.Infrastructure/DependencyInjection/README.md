?# Infrastructure / DependencyInjection

This layer's **composition root**: `AddInfrastructure(this IServiceCollection,
IConfiguration)`, plus focused partials such as `AddAzureClients`,
`AddPersistence`, `AddSearch`, and the options binding + validation for each.

**Why it exists:** it gives the API exactly one entry point into this layer.
`Program.cs` calls `AddInfrastructure(...)` and stays ignorant of every concrete
type behind it, so implementations can be renamed, split, or replaced without
touching the host.

**Why it matters architecturally:** without this folder, the wiring drifts into
`Program.cs`, and the API project quietly grows direct knowledge of
`BlobStorageService` and `AzureSearchService`. At that point the layer boundary
exists only in the folder names — the dependency is real and the abstraction is
decorative.

**Rule:** bind options with `.Bind(...).ValidateDataAnnotations().ValidateOnStart()`
so a missing endpoint or key fails at **startup**, not on the first user request
at 3 a.m. Register interfaces from `Application/Interfaces` against the
implementations in this project — never the concretions directly.
