import { cn } from '@/shared/lib/cn';

const SEGMENTS = 5;

interface ScoreBarProps {
  readonly score: number;
}

/**
 * Relevance as five monochrome segments beside the exact figure.
 *
 * Monochrome because a score is a magnitude, not a category — colouring it
 * would claim a threshold between "good" and "bad" that the search service does
 * not define.
 *
 * The bar assumes the 0–1 range Azure AI Search returns for vector search and
 * saturates outside it. The numeric value beside it is the authority and is
 * printed unrounded to four places; the bar is only there to make a column of
 * scores comparable at a glance.
 */
export function ScoreBar({ score }: ScoreBarProps) {
  const clamped = Math.min(Math.max(score, 0), 1);
  const filled = Math.round(clamped * SEGMENTS);

  return (
    <span className="flex items-center gap-2">
      <span className="flex gap-px" aria-hidden>
        {Array.from({ length: SEGMENTS }, (_, index) => (
          <span
            key={index}
            className={cn('h-3 w-1 rounded-xs', index < filled ? 'bg-fg-muted' : 'bg-canvas-inset')}
          />
        ))}
      </span>
      <span className="font-mono text-mono-ui text-fg-muted tabular-nums">{score.toFixed(4)}</span>
    </span>
  );
}
