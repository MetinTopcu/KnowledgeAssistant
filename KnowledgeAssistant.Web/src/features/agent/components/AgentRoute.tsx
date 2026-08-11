import { Bot } from 'lucide-react';
import { useEffect, useMemo, useRef, useState } from 'react';

import { AgentComposer } from '@/features/agent/components/AgentComposer';
import { AgentRunHeader } from '@/features/agent/components/AgentRunHeader';
import { UngroundedBanner } from '@/features/agent/components/UngroundedBanner';
import { type AgentRun } from '@/features/agent/model/agentRun';
import { type AskAgentFormValues } from '@/features/agent/model/askAgentForm';
import { useAskAgent } from '@/features/agent/model/useAskAgent';
import { Alert } from '@/shared/components/Alert';
import { AnswerBody } from '@/shared/components/AnswerBody';
import { EmptyState } from '@/shared/components/EmptyState';
import { EvidenceTable } from '@/shared/components/EvidenceTable';
import { ProblemDetailsView } from '@/shared/components/ProblemDetailsView';
import { RunStatusLine } from '@/shared/components/RunStatusLine';

/**
 * The Agent console.
 *
 * A sibling of Ask rather than a mode inside it, because the two endpoints make
 * different promises about latency and cost — and because the agent's output
 * needs a reading its numbers do not share: the passage numbers here are the
 * order the agent chose to look things up, and a run with zero searches is a
 * fully formed answer that must be read differently from a grounded one.
 *
 * Not a chat. One question produces one record, exactly as in retrieval: the
 * endpoint retains no conversation between calls, so there is no thread to
 * render and nothing to scroll back through.
 */
export function AgentRoute() {
  const mutation = useAskAgent();

  const [run, setRun] = useState<AgentRun | null>(null);
  const [expanded, setExpanded] = useState<ReadonlySet<number>>(new Set());
  const [startedAt, setStartedAt] = useState<number | null>(null);
  const [reveal, setReveal] = useState<{ reference: number; nonce: number } | null>(null);

  const answerRef = useRef<HTMLDivElement>(null);

  const resolvableReferences = useMemo(
    () => new Set(run?.response.citations.map((citation) => citation.referenceNumber) ?? []),
    [run],
  );

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

  const handleSubmit = (values: AskAgentFormValues) => {
    setStartedAt(performance.now());

    mutation.mutate(values, {
      onSuccess: (nextRun) => {
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

  const ungrounded = run !== null && run.response.searchCount === 0;

  return (
    <div className="flex flex-col gap-6 p-6">
      <AgentComposer onSubmit={handleSubmit} isRunning={mutation.isPending} />

      {mutation.isPending && startedAt !== null && (
        <RunStatusLine
          startedAt={startedAt}
          label="Running the agent"
          // No stage list and no live search counter: the request reports
          // nothing until it returns, so a counter here could only be invented.
          // The real figure appears in the run header once it arrives.
          message="The agent is deciding what to search for"
        />
      )}

      {mutation.isError && (
        <Alert variant="danger" title="The agent run failed">
          <ProblemDetailsView problem={mutation.error.problem} />
        </Alert>
      )}

      {run === null && !mutation.isPending && !mutation.isError && (
        <EmptyState
          icon={Bot}
          title="Ask the agent"
          description="The agent decides whether, how often, and with what wording to search. It answers questions retrieval cannot, and costs what it costs."
        />
      )}

      {run !== null && (
        <div ref={answerRef} className="flex flex-col gap-4" aria-busy={mutation.isPending}>
          <AgentRunHeader run={run} />

          {ungrounded && <UngroundedBanner />}

          <AnswerBody
            answer={run.response.answer}
            resolvableReferences={resolvableReferences}
            onSelectCitation={selectCitation}
            tone={ungrounded ? 'muted' : 'default'}
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
                referenceHeading="Seen"
                caption="Passages the agent retrieved, numbered in the order it first saw them. Duplicates across searches are collapsed."
              />
            </section>
          )}
        </div>
      )}
    </div>
  );
}
