import { Monitor, Moon, Sun } from 'lucide-react';
import { type LucideIcon } from 'lucide-react';

import { useTheme } from '@/app/providers/useTheme';
import { Button } from '@/shared/components/Button';
import { type ThemePreference } from '@/shared/theme/theme';

/** The cycle order, chosen so a single press leaves "system" for an explicit choice. */
const CYCLE: readonly ThemePreference[] = ['system', 'light', 'dark'];

const ICONS: Record<ThemePreference, LucideIcon> = {
  system: Monitor,
  light: Sun,
  dark: Moon,
};

const LABELS: Record<ThemePreference, string> = {
  system: 'System',
  light: 'Light',
  dark: 'Dark',
};

function nextPreference(current: ThemePreference): ThemePreference {
  const index = CYCLE.indexOf(current);
  return CYCLE[(index + 1) % CYCLE.length] ?? 'system';
}

interface ThemeToggleProps {
  readonly showLabel: boolean;
}

/**
 * Cycles System → Light → Dark.
 *
 * A cycle rather than a menu because the rail footer has room for one control,
 * not a popover. The label announces the current value and the next one, so the
 * cycle is stated rather than discovered by pressing it three times. Settings
 * (Slice 6) offers the explicit three-way choice docs/DESIGN.md §6.8 specifies;
 * this is the shortcut, not the setting.
 */
export function ThemeToggle({ showLabel }: ThemeToggleProps) {
  const { preference, setPreference } = useTheme();

  const Icon = ICONS[preference];
  const next = nextPreference(preference);
  const label = `Theme: ${LABELS[preference]}. Switch to ${LABELS[next]}.`;

  return (
    <Button
      variant="subtle"
      size={showLabel ? 'sm' : 'icon'}
      onClick={() => {
        setPreference(next);
      }}
      aria-label={label}
      title={`${label} (Ctrl+J)`}
      className={showLabel ? 'min-w-0 flex-1 justify-start' : ''}
    >
      <Icon size={16} strokeWidth={1.5} aria-hidden />
      {showLabel && <span className="truncate">{LABELS[preference]}</span>}
    </Button>
  );
}
