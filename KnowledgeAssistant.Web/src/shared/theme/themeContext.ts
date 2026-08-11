import { createContext } from 'react';

import { type ResolvedTheme, type ThemePreference } from '@/shared/theme/theme';

export interface ThemeContextValue {
  /** What the user chose, which may be "system". */
  readonly preference: ThemePreference;
  /** What "system" currently resolves to, and therefore what is on screen. */
  readonly resolvedTheme: ResolvedTheme;
  readonly setPreference: (preference: ThemePreference) => void;
}

/**
 * Undefined outside the provider, so that `useTheme` can fail loudly rather
 * than hand back a plausible default that silently never changes anything.
 *
 * The context and its hook live in `shared/` while the provider stays in
 * `app/providers/`: composing the provider is the composition root's job, but
 * reading the theme is not, and Settings (§6.8) is a feature that has to read
 * and write it. A feature reaching into `app/` for that would invert the
 * dependency the folder layout exists to state.
 */
export const ThemeContext = createContext<ThemeContextValue | undefined>(undefined);
