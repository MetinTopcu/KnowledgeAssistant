import { z } from 'zod';

/** The three states `HealthStatus` serialises to. */
export const healthStatusSchema = z.enum(['Healthy', 'Degraded', 'Unhealthy']);

export type HealthStatus = z.infer<typeof healthStatusSchema>;

const healthEntrySchema = z.object({
  status: healthStatusSchema,
  durationMs: z.number(),
  /** The check's own message. Null when it did not supply one. */
  description: z.string().nullable(),
});

export type HealthEntry = z.infer<typeof healthEntrySchema>;

/**
 * The body the health endpoints write.
 *
 * `entries` is **optional on purpose**, not defensively: the server only
 * includes per-check detail when `Observability:HealthChecks:ExposeDetails` is
 * on, and that setting defaults to off. Absent entries therefore means "this
 * deployment does not publish per-check detail" — never "there are no
 * dependencies", and the screen must not render the two the same way.
 */
export const healthReportSchema = z.object({
  status: healthStatusSchema,
  totalDurationMs: z.number(),
  entries: z.record(z.string(), healthEntrySchema).optional(),
});

export type HealthReport = z.infer<typeof healthReportSchema>;

/**
 * Whether a check reports itself as switched off.
 *
 * `DependencyHealthCheck` returns **Healthy** when checks are disabled by
 * configuration, with a fixed-template description saying so. Left alone, a
 * dependency nobody is probing would show green — the kind of lie that ends an
 * incident review badly (§6.7).
 *
 * This is the one signal on the screen inferred rather than reported, so it is
 * deliberately additive: it raises a neutral "disabled" flag beside the status
 * and never replaces it. The reported status stays visible and verbatim, and if
 * this match ever goes stale the screen loses a hint rather than gaining a lie.
 * A `disabled` boolean in the payload would remove the guesswork entirely.
 */
export function reportsAsDisabled(entry: HealthEntry): boolean {
  return entry.description?.toLowerCase().includes('disabled by configuration') ?? false;
}
