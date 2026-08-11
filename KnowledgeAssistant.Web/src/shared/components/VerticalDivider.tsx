/**
 * A 1px rule between inline items in a metadata strip.
 *
 * Decorative, so it is hidden from assistive technology — a screen reader
 * announcing "separator" between every figure would bury the figures.
 */
export function VerticalDivider() {
  return <span className="h-3 w-px shrink-0 bg-border-default" aria-hidden />;
}
