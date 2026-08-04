import { env } from '@/shared/config/env';

/**
 * The ambient channel along the bottom of the window, in the manner of an
 * editor's status bar.
 *
 * It carries only what is true right now with no request in flight: which stack
 * this is, which region, and which build. The health indicator, last-run
 * latency, and last correlation id described in docs/DESIGN.md §5 join it with
 * the slices that produce them — an indicator wired to nothing would be a
 * decoration that looks like a fact.
 */
export function StatusBar() {
  return (
    <footer className="flex h-statusbar shrink-0 items-center justify-between gap-4 border-t px-3 text-mono-ui text-fg-subtle">
      <div className="flex min-w-0 items-center gap-3">
        <span className="truncate font-mono">{env.environment}</span>
        {/* The region is the first thing to go on a narrow viewport: which
            stack you are on matters at every width, which region does not. */}
        <span className="hidden h-3 w-px shrink-0 bg-border-default md:block" aria-hidden />
        <span className="hidden truncate font-mono md:inline">{env.azureRegion}</span>
      </div>
      <span className="shrink-0 font-mono">v{env.appVersion}</span>
    </footer>
  );
}
