import { z } from 'zod';

/**
 * The server's `UploadDocumentResponse`, validated rather than trusted.
 *
 * Parsing the response is not ceremony: a success here means the document was
 * stored, extracted, chunked, embedded, and written to both indexes, and
 * `chunkCount` is the only visible evidence of that. If the contract ever
 * changes shape, this fails loudly at the boundary instead of rendering
 * `undefined` chunks as a plausible zero.
 *
 * `blobUri` is absent by design on the server side — it names internal storage
 * topology no client can use — so it is absent here too.
 */
export const uploadDocumentResponseSchema = z.object({
  documentId: z.string(),
  fileName: z.string(),
  contentType: z.string(),
  sizeInBytes: z.number(),
  blobName: z.string(),
  chunkCount: z.number(),
  receivedAtUtc: z.string(),
});

export type UploadDocumentResponse = z.infer<typeof uploadDocumentResponseSchema>;
