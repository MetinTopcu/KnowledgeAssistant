import { CitationMark } from '@/shared/components/CitationMark';
import { splitAnswerIntoSegments } from '@/shared/lib/answerSegments';
import { cn } from '@/shared/lib/cn';

interface AnswerBodyProps {
  readonly answer: string;
  readonly resolvableReferences: ReadonlySet<number>;
  readonly onSelectCitation: (referenceNumber: number) => void;
  /**
   * `muted` demotes the prose. Used when the answer is not grounded in the
   * corpus: typography should not lend it authority the product cannot vouch
   * for.
   */
  readonly tone?: 'default' | 'muted';
}

/**
 * The generated answer, with its citation markers made interactive.
 *
 * Rendered as text, not Markdown. docs/DESIGN.md §6.1 calls for Markdown, and
 * it is deliberately not done here: rendering model output as markup means
 * accepting model-authored HTML, which needs a sanitiser chosen and reviewed on
 * its own terms rather than added as a detail of this slice. Whitespace is
 * preserved so lists and paragraphs the model wrote still read correctly.
 */
export function AnswerBody({
  answer,
  resolvableReferences,
  onSelectCitation,
  tone = 'default',
}: AnswerBodyProps) {
  const segments = splitAnswerIntoSegments(answer);

  return (
    <div
      className={cn(
        'max-w-answer text-answer whitespace-pre-wrap',
        tone === 'muted' ? 'text-fg-muted' : 'text-fg-default',
      )}
    >
      {segments.map((segment, index) =>
        // Segments have no identity of their own: the index *is* the position in
        // the answer, and the whole list is regenerated whenever the answer
        // changes, so there is nothing for React to preserve across edits.
        segment.kind === 'text' ? (
          <span key={`text-${String(index)}`}>{segment.value}</span>
        ) : (
          <CitationMark
            key={`citation-${String(index)}`}
            referenceNumber={segment.referenceNumber}
            resolves={resolvableReferences.has(segment.referenceNumber)}
            onSelect={onSelectCitation}
          />
        ),
      )}
    </div>
  );
}
