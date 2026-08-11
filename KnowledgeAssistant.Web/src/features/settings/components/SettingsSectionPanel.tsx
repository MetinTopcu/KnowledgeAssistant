import { type ReactNode } from 'react';

import {
  settingsSectionBySlug,
  type SettingsSectionSlug,
} from '@/features/settings/model/settingsSections';

interface SettingsSectionPanelProps {
  readonly slug: SettingsSectionSlug;
  readonly children: ReactNode;
}

/**
 * The frame every section renders inside: heading, one line of description,
 * then the section's own content.
 *
 * The heading and the description come from the same registry as the section
 * list, so the name a user clicked is the name they land on.
 */
export function SettingsSectionPanel({ slug, children }: SettingsSectionPanelProps) {
  const section = settingsSectionBySlug[slug];

  return (
    <section aria-labelledby={`settings-${slug}`} className="flex max-w-answer flex-col gap-5">
      <div className="flex flex-col gap-1">
        <h2 id={`settings-${slug}`} className="text-h1">
          {section.label}
        </h2>
        <p className="text-ui text-fg-muted">{section.description}</p>
      </div>

      {children}
    </section>
  );
}
