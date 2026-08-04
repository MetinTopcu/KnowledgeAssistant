import { type UploadItem } from '@/features/document-upload/model/uploadItem';
import { ProgressBar } from '@/shared/components/ProgressBar';
import { useElapsedSeconds } from '@/shared/hooks/useElapsedSeconds';
import { formatBytes } from '@/shared/lib/formatBytes';

interface UploadItemProgressProps {
  readonly item: UploadItem;
}

/**
 * The two running states, which are deliberately shown differently.
 *
 * Transfer is measured: the bar is determinate and the figure beside it is the
 * byte count Axios reported. Server-side ingestion is not measured by anything
 * — the request returns only when extraction, embedding, and both index writes
 * have finished — so it gets an indeterminate bar and an elapsed clock. No
 * stage list advances on a timer here, because that would be a picture of
 * progress rather than a report of it.
 */
export function UploadItemProgress({ item }: UploadItemProgressProps) {
  const isTransferring = item.status === 'transferring';
  const elapsed = useElapsedSeconds(item.startedAt, !isTransferring);

  if (isTransferring) {
    const total = item.totalBytes;
    const percent =
      total !== undefined && total > 0 ? Math.round((item.transferredBytes / total) * 100) : null;

    return (
      <div className="flex flex-col gap-1.5">
        <ProgressBar
          value={item.transferredBytes}
          max={total}
          label={`Uploading ${item.file.name}`}
        />
        <p className="font-mono text-mono-ui text-fg-subtle">
          {formatBytes(item.transferredBytes)}
          {total !== undefined && ` of ${formatBytes(total)}`}
          {percent !== null && ` · ${String(percent)}%`}
        </p>
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-1.5">
      <ProgressBar label={`Indexing ${item.file.name}`} />
      <p className="font-mono text-mono-ui text-fg-subtle">
        Extracting, embedding and indexing · {elapsed.toFixed(1)}s
      </p>
    </div>
  );
}
