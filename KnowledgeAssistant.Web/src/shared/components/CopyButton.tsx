import { Check, Copy } from 'lucide-react';

import { Button } from '@/shared/components/Button';
import { useCopyToClipboard } from '@/shared/hooks/useCopyToClipboard';

interface CopyButtonProps {
  readonly value: string;
  readonly label: string;
}

/**
 * Copies a value, confirming with a check for a moment.
 *
 * The label does not change with the state — only the icon does. Swapping the
 * text to "Copied" would resize the control mid-interaction and shift whatever
 * sits beside it; the confirmation is announced to assistive technology
 * instead, where it costs no layout.
 */
export function CopyButton({ value, label }: CopyButtonProps) {
  const { copied, copy } = useCopyToClipboard();
  const Icon = copied ? Check : Copy;

  return (
    <>
      <Button
        variant="subtle"
        size="sm"
        onClick={() => {
          void copy(value);
        }}
        title={label}
      >
        <Icon
          size={14}
          strokeWidth={1.5}
          className={copied ? 'text-success-fg' : undefined}
          aria-hidden
        />
        <span>{label}</span>
      </Button>
      <span role="status" aria-live="polite" className="sr-only">
        {copied ? `${label}: copied to clipboard` : ''}
      </span>
    </>
  );
}
