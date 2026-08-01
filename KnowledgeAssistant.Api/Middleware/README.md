?# Api / Middleware

Custom ASP.NET Core pipeline components: global exception handling
(`IExceptionHandler` / `ProblemDetails` mapping), correlation-ID propagation,
request/response logging enrichment, tenant resolution, security headers.

**Why it exists — and how it differs from `Application/Behaviors`:** both are
pipelines, and confusing them is a common source of duplicated concerns.

| | Middleware (here) | Behaviors (Application) |
|---|---|---|
| Scope | Every **HTTP request** | Every **use case** |
| Sees | Headers, status, sockets | Commands, queries, domain results |
| Example | Correlation ID, CORS | Validation, transactions |

A transport concern belongs here; a use-case concern belongs there. Static file
requests hit middleware and never reach a behaviour — which is exactly the
distinction.

**Rule:** exception handling is registered **first** so it wraps everything
downstream. Middleware must never leak an exception's stack trace or an internal
message to the client; map it to a `ProblemDetails` payload and log the detail
with the correlation ID for support to trace.
