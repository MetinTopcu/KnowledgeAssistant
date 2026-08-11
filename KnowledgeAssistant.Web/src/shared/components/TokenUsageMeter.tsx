import { type TokenUsage } from '@/shared/api/tokenUsage';

interface TokenUsageMeterProps {
  /** Null when the provider reported nothing, or no completion was needed. */
  readonly usage: TokenUsage | null;
}

/**
 * What the completion cost, exactly as reported.
 *
 * When the server sends null this says so rather than showing zero. A
 * zero-retrieval answer never calls the model, and a provider can decline to
 * report usage at all — both are "unknown cost", which is a different fact from
 * "no cost" and must not be rendered as one.
 */
export function TokenUsageMeter({ usage }: TokenUsageMeterProps) {
  if (usage === null) {
    return <span className="font-mono text-mono-ui text-fg-subtle">tokens not reported</span>;
  }

  return (
    <span
      className="font-mono text-mono-ui text-fg-muted tabular-nums"
      title={`${String(usage.promptTokens)} prompt + ${String(usage.completionTokens)} completion`}
    >
      {usage.totalTokens.toLocaleString()} tok
    </span>
  );
}
