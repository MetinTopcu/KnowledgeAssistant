import { NavLink } from 'react-router';

import { settingsSections } from '@/features/settings/model/settingsSections';
import { cn } from '@/shared/lib/cn';

/**
 * The section list.
 *
 * It carries the rail's selected treatment — a 2px leading accent bar rather
 * than a filled pill — because it is the same gesture one level down, and two
 * different answers to "which item is selected" in one window is one too many.
 *
 * The targets are relative, so this component states the section slugs once and
 * the router owns where `/settings` itself lives.
 */
export function SettingsSectionNav() {
  return (
    <nav aria-label="Settings sections" className="shrink-0 md:w-48">
      <ul className="flex flex-col gap-0.5">
        {settingsSections.map((section) => {
          const Icon = section.icon;

          return (
            <li key={section.slug}>
              <NavLink
                to={section.slug}
                className={({ isActive }) =>
                  cn(
                    'relative flex h-8 items-center gap-2 rounded-sm px-2 text-ui',
                    'transition-colors duration-instant ease-standard',
                    isActive
                      ? 'bg-canvas-selected font-semibold text-fg-default before:absolute before:top-1 before:bottom-1 before:-left-2 before:w-0.5 before:rounded-xs before:bg-accent-fg'
                      : 'text-fg-muted hover:bg-canvas-hover hover:text-fg-default',
                  )
                }
              >
                <Icon size={16} strokeWidth={1.5} aria-hidden />
                <span className="truncate">{section.label}</span>
              </NavLink>
            </li>
          );
        })}
      </ul>
    </nav>
  );
}
