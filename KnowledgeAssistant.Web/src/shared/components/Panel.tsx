import * as DialogPrimitive from '@radix-ui/react-dialog';
import { X } from 'lucide-react';
import { type ReactNode } from 'react';

import { Button } from '@/shared/components/Button';

interface PanelProps {
  readonly open: boolean;
  readonly onOpenChange: (open: boolean) => void;
  readonly title: string;
  readonly description?: string;
  readonly children: ReactNode;
}

/**
 * A right-docked side sheet, 400px wide (§6.6).
 *
 * A panel rather than a centred modal so the screen behind stays visible — the
 * point of uploading is usually to add to something you are already looking at.
 * It is still built on the dialog primitive, because it is modal in behaviour:
 * focus belongs inside it while it is open.
 */
export function Panel({ open, onOpenChange, title, description, children }: PanelProps) {
  return (
    <DialogPrimitive.Root open={open} onOpenChange={onOpenChange}>
      <DialogPrimitive.Portal>
        <DialogPrimitive.Overlay className="fixed inset-0 z-overlay bg-black/40 data-[state=open]:animate-overlay-in" />
        <DialogPrimitive.Content
          {...(description === undefined ? { 'aria-describedby': undefined } : {})}
          className="fixed top-0 right-0 bottom-0 z-dialog flex w-inspector max-w-full flex-col border-l bg-canvas-base shadow-elevation-2 data-[state=open]:animate-panel-in"
        >
          <div className="flex h-topbar shrink-0 items-center justify-between gap-4 border-b px-4">
            <div className="flex min-w-0 flex-col">
              <DialogPrimitive.Title className="truncate text-h2">{title}</DialogPrimitive.Title>
              {description !== undefined && (
                <DialogPrimitive.Description className="truncate text-caption text-fg-subtle">
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

          <div className="min-h-0 flex-1 overflow-auto p-4">{children}</div>
        </DialogPrimitive.Content>
      </DialogPrimitive.Portal>
    </DialogPrimitive.Root>
  );
}
