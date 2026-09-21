import { documentListSchema, type DocumentList } from '@/features/documents/model/documentSummary';
import { httpClient } from '@/shared/api/httpClient';

/**
 * Lists the ingested documents, newest first.
 *
 * The server bounds the page itself — the default is a screenful — and reports
 * `truncated` when it filled the limit. No limit is sent from here: a client
 * that has no paging UI has no business choosing a page size, and asking for
 * the server's default keeps the two from disagreeing.
 */
export async function fetchDocuments(signal?: AbortSignal): Promise<DocumentList> {
  const response = await httpClient.get('/api/documents', { signal });
  return documentListSchema.parse(response.data);
}
