import { MessageSquareText } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';

import { AnswerBody } from '@/shared/components/AnswerBody';
import { EvidenceTable } from '@/shared/components/EvidenceTable';
import { NoRetrievalNotice } from '@/features/ask/components/NoRetrievalNotice';
import { QuestionComposer } from '@/features/ask/components/QuestionComposer';
import { RunHeader } from '@/features/ask/components/RunHeader';
import { RunStatusLine } from '@/shared/components/RunStatusLine';
import { type AskQuestionFormValues } from '@/features/ask/model/askQuestionForm';
import { type AskRun } from '@/features/ask/model/askRun';
import { useAskQuestion } from '@/features/ask/model/useAskQuestion';
import { Alert } from '@/shared/components/Alert';
import { EmptyState } from '@/shared/components/EmptyState';
import { ProblemDetailsView } from '@/shared/components/ProblemDetailsView';

/**
 * The Ask console.
 *
 * Composer at the top, then the run: its metrics, its answer, and the evidence
 * it was built from. One question produces one record — there is no thread,
 * because the endpoint retains no conversation between calls.
 */
export function AskRoute() {
  const mutation = useAskQuestion();

  /**
   * The last answer that arrived, held here rather than read from the mutation.
   *
   * `mutate` clears `data` the moment it is called, which would blank the screen
   * for the length of the request. Keeping the previous run in state means the
   * old answer stays readable — and stays checkable against its evidence —
   * until the new one is actually ready to replace it.
   */
  const [run, setRun] = useState<AskRun | null>(null);

  const [expanded, setExpanded] = useState<ReadonlySet<number>>(new Set());
  const [startedAt, setStartedAt] = useState<number | null>(null);

  /**
   * Which evidence row a citation marker pointed at, and how many times one has
   * been clicked. The nonce is state rather than a ref because it is read during
   * render and drives the scroll effect — clicking the same marker twice must
   * scroll twice, which an unchanged reference alone would not do.
   */
  const [reveal, setReveal] = useState<{ reference: number; nonce: number } | null>(null);

  const answerRef = useRef<HTMLDivElement>(null);

  const resolvableReferences = useMemo(
    () => new Set(run?.response.citations.map((citation) => citation.referenceNumber) ?? []),
    [run],
  );

  // Bring the new answer into view once it has rendered. Honours the same
  // reduced-motion preference as the rest of the system: the scroll still
  // happens, it just stops being animated.
  useEffect(() => {
    if (run === null) {
      return;
    }

    const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    answerRef.current?.scrollIntoView({
      behavior: prefersReducedMotion ? 'auto' : 'smooth',
      block: 'start',
    });
  }, [run]);

  const handleSubmit = (values: AskQuestionFormValues) => {
    setStartedAt(performance.now());

    mutation.mutate(values, {
      onSuccess: (nextRun) => {
        // Expansion and reveal are reset here rather than on submit, because
        // they belong to the run being displayed — and until this moment the
        // run on screen is still the previous one.
        setExpanded(new Set());
        setReveal(null);
        setRun(nextRun);
      },
    });
  };

  const toggleExpanded = (referenceNumber: number) => {
    setExpanded((current) => {
      const next = new Set(current);

      if (next.has(referenceNumber)) {
        next.delete(referenceNumber);
      } else {
        next.add(referenceNumber);
      }

      return next;
    });
  };

  const selectCitation = (referenceNumber: number) => {
    setReveal((current) => ({ reference: referenceNumber, nonce: (current?.nonce ?? 0) + 1 }));
    setExpanded((current) => new Set(current).add(referenceNumber));
  };

  return (
    <div className="flex flex-col gap-6 p-6">
      <QuestionComposer onSubmit={handleSubmit} isRunning={mutation.isPending} />

      {mutation.isPending && startedAt !== null && (
        <RunStatusLine
          startedAt={startedAt}
          label="Answering"
          message="Retrieving passages and generating an answer"
        />
      )}

      {mutation.isError && (
        <Alert variant="danger" title="The question could not be answered">
          <ProblemDetailsView problem={mutation.error.problem} />
        </Alert>
      )}

      {run === null && !mutation.isPending && !mutation.isError && (
        <EmptyState
          icon={MessageSquareText}
          title="Ask a question"
          description="Retrieval runs one search and one grounded completion, then shows you every passage it was given."
        />
      )}

      {run !== null && (
        <div ref={answerRef} className="flex flex-col gap-4" aria-busy={mutation.isPending}>
          <RunHeader run={run} />

          {run.response.retrievedChunkCount === 0 && <NoRetrievalNotice />}

          <AnswerBody
            answer={run.response.answer}
            resolvableReferences={resolvableReferences}
            onSelectCitation={selectCitation}
          />

          {run.response.citations.length > 0 && (
            <section className="flex flex-col gap-2">
              <h2 className="eyebrow text-fg-subtle">
                Evidence · {run.response.citations.length} passages
              </h2>
              <EvidenceTable
                citations={run.response.citations}
                expanded={expanded}
                onToggle={toggleExpanded}
                revealReference={reveal?.reference ?? null}
                revealNonce={reveal?.nonce ?? 0}
              />
            </section>
          )}
        </div>
      )}
    </div>
  );
}
