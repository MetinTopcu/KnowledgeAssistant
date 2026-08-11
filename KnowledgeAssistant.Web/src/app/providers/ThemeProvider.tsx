import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';

import { ThemeContext, type ThemeContextValue } from '@/shared/theme/themeContext';
import {
  applyTheme,
  readThemePreference,
  resolveTheme,
  subscribeToSystemTheme,
  writeThemePreference,
  type ResolvedTheme,
  type ThemePreference,
} from '@/shared/theme/theme';

interface ThemeProviderProps {
  readonly children: ReactNode;
}

/**
 * Owns the theme preference and keeps the document in step with it.
 *
 * `resolvedTheme` is held in state rather than derived on each render because
 * it can change without anything in React changing — the OS switching to dark
 * at sunset is an external event, and the subscription is what turns it into a
 * render.
 */
export function ThemeProvider({ children }: ThemeProviderProps) {
  const [preference, setPreferenceState] = useState<ThemePreference>(readThemePreference);
  const [resolvedTheme, setResolvedTheme] = useState<ResolvedTheme>(() =>
    resolveTheme(readThemePreference()),
  );

  useEffect(() => {
    const apply = () => {
      const resolved = resolveTheme(preference);
      applyTheme(resolved);
      setResolvedTheme(resolved);
    };

    apply();

    // Subscribed unconditionally. The listener recomputes from the current
    // preference, so it is a no-op for an explicit light or dark choice — and
    // an unconditional subscription cannot be left dangling by a preference
    // change that a conditional one would have skipped.
    return subscribeToSystemTheme(apply);
  }, [preference]);

  const setPreference = useCallback((next: ThemePreference) => {
    writeThemePreference(next);
    setPreferenceState(next);
  }, []);

  const value = useMemo<ThemeContextValue>(
    () => ({ preference, resolvedTheme, setPreference }),
    [preference, resolvedTheme, setPreference],
  );

  return <ThemeContext.Provider value={value}>{children}</ThemeContext.Provider>;
}
