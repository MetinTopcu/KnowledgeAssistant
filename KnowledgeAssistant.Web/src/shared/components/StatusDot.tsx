import { cn } from '@/shared/lib/cn';

export type StatusTone = 'success' | 'attention' | 'danger' | 'neutral';

const TONES: Record<StatusTone, string> = {
  success: 'bg-success-fg',
  attention: 'bg-attention-fg',
  danger: 'bg-danger-fg',
  neutral: 'bg-fg-subtle',
};

interface StatusDotProps {
  readonly tone: StatusTone;
  /** Always rendered. Status is never conveyed by colour alone (§14). */
  readonly label: string;
}

/**
 * A status indicator: an 8px dot with its label beside it.
 *
 * The dot is the only place in the system permitted a full radius (§9.5), and
 * the label is not optional — a colour-blind or monochrome reader gets exactly
 * the same information as everyone else.
 */
export function StatusDot({ tone, label }: StatusDotProps) {
  return (
    <span className="flex items-center gap-2">
      <span className={cn('size-2 shrink-0 rounded-full', TONES[tone])} aria-hidden />
      <span className="text-ui">{label}</span>
    </span>
  );
}
