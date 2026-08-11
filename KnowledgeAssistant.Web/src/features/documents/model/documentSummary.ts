import { z } from 'zod';

/**
 * One document in the corpus.
 *
 * **The shape is predicted, not observed.** No list endpoint exists yet, so
 * this mirrors `UploadDocumentResponse` — the only description the API gives of
 * an ingested document. It is written as a schema rather than a bare type on
 * purpose: if the real endpoint ships with a different shape, parsing fails
 * loudly at the boundary instead of rendering `undefined` as a plausible blank
 * cell.
 *
 * Declared here rather than shared with the upload feature. The two are
 * different endpoints and free to diverge, and a shared type would quietly
 * force one's shape onto the other the first time either changed.
 *
 * `blobUri` is deliberately absent, as it is from the upload response: it names
 * internal storage topology that no client can use.
 */
export const documentSummarySchema = z.object({
  documentId: z.string(),
  fileName: z.string(),
  contentType: z.string(),
  sizeInBytes: z.number(),
  blobName: z.string(),
  chunkCount: z.number(),
  receivedAtUtc: z.string(),
});

export type DocumentSummary = z.infer<typeof documentSummarySchema>;

export const documentListSchema = z.array(documentSummarySchema);
