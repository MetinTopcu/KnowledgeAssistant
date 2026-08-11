import { toneForStatus } from '@/features/status/components/healthTone';
import { reportsAsDisabled, type HealthEntry } from '@/features/status/model/healthReport';
import { StatusDot } from '@/shared/components/StatusDot';

interface DependencyTableProps {
  readonly entries: Readonly<Record<string, HealthEntry>>;
}

/**
 * One row per registered check, exactly as the server reported it.
 *
 * The status and the description are both verbatim. The only addition is a
 * neutral "disabled" flag beside a check whose own description says it is
 * switched off — it sits *next to* the reported status rather than replacing
 * it, so nothing the server said is hidden or overwritten.
 */
export function DependencyTable({ entries }: DependencyTableProps) {
  const rows = Object.entries(entries);

  return (
    <div className="overflow-x-auto rounded-sm border">
      <table className="w-full table-fixed border-collapse">
        <caption className="sr-only">Registered health checks and their reported status</caption>
        <thead className="bg-canvas-subtle">
          <tr className="border-b text-fg-subtle">
            <th scope="col" className="w-44 px-3 text-left">
              <span className="eyebrow">Check</span>
            </th>
            <th scope="col" className="w-32 px-3 text-left">
              <span className="eyebrow">Status</span>
            </th>
            <th scope="col" className="w-24 px-3 text-left">
              <span className="eyebrow">Duration</span>
            </th>
            <th scope="col" className="px-3 text-left">
              <span className="eyebrow">Message</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {rows.map(([name, entry]) => (
            <tr key={name} className="h-row-default border-b border-b-border-muted last:border-b-0">
              <td className="truncate px-3 font-mono text-mono-ui text-fg-default" title={name}>
                {name}
              </td>

              <td className="px-3">
                <span className="flex items-center gap-2">
                  <StatusDot tone={toneForStatus(entry.status)} label={entry.status} />
                  {reportsAsDisabled(entry) && (
                    <span
                      className="rounded-xs border bg-canvas-inset px-1 text-mono-chip text-fg-subtle"
                      title="This check reports that it is switched off by configuration, so its status is not evidence that the dependency is reachable."
                    >
                      disabled
                    </span>
                  )}
                </span>
              </td>

              <td className="px-3 font-mono text-mono-ui text-fg-muted tabular-nums">
                {entry.durationMs.toFixed(1)}ms
              </td>

              <td className="px-3 text-ui text-fg-muted">
                {entry.description ?? <span className="text-fg-subtle">No message reported</span>}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
