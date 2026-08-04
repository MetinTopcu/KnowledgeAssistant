import { QueryClient } from '@tanstack/react-query';

import { isApiRequestError } from '@/shared/api/ApiRequestError';

/** Attempts after the first, for failures that are plausibly transient. */
const MAX_RETRIES = 2;

/**
 * Decides whether a failed request is worth repeating.
 *
 * Only the server can say whether a failure is transient, and it already does:
 * a 4xx means the request itself was wrong and will be wrong again, while the
 * API's own Polly pipeline has already exhausted sensible retries against Azure
 * before returning a 5xx. Retrying a validation error just spends the user's
 * time confirming what the first response said.
 */
function shouldRetry(failureCount: number, error: unknown): boolean {
  if (failureCount >= MAX_RETRIES) {
    return false;
  }

  if (!isApiRequestError(error)) {
    return false;
  }

  const { status, isNetworkError } = error.problem;

  if (isNetworkError) {
    return true;
  }

  // 429 is retryable, but only on the server's terms — retryDelay below honours
  // Retry-After rather than guessing.
  if (status === 429) {
    return true;
  }

  return status >= 500;
}

/**
 * Waits as long as the server asked, and otherwise backs off exponentially.
 *
 * Ignoring `Retry-After` on a 429 from Azure OpenAI is how a rate limit becomes
 * a longer rate limit.
 */
function retryDelay(attemptIndex: number, error: unknown): number {
  if (isApiRequestError(error)) {
    const { retryAfterSeconds } = error.problem;
    if (retryAfterSeconds !== undefined) {
      return retryAfterSeconds * 1000;
    }
  }

  return Math.min(1000 * 2 ** attemptIndex, 8000);
}

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      retry: shouldRetry,
      retryDelay,
      staleTime: 30_000,
      // Answers cost tokens. Refetching one because a window regained focus
      // spends real money to redisplay something the user is already reading.
      refetchOnWindowFocus: false,
    },
    mutations: {
      retry: false,
    },
  },
});
