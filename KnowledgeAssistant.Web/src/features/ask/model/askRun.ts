import { type AskQuestionResponse } from '@/features/ask/model/askQuestionResponse';

/**
 * One completed question, with everything needed to judge it.
 *
 * The response is the server's; the rest is what only the client can know. They
 * are kept in separate fields rather than merged so that no reader has to guess
 * which numbers came from the API.
 */
export interface AskRun {
  readonly question: string;
  readonly topK: number;
  readonly response: AskQuestionResponse;
  /**
   * Wall-clock time from request dispatch to response parsed, measured here.
   *
   * This is round-trip time as the browser experienced it — network included —
   * not the server's processing time. The API reports no duration of its own,
   * and labelling a client measurement as server latency would misattribute
   * every slow connection to the service.
   */
  readonly elapsedMs: number;
  readonly correlationId: string | undefined;
  readonly traceId: string | undefined;
}
