import { ChevronDown, ChevronRight } from 'lucide-react';
import { useEffect, useRef } from 'react';

import { ScoreBar } from '@/shared/components/ScoreBar';
import { type EvidencePassage } from '@/shared/components/evidencePassage';
import { cn } from '@/shared/lib/cn';

interface EvidenceRowProps {
  readonly citation: EvidencePassage;
  readonly isExpanded: boolean;
  readonly onToggle: (referenceNumber: number) => void;
  /** Set when a citation mark in the answer pointed here. */
  readonly isRevealTarget: boolean;
  /** Changes on every reveal, so clicking the same marker twice still scrolls. */
  readonly revealNonce: number;
}

export function EvidenceRow({
  citation,
  isExpanded,
  onToggle,
  isRevealTarget,
  revealNonce,
}: EvidenceRowProps) {
  const rowRef = useRef<HTMLTableRowElement>(null);
  const Chevron = isExpanded ? ChevronDown : ChevronRight;

  useEffect(() => {
    if (isRevealTarget) {
      rowRef.current?.scrollIntoView({ block: 'nearest' });
    }
  }, [isRevealTarget, revealNonce]);

  return (
    <>
      <tr
        ref={rowRef}
        className={cn('border-b border-b-border-muted', isRevealTarget && 'bg-canvas-selected')}
      >
        <td className="p-0">
          <button
            type="button"
            onClick={() => {
              onToggle(citation.referenceNumber);
            }}
            aria-expanded={isExpanded}
            aria-label={`${isExpanded ? 'Collapse' : 'Expand'} source ${String(citation.referenceNumber)}`}
            className="flex h-row-default w-full items-center justify-center text-fg-subtle hover:text-fg-default"
          >
            <Chevron size={14} strokeWidth={1.5} aria-hidden />
          </button>
        </td>

        <td className="px-3 font-mono text-mono-ui text-fg-default tabular-nums">
          {citation.referenceNumber}
        </td>

        <td className="px-3">
          <ScoreBar score={citation.score} />
        </td>

        <td className="px-3">
          {/* The document id, not a file name. The API returns no name, and the
              blob path is yyyy/MM/dd/{guid}.pdf — the original name is not in
              it. Showing a name would mean inventing one. */}
          <span className="font-mono text-mono-ui text-fg-muted" title={citation.documentId}>
            {citation.documentId.slice(0, 8)}…
          </span>
        </td>

        <td className="px-3 font-mono text-mono-ui text-fg-muted tabular-nums">
          {citation.chunkOrder}
        </td>

        <td className="max-w-0 truncate px-3 text-ui text-fg-muted">{citation.text}</td>
      </tr>

      {isExpanded && (
        <tr className="border-b border-b-border-muted">
          <td colSpan={6} className="bg-canvas-subtle p-3">
            <div className="flex flex-col gap-2">
              {/* The complete chunk, never an excerpt: the point of a citation
                  is to be checkable, and an ellipsis is where a misattribution
                  hides. */}
              <p className="max-w-chunk rounded-sm border bg-canvas-inset p-3 font-mono text-mono-body whitespace-pre-wrap text-fg-default">
                {citation.text}
              </p>
              <dl className="flex flex-wrap gap-x-6 gap-y-1 font-mono text-mono-ui text-fg-subtle">
                <div className="flex gap-2">
                  <dt>chunk</dt>
                  <dd className="text-fg-muted select-all">{citation.chunkId}</dd>
                </div>
                <div className="flex gap-2">
                  <dt>document</dt>
                  <dd className="text-fg-muted select-all">{citation.documentId}</dd>
                </div>
              </dl>
            </div>
          </td>
        </tr>
      )}
    </>
  );
}
