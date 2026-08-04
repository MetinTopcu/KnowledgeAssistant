interface KeyHintProps {
  /** One entry per key. A chord is `['g', 'd']`, pressed in sequence. */
  readonly keys: readonly string[];
}

/**
 * Renders a key or chord as caps, per docs/DESIGN.md §8.4.
 *
 * Chords are spaced rather than joined with a `+`, because `g` then `d` is a
 * sequence and `Ctrl+B` is a combination — writing both the same way would
 * teach the wrong gesture.
 */
export function KeyHint({ keys }: KeyHintProps) {
  return (
    <span className="inline-flex items-center gap-1 text-fg-subtle">
      {keys.map((key) => (
        <kbd
          key={key}
          className="inline-flex min-w-5 items-center justify-center rounded-xs border bg-canvas-inset px-1 py-0.5 text-mono-chip text-fg-muted"
        >
          {key}
        </kbd>
      ))}
    </span>
  );
}
