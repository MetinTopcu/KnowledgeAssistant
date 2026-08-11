import { type HealthReport } from '@/features/status/model/healthReport';
import { type ApiProblem } from '@/shared/api/apiProblem';

export interface ProbeDescriptor {
  readonly id: 'overall' | 'liveness' | 'readiness';
  readonly path: string;
  readonly label: string;
  readonly purpose: string;
}

/**
 * The three endpoints, and why they are not interchangeable.
 *
 * Liveness and readiness are deliberately different on the server: liveness
 * never touches a dependency, readiness does and answers 503 when one is down.
 * Collapsing them would let a slow downstream service restart a healthy
 * container, which turns a degradation into an outage.
 */
export const PROBES: readonly ProbeDescriptor[] = [
  {
    id: 'overall',
    path: '/health',
    label: 'Overall',
    purpose: 'Every registered check, for humans.',
  },
  {
    id: 'liveness',
    path: '/health/live',
    label: 'Liveness',
    purpose: 'Is the process running? Never touches a dependency.',
  },
  {
    id: 'readiness',
    path: '/health/ready',
    label: 'Readiness',
    purpose: 'Are dependencies reachable? Answers 503 when one is not.',
  },
];

/**
 * One probe's outcome.
 *
 * A probe that answered and a probe that could not be reached are separate
 * shapes, because they are separate facts: 503 with a body is a *reported*
 * unhealthy state, while a transport failure means nothing is known at all.
 * Flattening them would let "unreachable" render as "unhealthy".
 */
export type ProbeOutcome =
  | {
      readonly kind: 'answered';
      readonly probe: ProbeDescriptor;
      readonly httpStatus: number;
      readonly report: HealthReport;
      readonly elapsedMs: number;
    }
  | {
      readonly kind: 'unreachable';
      readonly probe: ProbeDescriptor;
      readonly problem: ApiProblem;
    };

export interface HealthSnapshot {
  readonly outcomes: readonly ProbeOutcome[];
  /** When this client received the responses. */
  readonly checkedAt: number;
}
