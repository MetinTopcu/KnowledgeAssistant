import { useMediaQuery } from '@/shared/hooks/useMediaQuery';

export interface AccessibilityPreferences {
  /** `prefers-reduced-motion: reduce` — motion is suppressed while true. */
  readonly reducedMotion: boolean;
  /** `prefers-contrast: more`. */
  readonly increasedContrast: boolean;
  /** `forced-colors: active` — Windows contrast themes, and the browser's own equivalents. */
  readonly forcedColors: boolean;
  /** What `prefers-color-scheme` currently reports, which is what "System" resolves to. */
  readonly systemColorScheme: 'dark' | 'light';
}

const REDUCED_MOTION = '(prefers-reduced-motion: reduce)';
const INCREASED_CONTRAST = '(prefers-contrast: more)';
const FORCED_COLORS = '(forced-colors: active)';
const DARK_COLOR_SCHEME = '(prefers-color-scheme: dark)';

/**
 * Reads the accessibility preferences the operating system publishes.
 *
 * These are settings the user already made somewhere else, so the screen
 * reports them rather than offering to set them: an in-app override would be a
 * second source of truth for a preference the OS already owns, and the one that
 * loses is always the one the user actually configured.
 *
 * Live, not read once. Changing the OS setting with the app open updates the
 * page, which is also how a user verifies the app can see the change at all.
 */
export function useAccessibilityPreferences(): AccessibilityPreferences {
  const reducedMotion = useMediaQuery(REDUCED_MOTION);
  const increasedContrast = useMediaQuery(INCREASED_CONTRAST);
  const forcedColors = useMediaQuery(FORCED_COLORS);
  const prefersDark = useMediaQuery(DARK_COLOR_SCHEME);

  return {
    reducedMotion,
    increasedContrast,
    forcedColors,
    systemColorScheme: prefersDark ? 'dark' : 'light',
  };
}
