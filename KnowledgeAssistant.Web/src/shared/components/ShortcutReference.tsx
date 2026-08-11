import { KeyHint } from '@/shared/components/KeyHint';
import { shortcutGroups } from '@/shared/lib/keyboardShortcuts';

/**
 * The keyboard reference, rendered from the one registry that records which
 * bindings exist.
 *
 * Both surfaces that show shortcuts — the `?` dialog and Settings › Keyboard —
 * render this component rather than their own list. A reference that is
 * transcribed twice is a reference that disagrees with itself the first time a
 * binding moves.
 */
export function ShortcutReference() {
  return (
    <div className="flex flex-col gap-6">
      {shortcutGroups.map((group) => (
        <section key={group.name} className="flex flex-col gap-2">
          <h3 className="eyebrow text-fg-subtle">{group.name}</h3>
          <dl className="flex flex-col">
            {group.shortcuts.map((shortcut) => (
              <div
                key={shortcut.description}
                className="flex h-row-default items-center justify-between gap-4 border-b border-b-border-muted last:border-b-0"
              >
                <dt className="min-w-0 truncate text-ui text-fg-default">{shortcut.description}</dt>
                <dd className="shrink-0">
                  <KeyHint keys={shortcut.keys} />
                </dd>
              </div>
            ))}
          </dl>
        </section>
      ))}
    </div>
  );
}
