import { Keyboard, PanelLeftClose, PanelLeftOpen } from 'lucide-react';
import { Link } from 'react-router';

import { NavRailItem } from '@/app/layout/NavRailItem';
import { ThemeToggle } from '@/app/layout/ThemeToggle';
import { navigationItems } from '@/app/layout/navigationItems';
import { routePaths } from '@/app/router/routePaths';
import { Button } from '@/shared/components/Button';
import { cn } from '@/shared/lib/cn';

interface NavRailProps {
  readonly collapsed: boolean;
  /** False on narrow viewports, where the collapsed state is not the user's to choose. */
  readonly canCollapse: boolean;
  readonly onToggleCollapsed: () => void;
  readonly onShowShortcuts: () => void;
}

export function NavRail({
  collapsed,
  canCollapse,
  onToggleCollapsed,
  onShowShortcuts,
}: NavRailProps) {
  const ToggleIcon = collapsed ? PanelLeftOpen : PanelLeftClose;

  return (
    <nav
      aria-label="Primary"
      className={cn(
        'flex h-full flex-col border-r bg-canvas-subtle',
        'transition-[width] duration-base ease-standard',
        collapsed ? 'w-rail-collapsed' : 'w-rail',
      )}
    >
      <div
        className={cn(
          'flex h-topbar shrink-0 items-center border-b',
          collapsed ? 'justify-center px-1' : 'gap-2 px-3',
        )}
      >
        <Link
          to={routePaths.ask}
          className="flex min-w-0 items-center gap-2 rounded-sm text-fg-default"
        >
          {/* A geometric mark rather than a logotype: the product name is set
              in the same face as the rest of the interface, which is what makes
              the shell read as software instead of as a brand surface. */}
          <span className="size-4 shrink-0 rotate-45 rounded-xs bg-accent-emphasis" aria-hidden />
          {!collapsed && (
            <span className="truncate text-h3">
              Knowledge<span className="font-normal text-fg-muted">Assistant</span>
            </span>
          )}
        </Link>
      </div>

      <ul className={cn('flex flex-1 flex-col gap-0.5 py-2', collapsed ? 'px-1' : 'px-2')}>
        {navigationItems.map((item) => (
          <li key={item.path}>
            <NavRailItem item={item} collapsed={collapsed} />
          </li>
        ))}
      </ul>

      <div
        className={cn(
          'flex shrink-0 gap-1 border-t p-2',
          collapsed ? 'flex-col items-center' : 'items-center',
        )}
      >
        <ThemeToggle showLabel={!collapsed} />

        <Button
          variant="subtle"
          size="icon"
          onClick={onShowShortcuts}
          aria-label="Keyboard shortcuts"
          title="Keyboard shortcuts (?)"
        >
          <Keyboard size={16} strokeWidth={1.5} aria-hidden />
        </Button>

        {canCollapse && (
          <Button
            variant="subtle"
            size="icon"
            onClick={onToggleCollapsed}
            aria-label={collapsed ? 'Expand navigation' : 'Collapse navigation'}
            title={`${collapsed ? 'Expand' : 'Collapse'} navigation (Ctrl+B)`}
          >
            <ToggleIcon size={16} strokeWidth={1.5} aria-hidden />
          </Button>
        )}
      </div>
    </nav>
  );
}
