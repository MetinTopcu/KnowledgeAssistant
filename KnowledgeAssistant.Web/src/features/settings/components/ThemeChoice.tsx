import { Monitor, Moon, Sun, type LucideIcon } from 'lucide-react';

import { useAccessibilityPreferences } from '@/features/settings/model/useAccessibilityPreferences';
import { cn } from '@/shared/lib/cn';
import { type ResolvedTheme, type ThemePreference } from '@/shared/theme/theme';
import { useTheme } from '@/shared/theme/useTheme';

interface ThemeOption {
  readonly value: ThemePreference;
  readonly label: string;
  readonly icon: LucideIcon;
}

const OPTIONS: readonly ThemeOption[] = [
  { value: 'system', label: 'System', icon: Monitor },
  { value: 'light', label: 'Light', icon: Sun },
  { value: 'dark', label: 'Dark', icon: Moon },
];

function describe(value: ThemePreference, systemColorScheme: ResolvedTheme): string {
  switch (value) {
    case 'system':
      // The device's own report, not the applied theme: an explicit light
      // choice would otherwise have this line claim the OS asked for light.
      return `Follows this device, which currently reports ${systemColorScheme}.`;
    case 'light':
      return 'Full parity with dark. Every token pair meets WCAG AA in both.';
    case 'dark':
      return 'The default, for a window that stays open all day.';
  }
}

/**
 * The explicit three-way theme choice from docs/DESIGN.md §6.8.
 *
 * Native radios rather than a segmented control or a set of buttons with
 * `role="radiogroup"`: arrow-key traversal, the roving tab stop, and the
 * announced "2 of 3" all come free and correct, and the control the user is
 * choosing between is genuinely one-of-three. The label is the click target, so
 * the row is 44px of hit area rather than a 16px circle.
 */
export function ThemeChoice() {
  const { preference, setPreference } = useTheme();
  const { systemColorScheme } = useAccessibilityPreferences();

  return (
    <fieldset className="flex flex-col rounded-sm border">
      <legend className="sr-only">Theme</legend>

      {OPTIONS.map((option) => {
        const Icon = option.icon;
        const selected = option.value === preference;

        return (
          <label
            key={option.value}
            className={cn(
              'flex cursor-pointer items-center gap-3 border-b border-b-border-muted px-3 py-2.5 last:border-b-0',
              'transition-colors duration-instant ease-standard',
              selected ? 'bg-canvas-selected' : 'hover:bg-canvas-hover',
            )}
          >
            <input
              type="radio"
              name="theme-preference"
              value={option.value}
              checked={selected}
              onChange={() => {
                setPreference(option.value);
              }}
              // Named by the label alone and described by the line under it.
              // Wrapping both in the `<label>` makes the whole row clickable but
              // would otherwise fold the description into the name, so the
              // option would announce as one long sentence rather than as
              // "System, radio button, 1 of 3" with the detail following.
              aria-labelledby={`theme-${option.value}-label`}
              aria-describedby={`theme-${option.value}-description`}
              className="size-4 shrink-0 accent-accent-emphasis"
            />

            <Icon
              size={16}
              strokeWidth={1.5}
              className={selected ? 'shrink-0 text-fg-default' : 'shrink-0 text-fg-muted'}
              aria-hidden
            />

            <span className="flex min-w-0 flex-col">
              <span
                id={`theme-${option.value}-label`}
                className={cn('text-ui', selected ? 'font-semibold' : undefined)}
              >
                {option.label}
              </span>
              <span
                id={`theme-${option.value}-description`}
                className="text-caption text-fg-subtle"
              >
                {describe(option.value, systemColorScheme)}
              </span>
            </span>
          </label>
        );
      })}
    </fieldset>
  );
}
