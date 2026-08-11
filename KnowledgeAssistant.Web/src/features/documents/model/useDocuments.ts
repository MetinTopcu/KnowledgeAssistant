import { useQuery, type UseQueryResult } from '@tanstack/react-query';

import { fetchDocuments } from '@/features/documents/api/fetchDocuments';
import { type DocumentSummary } from '@/features/documents/model/documentSummary';
import { isApiRequestError, type ApiRequestError } from '@/shared/api/ApiRequestError';

/** The key other features invalidate once an upload has been ingested. */
export const documentsQueryKey = ['documents'] as const;

/**
 * True when the failure means "this API cannot do that yet" rather than
 * "that went wrong".
 *
 * 405 is the precise signal: `api/documents` is a routed path, so a `GET`
 * matches the route and finds no action for the method. 404 is accepted too,
 * for a deployment that routes the collection differently — both mean the read
 * capability is absent, which is a different thing from a failure and must not
 * be reported as one.
 */
export function isCapabilityUnavailable(error: unknown): boolean {
  return isApiRequestError(error) && (error.problem.status === 405 || error.problem.status === 404);
}

/**
 * Reads the corpus.
 *
 * A query rather than a mutation: it is a `GET`, it is idempotent, it costs no
 * tokens, and it is exactly the kind of thing a cache is for. The shared client
 * already declines to retry 4xx, so the 405 this currently returns is asked
 * once and reported, not hammered.
 */
export function useDocuments(): UseQueryResult<readonly DocumentSummary[], ApiRequestError> {
  return useQuery<readonly DocumentSummary[], ApiRequestError>({
    queryKey: documentsQueryKey,
    queryFn: ({ signal }) => fetchDocuments(signal),
  });
}
