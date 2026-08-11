import { z } from 'zod';

/**
 * What a completion cost.
 *
 * Shared between the retrieval and agent responses because it is genuinely one
 * type on the server too — both slices return the same
 * `Application.Interfaces.TokenUsage`. Nullable on the wire and nullable here:
 * the provider may report nothing, and a zero-retrieval answer never calls the
 * model at all.
 */
export const tokenUsageSchema = z.object({
  promptTokens: z.number(),
  completionTokens: z.number(),
  totalTokens: z.number(),
});

export type TokenUsage = z.infer<typeof tokenUsageSchema>;
