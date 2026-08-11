import { z } from 'zod';

/**
 * Limits mirrored from `AskAgentQueryValidator` so the controls can exist —
 * a counter needs a maximum, a stepper needs bounds.
 *
 * Declared here rather than imported from the retrieval feature, because the
 * server declares them separately too: `MaxSourcesPerSearch` bounds one search
 * while the adapter's iteration ceiling bounds how many searches there are, and
 * neither is the retrieval slice's `MaxTopK`. They happen to share values
 * today; they are not the same rule.
 *
 * **These are affordances, not a second validator.** The server remains the
 * only authority, and anything that slips past is still refused with a problem
 * document.
 */
export const MAX_QUESTION_LENGTH = 2_000;
export const MIN_MAX_SOURCES = 1;
export const MAX_MAX_SOURCES = 20;

/** Matches `QuestionsController.DefaultMaxSources`. */
export const DEFAULT_MAX_SOURCES = 5;

/** The point where the counter starts warning, at 90% of the limit. */
export const QUESTION_LENGTH_WARNING = 1_800;

export const askAgentFormSchema = z.object({
  question: z
    .string()
    .trim()
    .min(1, 'A question is required.')
    .max(
      MAX_QUESTION_LENGTH,
      `Questions are limited to ${String(MAX_QUESTION_LENGTH)} characters.`,
    ),
  maxSources: z.number().int().min(MIN_MAX_SOURCES).max(MAX_MAX_SOURCES),
});

export type AskAgentFormValues = z.infer<typeof askAgentFormSchema>;
