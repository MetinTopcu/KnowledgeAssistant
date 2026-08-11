import { ArrowDown, ArrowUp } from 'lucide-react';
import { useMemo, useState } from 'react';

import { EvidenceRow } from '@/shared/components/EvidenceRow';
import { type EvidencePassage } from '@/shared/components/evidencePassage';

type SortKey = 'referenceNumber' | 'score';
type SortDirection = 'asc' | 'desc';

interface EvidenceTableProps {
  readonly citations: readonly EvidencePassage[];
  readonly expanded: ReadonlySet<number>;
  readonly onToggle: (referenceNumber: number) => void;
  readonly revealReference: number | null;
  readonly revealNonce: number;
  /** Heading for the reference column. */
  readonly referenceHeading?: string;
  /** What that number means in this mode, for the table's caption. */
  readonly caption?: string;
}

/**
 * The retrieved passages, as a data grid.
 *
 * A table rather than cards, because this is evidence to be scanned, compared
 * and sorted — a reader checking an answer wants the scores in a column, not
 * scattered across tiles.
 *
 * **Every retrieved passage appears, including ones the answer never mentions.**
 * They are not dimmed or marked "not cited": the server declines to report
 * which sources were used, because inferring it from prose is right most of the
 * time, and a flag that is right most of the time is worse than no flag in an
 * audit trail. Presence in this table means "the model was shown this", which
 * is exactly what the response guarantees.
 */
export function EvidenceTable({
  citations,
  expanded,
  onToggle,
  revealReference,
  revealNonce,
  referenceHeading = '#',
  caption = 'Passages retrieved for this question, with their relevance scores',
}: EvidenceTableProps) {
  const [sortKey, setSortKey] = useState<SortKey>('referenceNumber');
  const [direction, setDirection] = useState<SortDirection>('asc');

  const sorted = useMemo(() => {
    const factor = direction === 'asc' ? 1 : -1;
    return [...citations].sort((left, right) => (left[sortKey] - right[sortKey]) * factor);
  }, [citations, sortKey, direction]);

  const toggleSort = (key: SortKey) => {
    if (key === sortKey) {
      setDirection((current) => (current === 'asc' ? 'desc' : 'asc'));
      return;
    }

    setSortKey(key);
    setDirection(key === 'score' ? 'desc' : 'asc');
  };

  const SortIcon = direction === 'asc' ? ArrowUp : ArrowDown;

  const renderSortableHeader = (key: SortKey, label: string) => (
    <button
      type="button"
      onClick={() => {
        toggleSort(key);
      }}
      className="flex items-center gap-1 text-fg-subtle hover:text-fg-default"
    >
      <span className="eyebrow">{label}</span>
      {sortKey === key && <SortIcon size={12} strokeWidth={1.5} aria-hidden />}
    </button>
  );

  return (
    <div className="overflow-x-auto rounded-sm border">
      <table className="w-full table-fixed border-collapse">
        <caption className="sr-only">{caption}</caption>
        <thead className="bg-canvas-subtle">
          <tr className="border-b">
            <th scope="col" className="w-8" />
            <th scope="col" className="w-16 px-3 text-left">
              {renderSortableHeader('referenceNumber', referenceHeading)}
            </th>
            <th scope="col" className="w-40 px-3 text-left">
              {renderSortableHeader('score', 'Score')}
            </th>
            <th scope="col" className="w-28 px-3 text-left text-fg-subtle">
              <span className="eyebrow">Document</span>
            </th>
            <th scope="col" className="w-16 px-3 text-left text-fg-subtle">
              <span className="eyebrow">Chunk</span>
            </th>
            <th scope="col" className="px-3 text-left text-fg-subtle">
              <span className="eyebrow">Passage</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {sorted.map((citation) => (
            <EvidenceRow
              key={citation.chunkId}
              citation={citation}
              isExpanded={expanded.has(citation.referenceNumber)}
              onToggle={onToggle}
              isRevealTarget={revealReference === citation.referenceNumber}
              revealNonce={revealNonce}
            />
          ))}
        </tbody>
      </table>
    </div>
  );
}
