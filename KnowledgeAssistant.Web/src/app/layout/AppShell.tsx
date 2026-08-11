import { useEffect, useRef, useState } from 'react';
import { Outlet, useLocation } from 'react-router';

import { KeyboardShortcutsDialog } from '@/app/layout/KeyboardShortcutsDialog';
import { NavRail } from '@/app/layout/NavRail';
import { SkipToContentLink } from '@/app/layout/SkipToContentLink';
import { StatusBar } from '@/app/layout/StatusBar';
import { TopBar } from '@/app/layout/TopBar';
import { UploadAction } from '@/features/document-upload/components/UploadAction';
import { useDocumentTitle } from '@/app/router/useDocumentTitle';
import { useNavigationChords } from '@/app/router/useNavigationChords';
import { useRouteTitle } from '@/app/router/useRouteTitle';
import { useKeyboardShortcut } from '@/shared/hooks/useKeyboardShortcut';
import { useMediaQuery } from '@/shared/hooks/useMediaQuery';
import { useTheme } from '@/shared/theme/useTheme';

const MAIN_CONTENT_ID = 'main-content';

/** Below the `md` breakpoint (§7) the rail is icons only, whatever the user chose. */
const NARROW_VIEWPORT = '(max-width: 899px)';

const THEME_CYCLE: readonly ['system', 'light', 'dark'] = ['system', 'light', 'dark'];

/**
 * The application frame: rail down the full height, top bar over the content
 * column only, status bar across the bottom.
 *
 * Laid out as a grid rather than nested flex containers so that the content
 * column is the only region that scrolls — the rail and the two bars stay put
 * without any of them being taken out of flow.
 */
export function AppShell() {
  const [railCollapsedByUser, setRailCollapsedByUser] = useState(false);
  const [shortcutsOpen, setShortcutsOpen] = useState(false);

  const isNarrow = useMediaQuery(NARROW_VIEWPORT);
  const { preference, setPreference } = useTheme();
  const { pathname } = useLocation();

  const title = useRouteTitle('KnowledgeAssistant');
  useDocumentTitle(title);
  useNavigationChords();

  // The viewport wins. Keeping the user's choice in its own state rather than
  // overwriting it means widening the window restores what they had, instead of
  // silently adopting whatever the narrow layout forced.
  const railCollapsed = isNarrow || railCollapsedByUser;

  const toggleRail = () => {
    setRailCollapsedByUser((collapsed) => !collapsed);
  };

  useKeyboardShortcut({ key: 'b', meta: true }, toggleRail);
  useKeyboardShortcut({ key: 'j', meta: true }, () => {
    const index = THEME_CYCLE.indexOf(preference);
    setPreference(THEME_CYCLE[(index + 1) % THEME_CYCLE.length] ?? 'system');
  });
  useKeyboardShortcut({ key: '?', shift: true }, () => {
    setShortcutsOpen(true);
  });

  const mainRef = useRef<HTMLElement>(null);
  const hasNavigated = useRef(false);

  useEffect(() => {
    // Not on first paint — that would steal focus from the address bar before
    // the user has done anything. On a real navigation it moves focus out of
    // the rail and gives a screen reader something to announce.
    if (!hasNavigated.current) {
      hasNavigated.current = true;
      return;
    }

    // A screen that focuses its own control — Ask focuses the composer — has
    // made a better choice than the shell can. Only claim focus when the new
    // screen has not already placed it somewhere inside itself.
    if (mainRef.current?.contains(document.activeElement)) {
      return;
    }

    mainRef.current?.focus();
  }, [pathname]);

  return (
    <div className="grid h-dvh grid-cols-[auto_1fr] grid-rows-[1fr_auto] overflow-hidden">
      <SkipToContentLink targetId={MAIN_CONTENT_ID} />

      <NavRail
        collapsed={railCollapsed}
        canCollapse={!isNarrow}
        onToggleCollapsed={toggleRail}
        onShowShortcuts={() => {
          setShortcutsOpen(true);
        }}
      />

      <div className="flex min-w-0 flex-col overflow-hidden">
        {/* Upload is a shell action rather than a screen's: docs/DESIGN.md puts
            it in the top bar of both Ask (§6.1) and Documents (§6.4), and
            ingesting is worth doing from wherever you happen to be. */}
        <TopBar title={title} actions={<UploadAction />} />
        <main
          id={MAIN_CONTENT_ID}
          ref={mainRef}
          tabIndex={-1}
          className="min-h-0 flex-1 overflow-auto focus-visible:outline-none"
        >
          {/* Capped and centred so that on a very wide monitor the content does
              not stretch into an unreadable measure (§7). */}
          <div className="mx-auto h-full w-full max-w-content">
            <Outlet />
          </div>
        </main>
      </div>

      <div className="col-span-2">
        <StatusBar />
      </div>

      <KeyboardShortcutsDialog open={shortcutsOpen} onOpenChange={setShortcutsOpen} />
    </div>
  );
}
