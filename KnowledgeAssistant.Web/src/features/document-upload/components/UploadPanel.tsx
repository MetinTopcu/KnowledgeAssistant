import { useEffect } from 'react';

import { UploadDropZone } from '@/features/document-upload/components/UploadDropZone';
import { UploadQueueItem } from '@/features/document-upload/components/UploadQueueItem';
import { isActive } from '@/features/document-upload/model/uploadItem';
import { type UploadQueue } from '@/features/document-upload/model/useUploadQueue';
import { Button } from '@/shared/components/Button';
import { Panel } from '@/shared/components/Panel';

interface UploadPanelProps {
  readonly open: boolean;
  readonly onOpenChange: (open: boolean) => void;
  readonly queue: UploadQueue;
}

export function UploadPanel({ open, onOpenChange, queue }: UploadPanelProps) {
  // A file dropped anywhere other than the zone makes the browser navigate to
  // it, discarding the queue and everything in flight. While the panel is open,
  // the window refuses drops it did not ask for.
  useEffect(() => {
    if (!open) {
      return;
    }

    const swallow = (event: Event) => {
      event.preventDefault();
    };

    window.addEventListener('dragover', swallow);
    window.addEventListener('drop', swallow);

    return () => {
      window.removeEventListener('dragover', swallow);
      window.removeEventListener('drop', swallow);
    };
  }, [open]);

  const hasFinished = queue.items.some((item) => !isActive(item));

  return (
    <Panel
      open={open}
      onOpenChange={onOpenChange}
      title="Upload documents"
      description="PDF, ingested on upload"
    >
      <div className="flex flex-col gap-4">
        <UploadDropZone onFilesSelected={queue.enqueue} />

        {queue.items.length > 0 && (
          <div className="flex flex-col gap-2">
            <div className="flex items-center justify-between gap-2">
              <h3 className="eyebrow text-fg-subtle">
                Queue · {queue.items.length}
                {queue.activeCount > 0 && ` · ${String(queue.activeCount)} active`}
              </h3>
              {hasFinished && (
                <Button variant="subtle" size="sm" onClick={queue.clearFinished}>
                  Clear finished
                </Button>
              )}
            </div>

            <ul className="flex flex-col gap-2">
              {queue.items.map((item) => (
                <UploadQueueItem
                  key={item.id}
                  item={item}
                  onCancel={queue.cancel}
                  onRemove={queue.remove}
                />
              ))}
            </ul>
          </div>
        )}
      </div>
    </Panel>
  );
}
