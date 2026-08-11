import { healthReportSchema } from '@/features/status/model/healthReport';
import {
  PROBES,
  type HealthSnapshot,
  type ProbeDescriptor,
  type ProbeOutcome,
} from '@/features/status/model/probe';
import { isApiRequestError } from '@/shared/api/ApiRequestError';
import { httpClient } from '@/shared/api/httpClient';

/**
 * Statuses that carry a health report rather than a failure.
 *
 * 503 is the documented answer for an unhealthy readiness probe and it arrives
 * with a full body. Letting the client's error interceptor turn that into a
 * rejection would discard the very report the screen exists to show, so these
 * two are accepted and everything else is left to fail normally.
 */
function carriesReport(status: number): boolean {
  return status === 200 || status === 503;
}

async function probeOne(probe: ProbeDescriptor, signal?: AbortSignal): Promise<ProbeOutcome> {
  const startedAt = performance.now();

  try {
    const response = await httpClient.get(probe.path, {
      signal,
      validateStatus: carriesReport,
    });

    return {
      kind: 'answered',
      probe,
      httpStatus: response.status,
      report: healthReportSchema.parse(response.data),
      elapsedMs: performance.now() - startedAt,
    };
  } catch (error) {
    if (isApiRequestError(error)) {
      return { kind: 'unreachable', probe, problem: error.problem };
    }

    throw error;
  }
}

/**
 * Probes all three endpoints.
 *
 * Each is resolved independently so one failure cannot hide the other two —
 * during an incident, "readiness is down but liveness is fine" is the single
 * most useful thing this screen can say, and it is only sayable if a failing
 * probe does not reject the whole batch.
 */
export async function fetchHealth(signal?: AbortSignal): Promise<HealthSnapshot> {
  const outcomes = await Promise.all(PROBES.map((probe) => probeOne(probe, signal)));

  return { outcomes, checkedAt: Date.now() };
}
