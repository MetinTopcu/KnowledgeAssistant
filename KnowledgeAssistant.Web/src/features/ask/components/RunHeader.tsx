import { TokenUsageMeter } from '@/shared/components/TokenUsageMeter';
import { type AskRun } from '@/features/ask/model/askRun';
import { CopyButton } from '@/shared/components/CopyButton';
import { VerticalDivider } from '@/shared/components/VerticalDivider';

interface RunHeaderProps {
  readonly run: AskRun;
}

/**
 * The metrics strip above an answer, in the shape of a CI job header.
 *
 * Every figure here is either reported by the server or measured by this
 * client, and the two are not mixed silently: the elapsed time is labelled as a
 * round trip because that is what the browser can observe. The API reports no
 * server-side duration, and none is inferred.
 *
 * There is no search count. `/api/questions` performs exactly one search by
 * construction but does not report a count, and `searchCount` is a field of the
 * *agent* response. Printing a 1 here would put a client-invented number in a
 * row of server-reported ones, which is the one thing this strip exists to
 * prevent.
 */
export function RunHeader({ run }: RunHeaderProps) {
  const { response } = run;
  const answeredAt = new Date(response.answeredAtUtc);

  return (
    <div className="flex flex-wrap items-center gap-x-3 gap-y-2 border-y py-2">
      <span className="eyebrow text-fg-default">Retrieval</span>
      <VerticalDivider />

      <span className="font-mono text-mono-ui text-fg-muted tabular-nums">
        {response.retrievedChunkCount} retrieved
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

      {/* Run-level actions sit at the end of the strip, where §6.1 puts the
          overflow menu. Copying the answer alone — without the metrics or the
          evidence — is what someone pasting into a ticket actually wants. */}
      <span className="ml-auto">
        <CopyButton value={run.response.answer} label="Copy answer" />
      </span>
    </div>
  );
}
