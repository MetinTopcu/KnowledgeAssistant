import { SettingsSectionPanel } from '@/features/settings/components/SettingsSectionPanel';
import { ThemeChoice } from '@/features/settings/components/ThemeChoice';
import { KeyHint } from '@/shared/components/KeyHint';
import { useTheme } from '@/shared/theme/useTheme';

/**
 * Appearance.
 *
 * Theme only. Density and mono font size are named in docs/DESIGN.md §6.8 and
 * are absent on purpose: no screen varies by either yet, and a control that
 * stores a value nothing reads is a control that lies about having an effect.
 */
export function AppearanceSection() {
  const { resolvedTheme } = useTheme();

  return (
    <SettingsSectionPanel slug="appearance">
      <div className="flex flex-col gap-2">
        <h3 className="eyebrow text-fg-subtle">Theme</h3>
        <ThemeChoice />
        <p className="text-caption text-fg-subtle">
          Rendering {resolvedTheme} right now. The choice is stored in this browser — the API has no
          user accounts yet, so it cannot follow you to another machine. <KeyHint keys={['Ctrl']} />{' '}
          <KeyHint keys={['J']} /> cycles the same three values from anywhere.
        </p>
      </div>
    </SettingsSectionPanel>
  );
}
