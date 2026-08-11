import { Outlet } from 'react-router';

import { SettingsSectionNav } from '@/features/settings/components/SettingsSectionNav';

/**
 * The Settings screen.
 *
 * Sub-navigation is a left-aligned list inside the content area rather than
 * tabs in the top bar (docs/DESIGN.md §5), and each section is a route, so a
 * section can be linked to, bookmarked, and reached by the browser's own back
 * button.
 */
export function SettingsRoute() {
  return (
    <div className="flex flex-col gap-6 p-6 md:flex-row md:gap-8">
      <SettingsSectionNav />

      <div className="min-w-0 flex-1">
        <Outlet />
      </div>
    </div>
  );
}
