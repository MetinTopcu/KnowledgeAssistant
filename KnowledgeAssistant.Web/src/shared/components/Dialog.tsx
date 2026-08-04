import * as DialogPrimitive from '@radix-ui/react-dialog';
import { X } from 'lucide-react';
import { type ReactNode } from 'react';

import { Button } from '@/shared/components/Button';

interface DialogProps {
  readonly open: boolean;
  readonly onOpenChange: (open: boolean) => void;
  readonly title: string;
  readonly description?: string;
  readonly children: ReactNode;
}

/**
 * A modal dialog.
 *
 * Built on the Radix primitive rather than hand-rolled: focus trapping, scroll
 * locking, `Esc` handling, and the `aria-modal` wiring are the parts everyone
 * gets subtly wrong, and they are the parts a keyboard user depends on
 * entirely.
 *
 * Styling is this system's, not shadcn's — one of only two places permitted an
 * elevation (§9.6), and entering on opacity alone (§9.7).
 */
export function Dialog({ open, onOpenChange, title, description, children }: DialogProps) {
  return (
    <DialogPrimitive.Root open={open} onOpenChange={onOpenChange}>
      <DialogPrimitive.Portal>
        <DialogPrimitive.Overlay className="fixed inset-0 z-overlay bg-black/40 data-[state=open]:animate-overlay-in" />
        <DialogPrimitive.Content
          // Radix warns when no Description is rendered unless the association
          // is explicitly opted out of. Spread rather than a ternary, because
          // passing the prop as undefined is not the same as omitting it: when
          // a description *is* present, the prop must be absent so Radix can
          // wire the generated id itself.
          {...(description === undefined ? { 'aria-describedby': undefined } : {})}
          className="fixed top-1/2 left-1/2 z-dialog w-[calc(100vw-2rem)] max-w-dialog -translate-x-1/2 -translate-y-1/2 rounded-md border bg-canvas-overlay shadow-elevation-2 data-[state=open]:animate-dialog-in"
        >
          <div className="flex items-start justify-between gap-4 border-b px-5 py-4">
            <div className="flex min-w-0 flex-col gap-1">
              <DialogPrimitive.Title className="text-h2">{title}</DialogPrimitive.Title>
              {description !== undefined && (
                <DialogPrimitive.Description className="text-caption text-fg-muted">
                  {description}
                </DialogPrimitive.Description>
              )}
            </div>
            <DialogPrimitive.Close asChild>
              <Button variant="subtle" size="icon" aria-label="Close">
                <X size={16} strokeWidth={1.5} aria-hidden />
              </Button>
            </DialogPrimitive.Close>
          </div>

          <div className="max-h-[70vh] overflow-auto px-5 py-4">{children}</div>
        </DialogPrimitive.Content>
      </DialogPrimitive.Portal>
    </DialogPrimitive.Root>
  );
}
