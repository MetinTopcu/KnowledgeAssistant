import { type AgentRun } from '@/features/agent/model/agentRun';
import { type AskAgentFormValues } from '@/features/agent/model/askAgentForm';
import { askAgentResponseSchema } from '@/features/agent/model/askAgentResponse';
import { CORRELATION_ID_HEADER, TRACE_ID_HEADER } from '@/shared/api/apiProblem';
import { httpClient } from '@/shared/api/httpClient';
import { readHeader } from '@/shared/api/readHeader';

/**
 * Asks one question of the agent.
 *
 * A sibling route to `/api/questions` rather than a flag on it, because the two
 * make different promises: retrieval performs exactly one search, so its
 * latency and cost are predictable, while the agent decides how many times to
 * look and costs what it costs. That is a choice a caller should make
 * deliberately.
 */
export async function askAgent(
  values: AskAgentFormValues,
  signal?: AbortSignal,
): Promise<AgentRun> {
  const startedAt = performance.now();

  const response = await httpClient.post(
    '/api/questions/agent',
    { question: values.question, maxSources: values.maxSources },
    { signal },
  );

  const elapsedMs = performance.now() - startedAt;

  return {
    question: values.question,
    maxSources: values.maxSources,
    response: askAgentResponseSchema.parse(response.data),
    elapsedMs,
    correlationId: readHeader(response, CORRELATION_ID_HEADER),
    traceId: readHeader(response, TRACE_ID_HEADER),
  };
}
