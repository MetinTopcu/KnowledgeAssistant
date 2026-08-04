import { type ApiProblem } from '@/shared/api/apiProblem';

interface ProblemDetailsViewProps {
  readonly problem: ApiProblem;
}

/**
 * Renders a problem document as the server sent it.
 *
 * Deliberately not the friendly-copy mapping of docs/DESIGN.md §13.3. Nothing
 * here is rewritten, re-titled, or summarised: the status, the title, the
 * detail, and every error code with its description appear exactly as returned,
 * because a paraphrase is a second source of truth that drifts from the first
 * and is impossible to search the server logs for.
 *
 * The identifiers are last and selectable, since quoting them is the single
 * most useful thing a user can do with a failure.
 */
export function ProblemDetailsView({ problem }: ProblemDetailsViewProps) {
  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-col gap-1">
        <div className="flex items-baseline gap-2">
          {problem.status > 0 && (
            <span className="shrink-0 font-mono text-mono-ui text-fg-subtle">{problem.status}</span>
          )}
          <p className="text-h3 text-fg-default">{problem.title}</p>
        </div>
        {problem.detail !== undefined && <p className="text-ui text-fg-muted">{problem.detail}</p>}
      </div>

      {problem.errors.length > 0 && (
        <dl className="flex flex-col gap-2">
          {problem.errors.map((error) => (
            <div key={`${error.code}:${error.description}`} className="flex flex-col gap-1">
              <dt>
                <code className="inline-block rounded-xs border bg-canvas-inset px-1.5 py-0.5 text-mono-chip text-fg-default">
                  {error.code}
                </code>
              </dt>
              <dd className="text-ui text-fg-muted">{error.description}</dd>
            </div>
          ))}
        </dl>
      )}

      {(problem.correlationId !== undefined || problem.traceId !== undefined) && (
        <dl className="flex flex-col gap-1 border-t pt-2 text-mono-ui text-fg-subtle">
          {problem.correlationId !== undefined && (
            <div className="flex gap-2">
              <dt className="w-12 shrink-0">cid</dt>
              <dd className="break-all text-fg-muted select-all">{problem.correlationId}</dd>
            </div>
          )}
          {problem.traceId !== undefined && (
            <div className="flex gap-2">
              <dt className="w-12 shrink-0">trace</dt>
              <dd className="break-all text-fg-muted select-all">{problem.traceId}</dd>
            </div>
          )}
        </dl>
      )}
    </div>
  );
}
