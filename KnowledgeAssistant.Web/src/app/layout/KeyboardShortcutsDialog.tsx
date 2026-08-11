import { Dialog } from '@/shared/components/Dialog';
import { ShortcutReference } from '@/shared/components/ShortcutReference';

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
      description="Every binding currently wired up. The same reference is on the Settings screen."
    >
      <ShortcutReference />
    </Dialog>
  );
}
