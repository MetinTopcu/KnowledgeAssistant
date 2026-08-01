?# Application / Abstractions

The **CQRS contracts this layer defines for itself**: `ICommand`,
`ICommand<TResponse>`, `IQuery<TResponse>`, `ICommandHandler<,>`,
`IQueryHandler<,>`, and any marker interfaces the pipeline keys off
(`ITransactionalRequest`, `ICacheableQuery`).

**Why it exists — and how it differs from `Interfaces/`:** this is the single
most important distinction in the layer, so be strict about it.

| | `Abstractions/` | `Interfaces/` |
|---|---|---|
| Direction | **Inward** — shapes this layer | **Outward** — ports to the world |
| Implemented by | Application itself (handlers) | Infrastructure adapters |
| Example | `ICommandHandler<T>` | `IBlobStorageService` |

Thin marker interfaces over raw `IRequest` buy you real leverage: pipeline
behaviours can target *only* commands, the type system stops a query from
mutating state, and swapping the mediator implementation later touches this
folder instead of every handler.

**Rule:** contracts only — no implementations, no Domain leakage outward.
