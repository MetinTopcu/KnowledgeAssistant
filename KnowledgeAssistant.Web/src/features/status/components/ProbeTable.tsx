import { toneForStatus } from '@/features/status/components/healthTone';
import { type ProbeOutcome } from '@/features/status/model/probe';
import { StatusDot } from '@/shared/components/StatusDot';

interface ProbeTableProps {
  readonly outcomes: readonly ProbeOutcome[];
}

/**
 * The three endpoints and what each one answered.
 *
 * A probe that could not be reached is shown as unreachable, not as unhealthy:
 * the server never said it was unhealthy, and inventing that verdict would put
 * a fabricated status on the one screen whose entire job is to report facts.
 */
export function ProbeTable({ outcomes }: ProbeTableProps) {
  return (
    <div className="overflow-x-auto rounded-sm border">
      <table className="w-full table-fixed border-collapse">
        <caption className="sr-only">Health probe endpoints and their current status</caption>
        <thead className="bg-canvas-subtle">
          <tr className="border-b text-fg-subtle">
            <th scope="col" className="w-36 px-3 text-left">
              <span className="eyebrow">Probe</span>
            </th>
            <th scope="col" className="w-32 px-3 text-left">
              <span className="eyebrow">Status</span>
            </th>
            <th scope="col" className="w-20 px-3 text-left">
              <span className="eyebrow">HTTP</span>
            </th>
            <th scope="col" className="w-28 px-3 text-left">
              <span className="eyebrow">Checks</span>
            </th>
            <th scope="col" className="px-3 text-left">
              <span className="eyebrow">Endpoint</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {outcomes.map((outcome) => (
            <tr
              key={outcome.probe.id}
              className="h-row-default border-b border-b-border-muted last:border-b-0"
            >
              <td className="px-3">
                <span className="text-ui" title={outcome.probe.purpose}>
                  {outcome.probe.label}
                </span>
              </td>

              <td className="px-3">
                {outcome.kind === 'answered' ? (
                  <StatusDot
                    tone={toneForStatus(outcome.report.status)}
                    label={outcome.report.status}
                  />
                ) : (
                  <StatusDot tone="neutral" label="Unreachable" />
                )}
              </td>

              <td className="px-3 font-mono text-mono-ui text-fg-muted tabular-nums">
                {outcome.kind === 'answered' ? outcome.httpStatus : '—'}
              </td>

              <td
                className="px-3 font-mono text-mono-ui text-fg-muted tabular-nums"
                title="Time the server spent running this probe's checks."
              >
                {outcome.kind === 'answered'
                  ? `${outcome.report.totalDurationMs.toFixed(1)}ms`
                  : '—'}
              </td>

              <td className="truncate px-3 font-mono text-mono-ui text-fg-subtle">
                {outcome.probe.path}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
