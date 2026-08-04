import { type UploadDocumentResponse } from '@/features/document-upload/model/uploadDocumentResponse';

const BYTES_PER_MEGABYTE = 1024 * 1024;

/**
 * Below this many chunks per megabyte, extraction probably failed rather than
 * the document being short.
 *
 * A heuristic, not a rule from the server. It is set low on purpose: a false
 * "Low yield" on a legitimate document teaches people to ignore the warning,
 * which costs more than the occasional missed scan.
 */
const MINIMUM_CHUNKS_PER_MEGABYTE = 2;

/** Files smaller than this are exempt — a one-page PDF legitimately yields one chunk. */
const MINIMUM_SIZE_TO_JUDGE = BYTES_PER_MEGABYTE;

export type ChunkYieldVerdict = 'none' | 'low' | 'normal';

/**
 * Judges whether a document produced a plausible number of searchable passages.
 *
 * This exists because the server's own note on `ChunkCount` names the failure it
 * detects: "a large PDF that yields two chunks extracted badly". A scanned
 * document uploads and indexes successfully and is then unfindable, and nothing
 * else in the response says so.
 */
export function judgeChunkYield(result: UploadDocumentResponse): ChunkYieldVerdict {
  if (result.chunkCount === 0) {
    return 'none';
  }

  if (result.sizeInBytes < MINIMUM_SIZE_TO_JUDGE) {
    return 'normal';
  }

  const megabytes = result.sizeInBytes / BYTES_PER_MEGABYTE;
  return result.chunkCount / megabytes < MINIMUM_CHUNKS_PER_MEGABYTE ? 'low' : 'normal';
}
