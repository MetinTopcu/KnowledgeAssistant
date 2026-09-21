import { useQuery, type UseQueryResult } from '@tanstack/react-query';

import { fetchDocuments } from '@/features/documents/api/fetchDocuments';
import { type DocumentList } from '@/features/documents/model/documentSummary';
import { type ApiRequestError } from '@/shared/api/ApiRequestError';
import { documentsQueryKey } from '@/shared/api/queryKeys';

/**
 * Reads the corpus.
 *
 * A query rather than a mutation: it is a `GET`, it is idempotent, it costs no
 * tokens, and it is exactly the kind of thing a cache is for.
 *
 * There is no "capability unavailable" branch any more. This screen used to
 * report a 405, because the collection was routed for `POST` only; the endpoint
 * exists now, so a failure here means something went wrong and is reported as
 * such. An empty corpus is a success with no documents, which is a different
 * thing and rendered differently.
 */
export function useDocuments(): UseQueryResult<DocumentList, ApiRequestError> {
  return useQuery<DocumentList, ApiRequestError>({
    queryKey: documentsQueryKey,
    queryFn: ({ signal }) => fetchDocuments(signal),
  });
}
