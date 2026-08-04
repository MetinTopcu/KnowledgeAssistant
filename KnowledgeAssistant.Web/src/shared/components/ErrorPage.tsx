import { type LucideIcon } from 'lucide-react';
import { type ReactNode } from 'react';

import { cn } from '@/shared/lib/cn';

interface ErrorPageProps {
  readonly icon: LucideIcon;
  readonly title: string;
  readonly description: string;
  /**
   * The stable machine-readable code, shown verbatim. Never paraphrased: it is
   * what a user quotes in a ticket and what an operator greps the logs for.
   */
  readonly code?: string;
  readonly correlationId?: string | undefined;
  readonly traceId?: string | undefined;
  readonly actions?: ReactNode;
  readonly tone?: 'danger' | 'neutral';
}

/**
 * The page tier of docs/DESIGN.md §13.1: the route cannot render at all.
 *
 * Centred in a 480px block, no illustration, and the identifiers presented as
 * selectable monospace text rather than buried in a details pane.
 */
export function ErrorPage({
  icon: Icon,
  title,
  description,
  code,
  correlationId,
  traceId,
  actions,
  tone = 'danger',
}: ErrorPageProps) {
  return (
    <div className="flex h-full items-center justify-center p-16">
      <div className="flex max-w-error flex-col items-start gap-4">
        <Icon
          size={24}
          strokeWidth={1.5}
          className={cn(tone === 'danger' ? 'text-danger-fg' : 'text-fg-subtle')}
          aria-hidden
        />

        <div className="flex flex-col gap-2">
          <h2 className="text-h1">{title}</h2>
          <p className="text-body text-fg-muted">{description}</p>
        </div>

        {code !== undefined && (
          <code className="rounded-xs border bg-canvas-inset px-1.5 py-0.5 text-mono-chip text-fg-default">
            {code}
          </code>
        )}

        {(correlationId !== undefined || traceId !== undefined) && (
          <dl className="flex flex-col gap-1 text-mono-ui text-fg-subtle">
            {correlationId !== undefined && (
              <div className="flex gap-2">
                <dt className="w-16 shrink-0">cid</dt>
                <dd className="break-all text-fg-muted select-all">{correlationId}</dd>
              </div>
            )}
            {traceId !== undefined && (
              <div className="flex gap-2">
                <dt className="w-16 shrink-0">trace</dt>
                <dd className="break-all text-fg-muted select-all">{traceId}</dd>
              </div>
            )}
          </dl>
        )}

        {actions !== undefined && <div className="flex items-center gap-2 pt-2">{actions}</div>}
      </div>
    </div>
  );
}
