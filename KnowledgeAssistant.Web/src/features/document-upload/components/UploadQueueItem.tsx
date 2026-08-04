import { CheckCircle2, FileText, MinusCircle, X, XOctagon } from 'lucide-react';

import { UploadItemProgress } from '@/features/document-upload/components/UploadItemProgress';
import { UploadItemResult } from '@/features/document-upload/components/UploadItemResult';
import { isActive, type UploadItem } from '@/features/document-upload/model/uploadItem';
import { ProblemDetailsView } from '@/shared/components/ProblemDetailsView';
import { Button } from '@/shared/components/Button';
import { formatBytes } from '@/shared/lib/formatBytes';

interface UploadQueueItemProps {
  readonly item: UploadItem;
  readonly onCancel: (id: string) => void;
  readonly onRemove: (id: string) => void;
}

const STATUS_LABELS: Record<UploadItem['status'], string> = {
  queued: 'Queued',
  transferring: 'Uploading',
  processing: 'Indexing',
  succeeded: 'Indexed',
  failed: 'Failed',
  cancelled: 'Cancelled',
};

export function UploadQueueItem({ item, onCancel, onRemove }: UploadQueueItemProps) {
  const running = isActive(item);

  return (
    <li className="flex flex-col gap-2 rounded-sm border p-3">
      <div className="flex items-start justify-between gap-2">
        <div className="flex min-w-0 items-start gap-2">
          {item.status === 'succeeded' && (
            <CheckCircle2
              size={16}
              strokeWidth={1.5}
              className="mt-0.5 shrink-0 text-success-fg"
              aria-hidden
            />
          )}
          {item.status === 'failed' && (
            <XOctagon
              size={16}
              strokeWidth={1.5}
              className="mt-0.5 shrink-0 text-danger-fg"
              aria-hidden
            />
          )}
          {item.status === 'cancelled' && (
            <MinusCircle
              size={16}
              strokeWidth={1.5}
              className="mt-0.5 shrink-0 text-fg-subtle"
              aria-hidden
            />
          )}
          {running && (
            <FileText
              size={16}
              strokeWidth={1.5}
              className="mt-0.5 shrink-0 text-fg-subtle"
              aria-hidden
            />
          )}

          <div className="flex min-w-0 flex-col">
            <span className="truncate text-ui" title={item.file.name}>
              {item.file.name}
            </span>
            <span className="font-mono text-mono-ui text-fg-subtle">
              {formatBytes(item.file.size)} · {STATUS_LABELS[item.status]}
            </span>
          </div>
        </div>

        <Button
          variant="subtle"
          size="icon"
          onClick={() => {
            if (running) {
              onCancel(item.id);
            } else {
              onRemove(item.id);
            }
          }}
          aria-label={running ? `Cancel ${item.file.name}` : `Remove ${item.file.name}`}
          title={running ? 'Cancel' : 'Remove'}
        >
          <X size={16} strokeWidth={1.5} aria-hidden />
        </Button>
      </div>

      {(item.status === 'transferring' || item.status === 'processing') && (
        <UploadItemProgress item={item} />
      )}

      {item.status === 'succeeded' && item.result !== null && (
        <UploadItemResult result={item.result} />
      )}

      {item.status === 'failed' && item.problem !== null && (
        <ProblemDetailsView problem={item.problem} />
      )}
    </li>
  );
}
