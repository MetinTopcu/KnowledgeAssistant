/** A run of plain answer text. */
interface TextSegment {
  readonly kind: 'text';
  readonly value: string;
}

/** A `[n]` marker the model wrote, which may or may not resolve to a citation. */
interface CitationSegment {
  readonly kind: 'citation';
  readonly referenceNumber: number;
}

export type AnswerSegment = TextSegment | CitationSegment;

/** Matches the numbered markers the grounded prompt asks the model to emit. */
const CITATION_PATTERN = /\[(\d+)\]/g;

/**
 * Splits an answer into text and citation markers so the markers can be made
 * interactive.
 *
 * This is presentation only. It decides where to draw a link, never whether a
 * source was "used": the server deliberately does not report that, on the
 * grounds that a used-or-not flag inferred from prose is right most of the time,
 * and a flag that is right most of the time is worse than no flag in an audit
 * trail. Splitting on the marker is exact; concluding anything from its absence
 * would not be.
 */
export function splitAnswerIntoSegments(answer: string): readonly AnswerSegment[] {
  const segments: AnswerSegment[] = [];
  let lastIndex = 0;

  for (const match of answer.matchAll(CITATION_PATTERN)) {
    const start = match.index;
    const [marker, digits] = match;

    if (start > lastIndex) {
      segments.push({ kind: 'text', value: answer.slice(lastIndex, start) });
    }

    segments.push({ kind: 'citation', referenceNumber: Number(digits) });
    lastIndex = start + marker.length;
  }

  if (lastIndex < answer.length) {
    segments.push({ kind: 'text', value: answer.slice(lastIndex) });
  }

  return segments;
}
