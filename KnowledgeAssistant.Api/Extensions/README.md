?# Api / Extensions

`IServiceCollection` and `IApplicationBuilder` extension methods that compose the
host: `AddApiServices`, `AddSwaggerDocumentation`, `AddApiVersioning`,
`AddAuthenticationAndAuthorization`, `UseApiPipeline`, `AddSerilogLogging`.

**Why it exists:** it keeps `Program.cs` a readable table of contents rather
than three hundred lines of registration. `Program.cs` should be scannable in
one screen and state *what* the application is composed of; each extension
method holds *how*.

**Why it is also the boundary guard:** this folder is the **only** place in the
API project that should touch Infrastructure — a single `AddInfrastructure(...)`
call. Controllers and endpoints stay dependent on Application alone, which is
what keeps the layering real rather than aspirational.

**Rule:** one concern per extension method, each returning its builder so
registration stays fluent.
