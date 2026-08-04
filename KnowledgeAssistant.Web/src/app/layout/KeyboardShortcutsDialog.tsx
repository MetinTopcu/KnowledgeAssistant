import { shortcutGroups } from '@/app/layout/keyboardShortcuts';
import { Dialog } from '@/shared/components/Dialog';
import { KeyHint } from '@/shared/components/KeyHint';

interface KeyboardShortcutsDialogProps {
  readonly open: boolean;
  readonly onOpenChange: (open: boolean) => void;
}

export function KeyboardShortcutsDialog({ open, onOpenChange }: KeyboardShortcutsDialogProps) {
  return (
    <Dialog
      open={open}
      onOpenChange={onOpenChange}
      title="Keyboard shortcuts"
      description="Every binding currently wired up. More arrive with the screens that use them."
    >
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
                  <dt className="min-w-0 truncate text-ui text-fg-default">
                    {shortcut.description}
                  </dt>
                  <dd className="shrink-0">
                    <KeyHint keys={shortcut.keys} />
                  </dd>
                </div>
              ))}
            </dl>
          </section>
        ))}
      </div>
    </Dialog>
  );
}
