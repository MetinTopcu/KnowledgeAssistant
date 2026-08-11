interface CitationMarkProps {
  readonly referenceNumber: number;
  /** False when the answer cites a number the response has no citation for. */
  readonly resolves: boolean;
  readonly onSelect: (referenceNumber: number) => void;
}

/**
 * An inline `[n]` marker, made navigable.
 *
 * A button rather than styled text, so the keyboard reaches it in reading order
 * and its accessible name says where it goes.
 *
 * A marker that resolves to no citation is shown plainly rather than as a link
 * and never silently dropped: the model wrote it, and hiding the mismatch would
 * conceal exactly the kind of ungrounded reference a reader is checking for.
 */
export function CitationMark({ referenceNumber, resolves, onSelect }: CitationMarkProps) {
  if (!resolves) {
    return (
      <span
        className="font-mono text-mono-chip text-attention-fg"
        title="The answer cites this number, but no such source was returned."
      >
        [{referenceNumber}]
      </span>
    );
  }

  return (
    <button
      type="button"
      onClick={() => {
        onSelect(referenceNumber);
      }}
      aria-label={`Show source ${String(referenceNumber)}`}
      className="mx-px rounded-xs border bg-canvas-inset px-1 align-baseline font-mono text-mono-chip text-accent-fg transition-colors duration-instant ease-standard hover:border-accent-fg"
    >
      [{referenceNumber}]
    </button>
  );
}
