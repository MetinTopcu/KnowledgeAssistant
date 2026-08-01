# Domain / Common

The base building blocks every other Domain type is composed from:
`Entity<TId>`, `AggregateRoot<TId>`, `ValueObject`, `IDomainEvent`,
and the `Result` / `Error` primitives if you model failure as a value.

**Why it exists:** `Entity` and `AggregateRoot` are not entities themselves, so
putting them in `Entities/` would blur the folder's meaning. Identity semantics,
equality contracts, and the domain-event collection live here once and are
inherited everywhere, so the rules cannot drift per aggregate.

**Rule:** these types are abstract or sealed primitives. No business policy here.
