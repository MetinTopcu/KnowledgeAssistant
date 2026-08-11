import { type AskAgentResponse } from '@/features/agent/model/askAgentResponse';

/**
 * One completed agent run.
 *
 * As with retrieval, what the server sent and what the client measured are kept
 * in separate fields so no reader has to guess which numbers came from the API.
 */
export interface AgentRun {
  readonly question: string;
  readonly maxSources: number;
  readonly response: AskAgentResponse;
  /**
   * Round-trip time as the browser experienced it, network included — not the
   * server's processing time. The API reports no duration of its own.
   *
   * It matters more here than in retrieval: an agent run's duration is not
   * predictable, because the agent decides how many times to search.
   */
  readonly elapsedMs: number;
  readonly correlationId: string | undefined;
  readonly traceId: string | undefined;
}
