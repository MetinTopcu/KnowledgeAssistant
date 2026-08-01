# KnowledgeAssistant — Architecture

Enterprise AI Knowledge Assistant on .NET 9 and Azure.
This document defines the structure and the rules that keep it intact. Each
folder additionally carries its own `README.md` explaining that folder's purpose
in detail.

**Status: architecture only.** No business logic, no implementations, no
placeholder classes. Every folder is empty by design and documented rather than
filled with stubs that would have to be deleted later.

---

## 1. The dependency rule

One rule governs everything else:

> Source code dependencies point **inward only**. Nothing in an inner circle
> knows anything about an outer circle.

```
                 ┌──────────────────────────────────┐
                 │              Api                 │  HTTP, DI composition root
                 │  ┌────────────────────────────┐  │
                 │  │      Infrastructure        │  │  Azure, persistence, adapters
                 │  │  ┌──────────────────────┐  │  │
                 │  │  │     Application      │  │  │  use cases, CQRS, ports
                 │  │  │  ┌────────────────┐  │  │  │
                 │  │  │  │    Domain      │  │  │  │  entities, invariants
                 │  │  │  └────────────────┘  │  │  │
                 │  │  └──────────────────────┘  │  │
                 │  └────────────────────────────┘  │
                 └──────────────────────────────────┘
```

Enforced today by project references:

| Project | References | Packages |
|---|---|---|
| **Domain** | *(nothing)* | *(none — deliberately)* |
| **Application** | Domain | MediatR, FluentValidation, `*.Abstractions` only |
| **Infrastructure** | Application, Domain | Azure SDKs, hosting primitives |
| **Api** | Application, Infrastructure | Serilog, OpenAPI |

`Domain.csproj` having no `<ItemGroup>` at all is the point, not an oversight.
If it ever grows one, the enterprise model has been contaminated by a framework.

### The one compromise

`Api → Infrastructure` violates a strict reading of the rule. It is the standard
and accepted exception: something must be the composition root, and that is the
outermost layer. It is kept honest by convention — **only `Api/Extensions/` may
reference an Infrastructure type**, via a single `AddInfrastructure(...)` call.
Controllers and endpoints see Application alone.

If you want that enforced by the compiler rather than by discipline, add a
`KnowledgeAssistant.Bootstrap` project that references Infrastructure and expose
only the DI extension to the Api. Worth it on a large team; overhead on a small
one.

---

## 2. Why the flow of control runs opposite to the dependencies

This is the part that makes Clean Architecture worth the ceremony.

At **runtime**, an ingestion request flows outward:
`Controller → Handler → IBlobStorageService → Azure Blob`.

At **compile time**, the arrow between the last two points *backwards*:
`Application` declares `IBlobStorageService`; `Infrastructure` implements it.
Application never references Azure.

That inversion buys three concrete things:

1. **Testability.** A handler is unit-tested against fakes. No emulator, no
   `WebApplicationFactory`, no network, no Azure subscription — tests that run in
   milliseconds and can run on a laptop offline.
2. **Replaceability.** Azure AI Search is a detail behind `IVectorSearchService`.
   Evaluating a different vector store is a new adapter, not a rewrite.
3. **Build-time isolation.** Because Application *cannot* reference
   `Azure.Storage.Blobs`, the boundary cannot erode by accident. Someone has to
   deliberately edit a `.csproj` to break it — which is exactly the kind of
   change that gets caught in review.

---

## 3. CQRS and the request pipeline

Commands and queries are separated because they have genuinely different
requirements — writes load aggregates and enforce invariants; reads project,
cache, and paginate. In a knowledge assistant, reads dominate by orders of
magnitude and benefit from optimisations that would be unsafe on the write side.

Every request passes through the same MediatR pipeline. Register
outermost-first; the pipeline nests LIFO:

```
Request
  └─ LoggingBehavior          correlation id, timing
      └─ ValidationBehavior   FluentValidation — fail fast, before any transaction
          └─ AuthorizationBehavior
              └─ TransactionBehavior   (commands only, via ITransactionalRequest)
                  └─ CachingBehavior   (queries only, via ICacheableQuery)
                      └─ Handler       ← the only place a use case lives
```

Validation must precede the transaction behaviour, or invalid input opens and
rolls back transactions for no reason.

