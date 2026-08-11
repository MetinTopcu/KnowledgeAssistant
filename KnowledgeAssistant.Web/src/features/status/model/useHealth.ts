import { useQuery, type UseQueryResult } from '@tanstack/react-query';

import { fetchHealth } from '@/features/status/api/fetchHealth';
import { type HealthSnapshot } from '@/features/status/model/probe';
import { type ApiRequestError } from '@/shared/api/ApiRequestError';

export const healthQueryKey = ['health'] as const;

/**
 * Reads the health endpoints.
 *
 * `staleTime` is zero, against the shared default: health is the one thing in
 * this product where a cached answer is worse than no answer, and the server
 * says so itself — it sets `no-store` on every health response. Its own
 * `CacheSeconds` already bounds how hard the dependencies are actually probed,
 * so asking again is cheap.
 */
export function useHealth(): UseQueryResult<HealthSnapshot, ApiRequestError> {
  return useQuery<HealthSnapshot, ApiRequestError>({
    queryKey: healthQueryKey,
    queryFn: ({ signal }) => fetchHealth(signal),
    staleTime: 0,
  });
}
