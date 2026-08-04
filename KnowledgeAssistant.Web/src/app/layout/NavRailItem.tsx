import { NavLink } from 'react-router';

import { type NavigationItem } from '@/app/layout/navigationItems';
import { cn } from '@/shared/lib/cn';

interface NavRailItemProps {
  readonly item: NavigationItem;
  readonly collapsed: boolean;
}

export function NavRailItem({ item, collapsed }: NavRailItemProps) {
  const Icon = item.icon;

  return (
    <NavLink
      to={item.path}
      end={item.end ?? false}
      title={collapsed ? item.label : undefined}
      className={({ isActive }) =>
        cn(
          'relative flex h-8 items-center gap-2 rounded-sm text-ui',
          'transition-colors duration-instant ease-standard',
          collapsed ? 'justify-center px-0' : 'px-2',
          isActive
            ? 'bg-canvas-selected font-semibold text-fg-default'
            : 'text-fg-muted hover:bg-canvas-hover hover:text-fg-default',
          // A 2px leading bar rather than a filled pill: the selected state
          // should read as a marker in a list, not as a floating object.
          isActive &&
            'before:absolute before:top-1 before:bottom-1 before:-left-2 before:w-0.5 before:rounded-xs before:bg-accent-fg',
        )
      }
    >
      <Icon size={collapsed ? 20 : 16} strokeWidth={1.5} aria-hidden />
      {!collapsed && <span className="truncate">{item.label}</span>}
    </NavLink>
  );
}
