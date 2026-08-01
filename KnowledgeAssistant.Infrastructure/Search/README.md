?# Infrastructure / Search

The retrieval subsystem: the `IVectorSearchService` implementation, index schema
definitions, index-provisioning code, chunking and embedding orchestration,
hybrid (keyword + vector) query construction, semantic ranking configuration,
and result-to-DTO mapping.

**Why it has its own top-level folder rather than sitting under `Azure/`:**
retrieval is the core capability of a knowledge assistant, not one integration
among many — it carries the most logic, changes most often, and is the piece
most likely to be re-platformed. Giving it a dedicated seam means the day you
evaluate a different vector store, the blast radius is this folder.

**Rule:** the index schema is a **derived artefact**. Treat it as
code-defined and rebuildable, never hand-edited in the portal, or your
environments will drift and nobody will be able to say what production's schema
actually is. Chunking strategy and embedding model are versioned decisions —
record which produced each index so a model change triggers a deliberate rebuild
rather than a silently mixed index.
