import { Upload } from 'lucide-react';
import { useState } from 'react';

import { UploadPanel } from '@/features/document-upload/components/UploadPanel';
import { useUploadQueue } from '@/features/document-upload/model/useUploadQueue';
import { Button } from '@/shared/components/Button';

/**
 * The upload entry point, and the owner of the queue.
 *
 * The queue lives here rather than inside the panel on purpose: the panel
 * unmounts when it closes, and an upload should survive the user closing it to
 * go and read something. Because the state is up here, the button can also
 * report what is still running once the panel is gone.
 */
export function UploadAction() {
  const [open, setOpen] = useState(false);
  const queue = useUploadQueue();

  return (
    <>
      <Button
        variant="default"
        size="sm"
        onClick={() => {
          setOpen(true);
        }}
      >
        <Upload size={16} strokeWidth={1.5} aria-hidden />
        <span>{queue.activeCount > 0 ? `Uploading ${String(queue.activeCount)}…` : 'Upload'}</span>
      </Button>

      <UploadPanel open={open} onOpenChange={setOpen} queue={queue} />
    </>
  );
}
