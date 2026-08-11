import { askQuestionResponseSchema } from '@/features/ask/model/askQuestionResponse';
import { type AskRun } from '@/features/ask/model/askRun';
import { type AskQuestionFormValues } from '@/features/ask/model/askQuestionForm';
import { CORRELATION_ID_HEADER, TRACE_ID_HEADER } from '@/shared/api/apiProblem';
import { httpClient } from '@/shared/api/httpClient';
import { readHeader } from '@/shared/api/readHeader';

/**
 * Asks one question against the retrieval pipeline.
 *
 * `POST` rather than `GET`, matching the endpoint: a question is free text that
 * does not belong in a URL every proxy logs, and the response is neither
 * cacheable nor idempotent in cost.
 *
 * The elapsed measurement brackets exactly the request — it starts before
 * dispatch and stops once the body is parsed — so it reports the wait the user
 * actually had rather than a figure assembled from parts.
 */
export async function askQuestion(
  values: AskQuestionFormValues,
  signal?: AbortSignal,
): Promise<AskRun> {
  const startedAt = performance.now();

  const response = await httpClient.post(
    '/api/questions',
    { question: values.question, topK: values.topK },
    { signal },
  );

  const elapsedMs = performance.now() - startedAt;

  return {
    question: values.question,
    topK: values.topK,
    response: askQuestionResponseSchema.parse(response.data),
    elapsedMs,
    correlationId: readHeader(response, CORRELATION_ID_HEADER),
    traceId: readHeader(response, TRACE_ID_HEADER),
  };
}