There are two distinct pipelines in this system, and keeping them straight
matters: **middleware** (`Api/Middleware`) wraps every *HTTP request*;
**behaviors** (`Application/Behaviors`) wrap every *use case*. Correlation IDs
and CORS are the former. Validation and transactions are the latter.

---

## 4. Two distinctions the folder names do not make obvious

**`Application/Abstractions` vs `Application/Interfaces`** — both hold
interfaces, and conflating them dissolves the layer's structure:

- `Abstractions/` points **inward**: contracts Application defines *and
  implements* — `ICommand`, `IQueryHandler<,>`. They shape the pipeline.
- `Interfaces/` points **outward**: ports Application *requires* and
  Infrastructure satisfies — `IBlobStorageService`. They are the DIP boundary.

**`Infrastructure/Persistence` vs `Infrastructure/Search`** — Persistence is the
authoritative state of record. Search is a **derived, rebuildable projection**.
A search index is a query accelerator, not a database; you must always be able to
drop the index and rebuild it from persistence. Systems that forget this end up
with an index nobody dares delete.

---

## 5. Decisions made in this scaffold

**MediatR is pinned to 12.4.1, the last Apache-2.0 release.** Version 13 and
later ship a proprietary licence and require paid commercial licensing above a
revenue threshold. Moving to 14.x is a procurement decision, not a version bump.
Because handlers depend on `Abstractions/`, an eventual migration away from
MediatR is contained.

**Central Package Management** (`Directory.Packages.props`) — every version
declared once, with transitive pinning on, eliminating the drift where two
projects silently bind different versions of the same assembly.

**`Microsoft.Extensions.*` is pinned to 10.0.x** although the apps target
`net9.0`. Those packages multi-target and run fine on net9.0; the 10.x floor is
forced transitively by `Microsoft.Extensions.Azure` and `Serilog.AspNetCore`.
`Microsoft.AspNetCore.*` stays on 9.0.x — ASP.NET Core packages are
framework-bound.

**A repo-scoped `NuGet.config`** clears machine-level feeds so restores are
reproducible on a laptop and a build agent alike, and adds package source
mapping — required by CPM here, and the standard mitigation for
dependency-confusion attacks.

**Warnings are errors**, with nullable reference types and code style enforced in
build. Cheap now, extremely expensive to retrofit later.

**Credentials are designed out rather than hidden.** Authentication is Entra ID
via `DefaultAzureCredential` — user secrets locally, environment variables and
managed identity in production — so `appsettings.json` holds endpoints and
deployment names and there is no key to leak. See **`CONFIGURATION.md`**.

**Additions beyond your specified list**, called out for transparency:
- `Domain/Common` — `Entity<TId>`, `AggregateRoot`, `ValueObject` base types
  need a home; putting them in `Entities/` would blur that folder's meaning.
- `Infrastructure/Azure/{Common,Blob,AiFoundry}` — sub-folders so one flat
  `Azure/` does not become a grab bag as three services accumulate.

---

## 6. Open decisions for you

**`Api/Controllers` vs `Api/Endpoints` overlap.** Both were requested and both
exist, but shipping both as the primary style produces an API whose shape depends
on its author. The recommendation: controllers carry the versioned business
surface; `Endpoints/` holds only operational routes (`/health`, `/ready`,
`/version`). Decide and record it.

**No ORM is wired.** Your stack did not name one, and `Persistence/` should not
be committed to EF Core before the write model exists.

**No test project yet.** When you add one, add an architecture fitness test
(NetArchTest or ArchUnitNET) that fails the build when a rule here is broken:

- Domain has no dependency on Application, Infrastructure, or any third party
- Application has no dependency on Infrastructure or `Azure.*`
- Nothing in `Api/` outside `Extensions/` references an Infrastructure type
- Every `ICommand`/`IQuery` has a handler; every request type has a validator

Documented rules erode. Executable rules do not.

---

## 7. Next steps, in order

1. Define Domain primitives in `Domain/Common`, then the first aggregate.
2. Define the CQRS contracts in `Application/Abstractions`.
3. Declare the first outbound ports in `Application/Interfaces`.
4. Build the pipeline in `Application/Behaviors` (logging + validation first).
5. Implement `AddInfrastructure` with options binding and `ValidateOnStart`.
6. Compose `Program.cs` from `Api/Extensions`, wire Serilog and the global
   exception handler.
7. Add the test project and the fitness tests above **before** the codebase is
   large enough for the rules to have already been broken.
