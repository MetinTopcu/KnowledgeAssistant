import { useMutation, type UseMutationResult } from '@tanstack/react-query';

import { askAgent } from '@/features/agent/api/askAgent';
import { type AgentRun } from '@/features/agent/model/agentRun';
import { type AskAgentFormValues } from '@/features/agent/model/askAgentForm';
import { type ApiRequestError } from '@/shared/api/ApiRequestError';

/**
 * A mutation, not a query: a `POST` that spends tokens on every call and must
 * never be cached or refetched on a window focus.
 *
 * Retries are off, and more emphatically than for retrieval — an agent run can
 * perform several searches and several completions before it returns, so a
 * silent retry could multiply an already unpredictable cost. A failed run is
 * shown with the server's problem document and repeated only if the user says
 * so.
 */
export function useAskAgent(): UseMutationResult<AgentRun, ApiRequestError, AskAgentFormValues> {
  return useMutation<AgentRun, ApiRequestError, AskAgentFormValues>({
    mutationFn: (values) => askAgent(values),
    retry: false,
  });
}
