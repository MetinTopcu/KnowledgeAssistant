import { SettingsSectionPanel } from '@/features/settings/components/SettingsSectionPanel';
import { useAccessibilityPreferences } from '@/features/settings/model/useAccessibilityPreferences';
import { Alert } from '@/shared/components/Alert';
import { DefinitionList, DefinitionRow } from '@/shared/components/DefinitionList';

interface DetectedProps {
  readonly value: string;
}

/** The detected value, in the same mono chip the health screen uses for flags. */
function Detected({ value }: DetectedProps) {
  return (
    <span className="w-fit rounded-xs border bg-canvas-inset px-1 font-mono text-mono-chip text-fg-default">
      {value}
    </span>
  );
}

/**
 * Accessibility.
 *
 * Nothing on this page is a control, and that is the design: docs/DESIGN.md §14
 * commits to *respecting* `prefers-reduced-motion`, `prefers-color-scheme`, and
 * `prefers-contrast`, and defines no preference of the product's own. So the
 * section reports what the operating system says, what this client does about
 * it, and — where the answer is "nothing yet" — says that instead of offering a
 * switch that would fight a setting the OS already owns.
 */
export function AccessibilitySection() {
  const { reducedMotion, increasedContrast, forcedColors, systemColorScheme } =
    useAccessibilityPreferences();

  return (
    <SettingsSectionPanel slug="accessibility">
      <DefinitionList label="System accessibility preferences and how this client honours them">
        <DefinitionRow
          term="Reduced motion"
          note="Honoured in full. While this is on, every transition and animation is suppressed — the skeleton pulse, the indeterminate progress bar, the dialog fade — and the scroll to a cited passage jumps instead of gliding."
        >
          <Detected
            value={`prefers-reduced-motion: ${reducedMotion ? 'reduce' : 'no-preference'}`}
          />
        </DefinitionRow>

        <DefinitionRow
          term="Colour scheme"
          note="Read continuously, not once at startup. This is what the Appearance theme resolves to while it is set to System, so switching your device to dark at sunset switches this window with it."
        >
          <Detected value={`prefers-color-scheme: ${systemColorScheme}`} />
        </DefinitionRow>

        <DefinitionRow
          term="Increased contrast"
          note="Read but not yet acted on. Both palettes already meet WCAG 2.2 AA on every foreground and background pair, and no higher-contrast variant exists to switch to — so this client does not currently change anything when the preference is on."
        >
          <Detected value={`prefers-contrast: ${increasedContrast ? 'more' : 'no-preference'}`} />
        </DefinitionRow>

        <DefinitionRow
          term="Forced colours"
          note="Handled by the browser, which replaces this product's palette with your system colours wherever it applies. Status is never carried by colour alone here — every dot has a label and every code is spelled out — so nothing becomes unreadable when the palette is taken over."
        >
          <Detected value={`forced-colors: ${forcedColors ? 'active' : 'none'}`} />
        </DefinitionRow>
      </DefinitionList>

      <Alert variant="info" title="There is nothing to switch on this page">
        Each preference above belongs to your operating system, and this client reads it live. An
        in-app override would be a second answer to a question the OS has already answered, and the
        one that loses is always the one you actually configured.
      </Alert>
    </SettingsSectionPanel>
  );
}
