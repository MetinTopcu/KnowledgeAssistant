import { AlertTriangle, CheckCircle2, Info, XOctagon } from 'lucide-react';
import { type LucideIcon } from 'lucide-react';
import { type ReactNode } from 'react';

import { cn } from '@/shared/lib/cn';

type AlertVariant = 'info' | 'success' | 'warning' | 'danger';

const ICONS: Record<AlertVariant, LucideIcon> = {
  info: Info,
  success: CheckCircle2,
  warning: AlertTriangle,
  danger: XOctagon,
};

/** Tint plus a 1px border. No shadow, no rounded blob behind the icon (§8.6). */
const VARIANTS: Record<AlertVariant, string> = {
  info: 'border-border-default bg-canvas-subtle text-fg-muted',
  success: 'border-success-fg/30 bg-success-subtle text-success-fg',
  warning: 'border-attention-fg/30 bg-attention-subtle text-attention-fg',
  danger: 'border-danger-fg/30 bg-danger-subtle text-danger-fg',
};

interface AlertProps {
  readonly variant: AlertVariant;
  readonly title: string;
  readonly children?: ReactNode;
}

export function Alert({ variant, title, children }: AlertProps) {
  const Icon = ICONS[variant];

  return (
    <div className={cn('flex gap-2 rounded-sm border p-3', VARIANTS[variant])}>
      <Icon size={16} strokeWidth={1.5} className="mt-0.5 shrink-0" aria-hidden />
      <div className="flex min-w-0 flex-col gap-1">
        <p className="text-h3">{title}</p>
        {children !== undefined && <div className="text-ui text-fg-muted">{children}</div>}
      </div>
    </div>
  );
}
