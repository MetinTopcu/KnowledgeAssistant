/**
 * The reading of environment configuration that the Environment section shows.
 *
 * Kept out of the component because "what does a base URL of `/` actually
 * resolve to" is a question with an answer, not a rendering decision.
 */

export interface ApiBaseUrl {
  /** Verbatim, as configured at build time. */
  readonly configured: string;
  /** What a request actually goes to from this page, absolute. */
  readonly effective: string;
  /** True when the configured value is relative, so the API rides this origin. */
  readonly isSameOrigin: boolean;
}

/**
 * Resolves the configured base URL against the page's origin.
 *
 * `/` is the committed default and the co-hosted deployment's value, so the
 * configured string alone tells a user almost nothing — the absolute URL is
 * what they can paste into curl or compare against the browser's network tab.
 * Both are shown; neither replaces the other.
 */
export function resolveApiBaseUrl(configured: string, origin: string): ApiBaseUrl {
  try {
    const url = new URL(configured, origin);
    return {
      configured,
      effective: url.href,
      isSameOrigin: url.origin === origin,
    };
  } catch {
    // An unparseable value is still worth showing verbatim: it is precisely the
    // configuration a user comes to this page to check.
    return { configured, effective: configured, isSameOrigin: false };
  }
}

/** A duration in milliseconds, and the same duration in words. */
export function describeTimeout(milliseconds: number): string {
  if (milliseconds % 60_000 === 0) {
    const minutes = milliseconds / 60_000;
    return `${minutes.toString()} ${minutes === 1 ? 'minute' : 'minutes'}`;
  }

  const seconds = milliseconds / 1_000;
  return `${seconds.toString()} ${seconds === 1 ? 'second' : 'seconds'}`;
}
