import { type ReactNode } from 'react';

interface DefinitionListProps {
  readonly children: ReactNode;
  /** Describes the block for assistive technology; never rendered visually. */
  readonly label: string;
}

/**
 * Label/value pairs for a metadata block, per docs/DESIGN.md §8.4.
 *
 * A real `<dl>` rather than a two-column grid of `<span>`s: the pairing between
 * a label and its value is the content here, and a screen reader that cannot
 * hear the pairing is left with a list of unattributed strings.
 */
export function DefinitionList({ children, label }: DefinitionListProps) {
  return (
    <dl aria-label={label} className="flex flex-col rounded-sm border">
      {children}
    </dl>
  );
}

interface DefinitionRowProps {
  readonly term: string;
  readonly children: ReactNode;
  /** Optional sentence under the value, for what the value implies. */
  readonly note?: ReactNode;
}

/**
 * One pair. The label is an 11px eyebrow (§9.3) in a fixed column so that the
 * values line up down the block rather than starting at a different x for every
 * row.
 */
export function DefinitionRow({ term, children, note }: DefinitionRowProps) {
  return (
    <div className="flex flex-col gap-1 border-b border-b-border-muted p-3 last:border-b-0 md:flex-row md:gap-4">
      <dt className="shrink-0 eyebrow text-fg-subtle md:w-44 md:pt-1">{term}</dt>
      <dd className="flex min-w-0 flex-1 flex-col gap-1">
        {children}
        {note !== undefined && <p className="text-caption text-fg-subtle">{note}</p>}
      </dd>
    </div>
  );
}
