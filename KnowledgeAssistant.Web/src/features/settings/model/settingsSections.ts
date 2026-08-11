import { Accessibility, Keyboard, Palette, Server, type LucideIcon } from 'lucide-react';

/**
 * The sections of the Settings screen.
 *
 * Fewer than docs/DESIGN.md §6.8 names, deliberately. That section also lists
 * **Defaults** — default mode, default topK, default maxSources — and those are
 * not here, because a stored default is only a setting once a screen reads it.
 * Shipping the controls before Ask and Agent consult them would put four
 * inputs on the page that change nothing, which is the definition of a
 * placeholder. They arrive with the change that makes the composers read them.
 *
 * Appearance is also narrower than §6.8: theme is here, density and mono font
 * size are not, for the same reason — no screen varies by them yet.
 */
export type SettingsSectionSlug = 'appearance' | 'keyboard' | 'accessibility' | 'environment';

export interface SettingsSection {
  readonly slug: SettingsSectionSlug;
  readonly label: string;
  /** One line, shown under the heading of the section it names. */
  readonly description: string;
  readonly icon: LucideIcon;
}

/**
 * Keyed by slug so that every section is guaranteed a definition and no slug
 * can be added without one. Insertion order is the order of the section list.
 */
const SECTIONS = {
  appearance: {
    slug: 'appearance',
    label: 'Appearance',
    description: 'How this browser renders the product.',
    icon: Palette,
  },
  keyboard: {
    slug: 'keyboard',
    label: 'Keyboard',
    description: 'Every binding this build has wired up.',
    icon: Keyboard,
  },
  accessibility: {
    slug: 'accessibility',
    label: 'Accessibility',
    description: 'The system preferences this client reads, and what it does with each.',
    icon: Accessibility,
  },
  environment: {
    slug: 'environment',
    label: 'Environment',
    description: 'The configuration this client was built with. Read-only.',
    icon: Server,
  },
} as const satisfies Record<SettingsSectionSlug, SettingsSection>;

export const settingsSectionBySlug: Readonly<Record<SettingsSectionSlug, SettingsSection>> =
  SECTIONS;

export const settingsSections: readonly SettingsSection[] = Object.values(SECTIONS);

/** Where `/settings` lands. Appearance, because it is the only section that acts. */
export const DEFAULT_SETTINGS_SECTION: SettingsSectionSlug = 'appearance';
