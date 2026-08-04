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
 */
export const ThemeContext = createContext<ThemeContextValue | undefined>(undefined);
