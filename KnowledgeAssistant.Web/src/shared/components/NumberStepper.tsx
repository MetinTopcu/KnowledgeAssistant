import { Minus, Plus } from 'lucide-react';

import { Button } from '@/shared/components/Button';

interface NumberStepperProps {
  readonly label: string;
  readonly value: number;
  readonly min: number;
  readonly max: number;
  readonly onChange: (value: number) => void;
  readonly disabled?: boolean;
}

/**
 * A discrete integer control (§8.3).
 *
 * A stepper rather than a slider: a slider implies a continuum, and this is a
 * count of passages where every value is meaningfully different from its
 * neighbour. The value is rendered in the mono face so it does not shift width
 * as it changes.
 */
export function NumberStepper({
  label,
  value,
  min,
  max,
  onChange,
  disabled = false,
}: NumberStepperProps) {
  const clamp = (next: number) => Math.min(Math.max(next, min), max);

  return (
    <div className="flex items-center gap-2">
      <span className="text-ui text-fg-muted">{label}</span>
      <div className="flex h-control-sm items-center rounded-sm border">
        <Button
          variant="subtle"
          size="icon"
          disabled={disabled || value <= min}
          onClick={() => {
            onChange(clamp(value - 1));
          }}
          aria-label={`Decrease ${label}`}
        >
          <Minus size={14} strokeWidth={1.5} aria-hidden />
        </Button>

        <output
          className="w-6 text-center font-mono text-mono-ui text-fg-default tabular-nums"
          aria-label={`${label}: ${String(value)}`}
        >
          {value}
        </output>

        <Button
          variant="subtle"
          size="icon"
          disabled={disabled || value >= max}
          onClick={() => {
            onChange(clamp(value + 1));
          }}
          aria-label={`Increase ${label}`}
        >
          <Plus size={14} strokeWidth={1.5} aria-hidden />
        </Button>
      </div>
    </div>
  );
}
