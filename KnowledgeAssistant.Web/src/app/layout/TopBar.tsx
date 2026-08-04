import { type ReactNode } from 'react';

interface TopBarProps {
  readonly title: string;
  /** Contextual actions for the current screen, right-aligned. */
  readonly actions?: ReactNode;
}

export function TopBar({ title, actions }: TopBarProps) {
  return (
    <header className="flex h-topbar shrink-0 items-center justify-between gap-4 border-b px-6">
      <h1 className="truncate text-h2">{title}</h1>
      {actions !== undefined && <div className="flex shrink-0 items-center gap-2">{actions}</div>}
    </header>
  );
}
