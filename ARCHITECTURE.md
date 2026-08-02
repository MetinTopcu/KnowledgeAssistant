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

## 5a. The ingestion pipeline, and what happens when it half-fails

One command drives six stages:

```
upload → download → chunk → embed → index chunks → index document
```

The handler orchestrates and computes nothing; each stage is a port that owns its
own rules, retries, and failure taxonomy. Service errors pass through unwrapped,
so the error code the caller sees (`Chunking.*`, `Embedding.*`, `VectorIndex.*`)
names the stage that broke.

**Bytes are re-read from storage rather than reusing the uploaded stream.**
Rewinding the request stream only works because ASP.NET Core buffers request
bodies — a property of one delivery mechanism. Reading from storage keeps the
pipeline runnable from a queue message or a reconciliation job, and makes storage
the single source of truth for what is actually processed.

**The document index is written last, and that is what makes partial failures
tractable.** Its presence is the ingestion-complete marker:

- blob **with** a document-index entry → fully ingested
- blob **without** one → incomplete ingestion, whatever stage it stopped at

So the orphaned-blob question has an operational answer. A reconciliation pass
lists blobs, reads the `documentId` the storage adapter writes into blob
metadata, checks the document index, and re-drives anything missing. It converges
rather than duplicating, because every step is idempotent by construction: the
blob name derives from the document id, chunk ids derive from the document id and
position, and both indexes upsert.

**Orphaned blobs are never deleted.** The blob is the input and the only artefact
ingestion can be replayed from. A compensating delete destroys the user's upload
to tidy up a retryable failure, and a delete that itself fails leaves the original
problem plus a half-executed rollback.

**Known limitation.** Ingestion is synchronous, so a caller waits through
extraction, embedding, and two index writes, and a disconnect cancels nearly
finished work. The fix is to store-and-enqueue, running the pipeline from the
queue — at which point the reconciliation rule above becomes the queue's retry
policy. That is a change of shape, not of logic: every stage moves unmodified.

---

## 5b. The retrieval pipeline

One query drives four stages:

```
embed question → vector search → build prompt → chat completion
```

**The question is embedded by the same service that embedded the corpus.** Not
for tidiness: vectors from different models are not comparable, and comparing
them yields a search that returns plausible nonsense rather than an error.
`IEmbeddingService` carries both a chunk overload and a single-text overload, and
both route through one private request path, so batching, retry, order
verification, and the dimension check cannot diverge between indexing and
querying.

**Retrieving nothing is a success.** With no sources, the system prompt leaves
the model nothing to do but decline — so the handler returns that answer directly
and never calls the model. It saves the call and removes the one opportunity for
an ungrounded answer to appear.

**Citations are numbered from what the model was actually sent.** The prompt
builder and the citation list walk the same ordered chunk list and stop at the
same context budget, so a `[2]` in the answer resolves to the second citation. A
citation list longer than the evidence sent would credit the answer to text the
model never saw.

**The prompt lives in Application, not Infrastructure.** It encodes product
rules — ground everything, cite sources, decline when the evidence is absent —
that must survive changing the model behind them.

**Nothing here writes.** A failure at any stage leaves no state to reconcile,
which is the sharp contrast with ingestion, where every stage past the first can
leave an orphaned blob.

---

## 5c. The agent, and why it is a second pipeline rather than a flag

`POST /api/questions/agent` answers the same kind of question as `5b` by
inverting who decides what to retrieve. The retrieval pipeline searches once and
hands the model evidence; the agent is *given* the ability to search and decides
whether, how often, and with what wording.

```
question → agent ⇄ search_knowledge_base (embed → vector search) → answer
```

**Its knowledge source is the pipeline in 5b, not a second one.** The agent's one
tool runs in this process and calls `IEmbeddingService` and `IAzureSearchService`
— the same two ports, in the same order, with the same retry policy and the same
error codes. Azure AI Foundry offers a server-side Azure AI Search tool that would
have removed that code entirely; it was rejected because it embeds queries with
the *index's* vectorizer rather than with `IEmbeddingService`, so query and corpus
vectors would come from two independently configured models. That failure does not
raise an error — it returns plausible, wrong passages.

**Tool registration is entirely inside Infrastructure; the instructions are
entirely inside Application.** The function's name, its JSON schema, its dispatch,
and the loop that answers its calls are vendor mechanics
(`Infrastructure/Azure/Agents`). What the agent is *told* — search before
answering, ground every claim, cite, decline when the corpus is silent — is a
product rule (`Application/Agents/AgentInstructions`), for exactly the reason the
grounded prompt is. Application never learns that a tool exists.

**The search count is part of the response, not just the log.** An agent may
answer without searching, and when it does the answer came from training data
about documents it never opened — indistinguishable from a grounded answer by
reading it. `SearchCount` is the only field that separates the two, which is why
it is reported to the caller, counted as a metric, and logged as a warning.

**Two bounded costs, because the product of two unbounded ones is unbounded.**
`MaxSources` bounds one search; `MaxToolIterations` bounds how many searches there
can be. Reaching the second is not an error: the agent is told searching is over
and asked to answer with what it has, because a partial grounded answer beats a
500.

**Provisioning is fingerprinted.** The agent definition — model, instructions,
temperature, tool schema — is hashed into the agent version's metadata, so a
fleet converges on one version, a redeploy that changed nothing creates nothing,
and editing the instructions creates a version precisely because they changed.
Pinning `Azure:AiFoundry:Agent:Version` disables all of it, which is the
production arrangement.

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
