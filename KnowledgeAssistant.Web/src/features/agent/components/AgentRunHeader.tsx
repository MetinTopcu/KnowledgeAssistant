import { type AgentRun } from '@/features/agent/model/agentRun';
import { CopyButton } from '@/shared/components/CopyButton';
import { TokenUsageMeter } from '@/shared/components/TokenUsageMeter';
import { VerticalDivider } from '@/shared/components/VerticalDivider';
import { cn } from '@/shared/lib/cn';

interface AgentRunHeaderProps {
  readonly run: AgentRun;
}

/**
 * The metrics strip for an agent run.
 *
 * `searchCount` leads, because it is the field a reader should look at first
 * and it is amber at zero — the one number that separates an answer grounded in
 * the corpus from one that merely sounds like it.
 *
 * The passage count is `citations.length`, labelled as distinct passages. The
 * agent response has no `retrievedChunkCount`, and this is not a stand-in for
 * one: duplicates are collapsed server-side, so it is the number of distinct
 * passages the agent saw, which is a different quantity and is described as
 * such.
 */
export function AgentRunHeader({ run }: AgentRunHeaderProps) {
  const { response } = run;
  const answeredAt = new Date(response.answeredAtUtc);
  const ungrounded = response.searchCount === 0;

  return (
    <div className="flex flex-wrap items-center gap-x-3 gap-y-2 border-y py-2">
      <span className="eyebrow text-fg-default">Agent</span>
      <VerticalDivider />

      <span
        className={cn(
          'font-mono text-mono-ui tabular-nums',
          ungrounded ? 'text-attention-fg' : 'text-fg-muted',
        )}
        title={
          ungrounded
            ? 'The agent did not consult the corpus.'
            : 'How many times the agent chose to search.'
        }
      >
        {response.searchCount} {response.searchCount === 1 ? 'search' : 'searches'}
      </span>
      <VerticalDivider />

      <span
        className="font-mono text-mono-ui text-fg-muted tabular-nums"
        title="Distinct passages the agent retrieved. Duplicates across searches are collapsed."
      >
        {response.citations.length} passages
      </span>
      <VerticalDivider />

      <TokenUsageMeter usage={response.tokenUsage} />
      <VerticalDivider />

      <span
        className="font-mono text-mono-ui text-fg-muted tabular-nums"
        title="Round trip measured in the browser, including network. Not server processing time."
      >
        {(run.elapsedMs / 1000).toFixed(1)}s round trip
      </span>
      <VerticalDivider />

      <time
        dateTime={response.answeredAtUtc}
        className="font-mono text-mono-ui text-fg-subtle"
        title={answeredAt.toUTCString()}
      >
        {answeredAt.toLocaleTimeString()}
      </time>

      {run.correlationId !== undefined && (
        <>
          <VerticalDivider />
          <span className="font-mono text-mono-ui text-fg-subtle">
            cid <span className="text-fg-muted select-all">{run.correlationId}</span>
          </span>
        </>
      )}

      <span className="ml-auto">
        <CopyButton value={response.answer} label="Copy answer" />
      </span>
    </div>
  );
}
