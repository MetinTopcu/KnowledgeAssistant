import {
  documentListSchema,
  type DocumentSummary,
} from '@/features/documents/model/documentSummary';
import { httpClient } from '@/shared/api/httpClient';

/**
 * Lists the ingested documents.
 *
 * The endpoint does not exist yet — `api/documents` is routed, but only for
 * `POST`, so this currently answers 405. The request is still made rather than
 * short-circuited behind a flag, because a hard-coded "not supported" is itself
 * a claim about the server that goes stale the day the endpoint ships. Asking
 * and reporting the answer is self-correcting: when the route gains a `GET`,
 * this screen starts working with no change here.
 */
export async function fetchDocuments(signal?: AbortSignal): Promise<readonly DocumentSummary[]> {
  const response = await httpClient.get('/api/documents', { signal });
  return documentListSchema.parse(response.data);
}
