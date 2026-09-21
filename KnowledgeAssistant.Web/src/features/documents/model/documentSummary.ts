import { z } from 'zod';

/**
 * One document in the corpus.
 *
 * **The shape is observed, not predicted.** It mirrors `DocumentSummary` from
 * `GET /api/documents`, which returns exactly what the search index holds about
 * a document — and no more. There is no size, content type, or chunk count
 * here because the index does not store them: they are known at upload and
 * reported by that response, but were never persisted for later reading.
 *
 * A schema rather than a bare type, so a server-side change fails loudly at the
 * boundary instead of rendering `undefined` as a plausible blank cell.
 *
 * Declared here rather than shared with the upload feature. The two are
 * different endpoints and free to diverge, and a shared type would quietly
 * force one's shape onto the other the first time either changed.
 *
 * `blobUri` is deliberately absent, as it is from the upload response: it names
 * internal storage topology that no client can use. `blobName` is kept — it is
 * the handle that locates the object for anyone who can reach storage.
 */
export const documentSummarySchema = z.object({
  documentId: z.string(),
  fileName: z.string(),
  blobName: z.string(),
  uploadedAtUtc: z.string(),
});

export type DocumentSummary = z.infer<typeof documentSummarySchema>;

/**
 * The listing, as the API returns it.
 *
 * An object rather than a bare array, matching the server: `truncated` says the
 * page was filled and there may be more, which a list alone cannot express.
 * It is a "maybe" — a corpus of exactly the requested size reports `true` while
 * having nothing further to show.
 */
export const documentListSchema = z.object({
  documents: z.array(documentSummarySchema),
  count: z.number(),
  truncated: z.boolean(),
});

export type DocumentList = z.infer<typeof documentListSchema>;
