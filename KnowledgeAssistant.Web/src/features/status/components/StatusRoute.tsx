import { RefreshCw } from 'lucide-react';

import { DependencyTable } from '@/features/status/components/DependencyTable';
import { ProbeTable } from '@/features/status/components/ProbeTable';
import { useHealth } from '@/features/status/model/useHealth';
import { Alert } from '@/shared/components/Alert';
import { Button } from '@/shared/components/Button';
import { ProblemDetailsView } from '@/shared/components/ProblemDetailsView';
import { Skeleton } from '@/shared/components/Skeleton';

const SKELETON_WIDTHS = ['w-2/3', 'w-1/2', 'w-3/5'];

/**
 * The Status screen.
 *
 * Two blocks, as in §6.7: the probes and what each answered, then the per-check
 * detail — when the deployment publishes any. Every value shown came from a
 * health response; nothing is defaulted, inferred, or filled in.
 */
export function StatusRoute() {
  const query = useHealth();

  const detailed = query.data?.outcomes.find(
    (outcome) => outcome.kind === 'answered' && outcome.report.entries !== undefined,
  );
  const entries = detailed?.kind === 'answered' ? detailed.report.entries : undefined;

  return (
    <div className="flex flex-col gap-6 p-6">
      <div className="flex items-center justify-between gap-4">
        <p className="max-w-answer text-ui text-fg-muted">
          Liveness never touches a dependency — it answers whether the process is running. Readiness
          does, and returns 503 when one is unreachable. They are deliberately different: restarting
          a container because a downstream service is slow turns a degradation into an outage.
        </p>

        <Button
          variant="default"
          size="sm"
          onClick={() => {
            void query.refetch();
          }}
          disabled={query.isFetching}
        >
          <RefreshCw size={14} strokeWidth={1.5} aria-hidden />
          <span>{query.isFetching ? 'Checking…' : 'Refresh'}</span>
        </Button>
      </div>

      {query.isPending && (
        <div className="flex flex-col gap-2 rounded-sm border p-3" aria-busy>
          {SKELETON_WIDTHS.map((width) => (
            <Skeleton key={width} className={`h-4 ${width}`} />
          ))}
        </div>
      )}

      {query.isError && (
        <Alert variant="danger" title="The health endpoints could not be read">
          <ProblemDetailsView problem={query.error.problem} />
        </Alert>
      )}

      {query.isSuccess && (
        <>
          <section className="flex flex-col gap-2">
            <h2 className="eyebrow text-fg-subtle">Probes</h2>
            <ProbeTable outcomes={query.data.outcomes} />
            <p className="text-caption text-fg-subtle">
              Checked{' '}
              <time dateTime={new Date(query.data.checkedAt).toISOString()}>
                {new Date(query.data.checkedAt).toLocaleTimeString()}
              </time>{' '}
              by this browser. Durations are the server&apos;s own measurement of each probe.
            </p>
          </section>

          <section className="flex flex-col gap-2">
            <h2 className="eyebrow text-fg-subtle">Dependencies</h2>

            {entries === undefined ? (
              <Alert variant="info" title="Per-check detail is not published by this deployment">
                The health endpoints answered, and the statuses above are authoritative — but they
                report an overall result only. Set{' '}
                <code className="font-mono text-mono-ui">
                  Observability:HealthChecks:ExposeDetails
                </code>{' '}
                to true to see each dependency separately. Until then this client cannot say which
                dependency a degraded result came from, and will not guess.
              </Alert>
            ) : (
              <DependencyTable entries={entries} />
            )}
          </section>
        </>
      )}
    </div>
  );
}
