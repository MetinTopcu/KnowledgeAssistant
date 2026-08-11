import { useMutation, type UseMutationResult } from '@tanstack/react-query';

import { askQuestion } from '@/features/ask/api/askQuestion';
import { type AskQuestionFormValues } from '@/features/ask/model/askQuestionForm';
import { type AskRun } from '@/features/ask/model/askRun';
import { type ApiRequestError } from '@/shared/api/ApiRequestError';

/**
 * Asking is a mutation, not a query.
 *
 * It is a `POST`, it is not idempotent in cost — every call spends tokens — and
 * its result must never be served from a cache or refetched on a window focus.
 * Modelling it as a query would make all three of those the default and require
 * switching each one off.
 *
 * Retries are off for the same reason: the shared client retries 5xx and 429,
 * which is right for a read but wrong for a request that bills on each attempt.
 * A failed run is shown with the server's problem document and re-run only if
 * the user decides to.
 */
export function useAskQuestion(): UseMutationResult<
  AskRun,
  ApiRequestError,
  AskQuestionFormValues
> {
  return useMutation<AskRun, ApiRequestError, AskQuestionFormValues>({
    mutationFn: (values) => askQuestion(values),
    retry: false,
  });
}
