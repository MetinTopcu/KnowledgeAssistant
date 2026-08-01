?# Application / Behaviors

MediatR `IPipelineBehavior<,>` implementations — the cross-cutting concerns that
wrap **every** request: validation, logging/correlation, performance timing,
transactions/unit-of-work, caching, retry policy, authorisation.

**Why it exists:** this is the Decorator pattern applied to the use-case
boundary, and it is the main reason to adopt MediatR at all. Without it, every
handler opens with the same defensive prologue. With it, a handler contains only
the use case, and a new concern is one class registered once rather than an edit
to a hundred files — Single Responsibility and Open/Closed, enforced structurally.

**Rule:** registration order *is* behaviour. The pipeline is LIFO-nested, so
register outermost-first — typically:
`Logging → Validation → Authorisation → Transaction → Caching → handler`.
Validation must run before anything opens a transaction. Keep each behaviour
oblivious to any specific use case; the moment one contains a `switch` over
request types, it belongs in a handler.
