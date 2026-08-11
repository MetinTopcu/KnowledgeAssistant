import { z } from 'zod';

import { tokenUsageSchema } from '@/shared/api/tokenUsage';

/**
 * One passage the agent retrieved while answering.
 *
 * **Deliberately not `AnswerCitation` from the retrieval feature**, mirroring
 * the server's own decision to keep the two records apart. The fields are
 * identical and the meanings are not: in retrieval, `referenceNumber` is a
 * position in a prompt the handler built, known before the model runs, with one
 * for every retrieved chunk. Here it is the order in which the agent *chose* to
 * look things up — it depends on searches nobody scripted, it is not knowable
 * in advance, and duplicates are collapsed so the first sighting wins the
 * number. Sharing the type would force one meaning onto the other the first
 * time either changed.
 */
const agentCitationSchema = z.object({
  referenceNumber: z.number(),
  chunkId: z.string(),
  documentId: z.string(),
  chunkOrder: z.number(),
  text: z.string(),
  blobUri: z.string(),
  score: z.number(),
});

export type AgentCitation = z.infer<typeof agentCitationSchema>;

/**
 * The server's `AskAgentResponse`, validated rather than trusted.
 *
 * Note what is **not** here: no `retrievedChunkCount`, and no record of the
 * queries the agent ran. The response reports how many searches happened, not
 * what they were. Neither absence is filled in by this client.
 */
export const askAgentResponseSchema = z.object({
  answer: z.string(),
  citations: z.array(agentCitationSchema),
  /**
   * How many searches the agent ran. Zero means it answered without opening the
   * corpus at all — the field a reader should look at first, and the only thing
   * in the response that distinguishes a grounded answer from a confident one.
   */
  searchCount: z.number(),
  tokenUsage: tokenUsageSchema.nullable(),
  answeredAtUtc: z.string(),
});

export type AskAgentResponse = z.infer<typeof askAgentResponseSchema>;
