import { SettingsSectionPanel } from '@/features/settings/components/SettingsSectionPanel';
import { KeyHint } from '@/shared/components/KeyHint';
import { ShortcutReference } from '@/shared/components/ShortcutReference';

/**
 * The keyboard reference at rest.
 *
 * The same component the `?` dialog renders, from the same registry: this
 * screen is where you read the bindings, the dialog is where you check one
 * mid-task, and neither can drift from the other.
 */
export function KeyboardSection() {
  return (
    <SettingsSectionPanel slug="keyboard">
      <ShortcutReference />

      <p className="text-caption text-fg-subtle">
        <KeyHint keys={['?']} /> opens this same list over any screen. Bindings appear here as they
        are wired up, so a key named on this page is a key that works — the full model in the design
        specification is larger than this list, and the difference is what has not been built yet.
      </p>
    </SettingsSectionPanel>
  );
}
