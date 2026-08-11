import { type HealthStatus } from '@/features/status/model/healthReport';
import { type StatusTone } from '@/shared/components/StatusDot';

/** Hue carries meaning here and nowhere else on this screen (§2). */
const TONES: Record<HealthStatus, StatusTone> = {
  Healthy: 'success',
  Degraded: 'attention',
  Unhealthy: 'danger',
};

export function toneForStatus(status: HealthStatus): StatusTone {
  return TONES[status];
}
