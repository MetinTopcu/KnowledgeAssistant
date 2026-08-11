import { type LucideIcon } from 'lucide-react';
import { type ReactNode } from 'react';

interface EmptyStateProps {
  readonly icon: LucideIcon;
  readonly title: string;
  readonly description: string;
  readonly action?: ReactNode;
}

/**
 * The one empty-state formula (§11): a 24px muted icon, a title, one sentence
 * capped at a readable measure, and at most one action.
 *
 * No illustration and no larger icon. An empty state is a signpost, not an
 * event.
 */
export function EmptyState({ icon: Icon, title, description, action }: EmptyStateProps) {
  return (
    <div className="flex flex-col items-center gap-3 px-6 py-12 text-center">
      <Icon size={24} strokeWidth={1.5} className="text-fg-subtle" aria-hidden />
      <div className="flex max-w-empty flex-col gap-1">
        <h2 className="text-h3">{title}</h2>
        <p className="text-ui text-fg-muted">{description}</p>
      </div>
      {action !== undefined && <div className="pt-1">{action}</div>}
    </div>
  );
}
