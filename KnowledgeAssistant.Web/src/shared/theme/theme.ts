/**
 * Theme resolution, kept out of the component that applies it.
 *
 * The product offers System / Light / Dark. "System" is a preference, not a
 * theme — it has to be resolved against the OS before anything can be applied,
 * and it can change while the app is open.
 */

export type ThemePreference = 'system' | 'light' | 'dark';
export type ResolvedTheme = 'light' | 'dark';

const STORAGE_KEY = 'knowledgeassistant.theme';

/**
 * Dark, per docs/DESIGN.md §9.2: this is a tool engineers keep open all day.
 * Light has full parity and is one setting away.
 */
export const DEFAULT_THEME_PREFERENCE: ThemePreference = 'dark';

const DARK_MEDIA_QUERY = '(prefers-color-scheme: dark)';

function isThemePreference(value: string | null): value is ThemePreference {
  return value === 'system' || value === 'light' || value === 'dark';
}

/**
 * Reads the stored preference, falling back to the default.
 *
 * Storage can throw rather than return null — Safari's private mode and
 * enterprise policies that disable site data both do — so a failure here has to
 * mean "no preference", never "no application".
 */
export function readThemePreference(): ThemePreference {
  try {
    const stored = window.localStorage.getItem(STORAGE_KEY);
    return isThemePreference(stored) ? stored : DEFAULT_THEME_PREFERENCE;
  } catch {
    return DEFAULT_THEME_PREFERENCE;
  }
}

/**
 * Persists the preference. A failure to store is deliberately swallowed: the
 * theme still applies for this session, and refusing to switch because storage
 * is unavailable would be a worse outcome than forgetting the choice.
 */
export function writeThemePreference(preference: ThemePreference): void {
  try {
    window.localStorage.setItem(STORAGE_KEY, preference);
  } catch {
    // Storage is unavailable; the in-memory preference still governs.
  }
}

/** Resolves a preference against the OS setting. */
export function resolveTheme(preference: ThemePreference): ResolvedTheme {
  if (preference !== 'system') {
    return preference;
  }

  return window.matchMedia(DARK_MEDIA_QUERY).matches ? 'dark' : 'light';
}

/**
 * Applies the theme by toggling the class the token stylesheet keys off.
 *
 * `color-scheme` rides along on the same class so that scrollbars, form
 * controls, and the browser's own chrome match the page rather than staying
 * light on a dark canvas.
 */
export function applyTheme(theme: ResolvedTheme): void {
  document.documentElement.classList.toggle('dark', theme === 'dark');
}

/** Subscribes to OS theme changes. Returns an unsubscribe function. */
export function subscribeToSystemTheme(onChange: () => void): () => void {
  const query = window.matchMedia(DARK_MEDIA_QUERY);
  query.addEventListener('change', onChange);
  return () => {
    query.removeEventListener('change', onChange);
  };
}
