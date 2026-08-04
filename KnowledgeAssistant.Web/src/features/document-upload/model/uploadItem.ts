import { type UploadDocumentResponse } from '@/features/document-upload/model/uploadDocumentResponse';
import { type ApiProblem } from '@/shared/api/apiProblem';

/**
 * Where one file is in the pipeline.
 *
 * `transferring` and `processing` are separate states because only one of them
 * has a measurable duration. Bytes leaving the browser can be counted; what the
 * server does afterwards — extract, chunk, embed, index — reports nothing until
 * it returns. Collapsing the two would mean showing a progress figure for a
 * stage that has none.
 */
export type UploadStatus =
  'queued' | 'transferring' | 'processing' | 'succeeded' | 'failed' | 'cancelled';

export interface UploadItem {
  readonly id: string;
  readonly file: File;
  readonly status: UploadStatus;
  readonly transferredBytes: number;
  /** Absent when the browser does not report a total for this body. */
  readonly totalBytes: number | undefined;
  /** When the request was dispatched, for the elapsed readout. */
  readonly startedAt: number | null;
  readonly result: UploadDocumentResponse | null;
  readonly problem: ApiProblem | null;
}

export function createUploadItem(file: File): UploadItem {
  return {
    id: crypto.randomUUID(),
    file,
    status: 'queued',
    transferredBytes: 0,
    totalBytes: undefined,
    startedAt: null,
    result: null,
    problem: null,
  };
}

/** True while the item still needs the queue to do something with it. */
export function isActive(item: UploadItem): boolean {
  return item.status === 'queued' || item.status === 'transferring' || item.status === 'processing';
}
