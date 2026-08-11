import { z } from 'zod';

import { tokenUsageSchema } from '@/shared/api/tokenUsage';

/**
 * One source the answer was grounded in.
 *
 * Every retrieved chunk is returned and numbered exactly as the prompt numbered
 * it — including passages the answer ignored. `text` is the full chunk rather
 * than an excerpt, because the point of a citation is to be checkable and an
 * ellipsis is where a misattribution hides.
 */
const answerCitationSchema = z.object({
  referenceNumber: z.number(),
  chunkId: z.string(),
  documentId: z.string(),
  chunkOrder: z.number(),
  text: z.string(),
  blobUri: z.string(),
  score: z.number(),
});

export type AnswerCitation = z.infer<typeof answerCitationSchema>;

/**
 * The server's `AskQuestionResponse`, validated rather than trusted.
 *
 * `tokenUsage` is nullable on the wire and nullable here: the provider may not
 * report it, and a zero-retrieval answer never called the model at all. It is
 * never defaulted to zero — "not reported" and "cost nothing" are different
 * facts, and a client cannot tell them apart by inventing one.
 */
export const askQuestionResponseSchema = z.object({
  answer: z.string(),
  citations: z.array(answerCitationSchema),
  retrievedChunkCount: z.number(),
  tokenUsage: tokenUsageSchema.nullable(),
  answeredAtUtc: z.string(),
});

export type AskQuestionResponse = z.infer<typeof askQuestionResponseSchema>;
