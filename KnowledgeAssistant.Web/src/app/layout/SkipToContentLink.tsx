interface SkipToContentLinkProps {
  /** The id of the element focus should jump to. */
  readonly targetId: string;
}

/**
 * The first thing in the tab order, hidden until it has focus.
 *
 * Without it, every keyboard user pays the cost of tabbing through the whole
 * navigation rail on every screen before reaching what they came for.
 */
export function SkipToContentLink({ targetId }: SkipToContentLinkProps) {
  return (
    <a
      href={`#${targetId}`}
      className="sr-only z-dialog rounded-sm border bg-canvas-overlay px-3 py-2 text-ui text-fg-default shadow-elevation-2 focus-visible:not-sr-only focus-visible:fixed focus-visible:top-3 focus-visible:left-3"
    >
      Skip to content
    </a>
  );
}
