interface ProgressBarProps {
  /** Units completed. Omit for an indeterminate bar. */
  readonly value?: number;
  /** Total units. Omit for an indeterminate bar. */
  readonly max?: number;
  readonly label: string;
}

/**
 * A 2px progress bar, determinate or indeterminate.
 *
 * The determinate case is a native `<progress>` rather than a styled div. A
 * div's fill has to be a width, a width has to come from a measurement, and a
 * measurement can only reach the DOM through an inline style — which this
 * codebase forbids. `<progress>` takes the measurement as an *attribute*, so
 * the value stays exact to the byte instead of being quantised to whatever
 * utility classes happen to exist, and the element carries its own semantics.
 *
 * Indeterminate is a div, because native indeterminate `<progress>` styling
 * differs enough between engines to be unusable — and it needs no dynamic
 * value, so the same objection does not apply.
 */
export function ProgressBar({ value, max, label }: ProgressBarProps) {
  if (value === undefined || max === undefined || max <= 0) {
    return (
      <div
        role="progressbar"
        aria-label={label}
        className="relative h-0.5 w-full overflow-hidden rounded-xs bg-canvas-inset"
      >
        <div className="h-full w-1/4 animate-progress-indeterminate bg-accent-emphasis" />
      </div>
    );
  }

  return (
    <progress
      value={value}
      max={max}
      aria-label={label}
      className="h-0.5 w-full appearance-none overflow-hidden rounded-xs bg-canvas-inset [&::-moz-progress-bar]:bg-accent-emphasis [&::-webkit-progress-bar]:bg-canvas-inset [&::-webkit-progress-value]:bg-accent-emphasis"
    />
  );
}
