import { z } from 'zod';

/**
 * Limits mirrored from the server so the controls can exist at all: a character
 * counter needs a maximum to count towards, and a stepper needs bounds to step
 * between.
 *
 * **These are affordances, not a second validator.** `AskQuestionQueryValidator`
 * remains the only authority — a request that gets past these is still sent and
 * still refused, and the server's problem document is what the user sees. If the
 * server's limits change, these go stale as labels, not as behaviour.
 */
export const MAX_QUESTION_LENGTH = 2_000;
export const MIN_TOP_K = 1;
export const MAX_TOP_K = 20;

/** Matches `QuestionsController.DefaultTopK`. */
export const DEFAULT_TOP_K = 5;

/** The point where the counter starts warning, at 90% of the limit. */
export const QUESTION_LENGTH_WARNING = 1_800;

export const askQuestionFormSchema = z.object({
  question: z
    .string()
    .trim()
    .min(1, 'A question is required.')
    .max(
      MAX_QUESTION_LENGTH,
      `Questions are limited to ${String(MAX_QUESTION_LENGTH)} characters.`,
    ),
  topK: z.number().int().min(MIN_TOP_K).max(MAX_TOP_K),
});

export type AskQuestionFormValues = z.infer<typeof askQuestionFormSchema>;
