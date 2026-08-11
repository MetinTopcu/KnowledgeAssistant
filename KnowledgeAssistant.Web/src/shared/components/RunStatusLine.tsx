import { ProgressBar } from '@/shared/components/ProgressBar';
import { useElapsedSeconds } from '@/shared/hooks/useElapsedSeconds';

interface RunStatusLineProps {
  readonly startedAt: number;
  /** What is happening, in the caller's own words. */
  readonly message: string;
  /** Accessible name for the progress bar. */
  readonly label: string;
}

/**
 * What is shown while a question is in flight.
 *
 * docs/DESIGN.md §12.1 sketches a staged narrative — embedding, searching,
 * generating — with a duration against each. That is not built, because the
 * endpoint is a single request that reports nothing until it returns: those
 * stage transitions could only be driven by a timer, which is a picture of
 * progress rather than a report of one. An indeterminate bar and a real elapsed
 * clock say exactly as much as is actually known.
 */
export function RunStatusLine({ startedAt, message, label }: RunStatusLineProps) {
  const elapsed = useElapsedSeconds(startedAt, true);

  return (
    <div className="flex flex-col gap-2" aria-live="polite">
      <ProgressBar label={label} />
      <p className="font-mono text-mono-ui text-fg-muted tabular-nums">
        {message} · {elapsed.toFixed(1)}s
      </p>
    </div>
  );
}
