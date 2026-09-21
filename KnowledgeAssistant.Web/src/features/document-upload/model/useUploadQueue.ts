import { useQueryClient } from '@tanstack/react-query';
import { useCallback, useEffect, useRef, useState } from 'react';

import { uploadDocument } from '@/features/document-upload/api/uploadDocument';
import {
  createUploadItem,
  isActive,
  type UploadItem,
} from '@/features/document-upload/model/uploadItem';
import { isApiRequestError } from '@/shared/api/ApiRequestError';
import { documentsQueryKey } from '@/shared/api/queryKeys';

export interface UploadQueue {
  readonly items: readonly UploadItem[];
  readonly activeCount: number;
  readonly enqueue: (files: readonly File[]) => void;
  readonly cancel: (id: string) => void;
  readonly remove: (id: string) => void;
  readonly clearFinished: () => void;
}

/**
 * Uploads files one at a time and reports where each one is.
 *
 * **Sequential, not parallel.** Each request makes the server run the whole
 * ingestion pipeline synchronously — extraction, embedding, and two index
 * writes — so several at once would multiply the load on one embedding
 * deployment and turn a queue of documents into a rate limit. Uploading in
 * order also makes a failure attributable to one file rather than to a batch.
 *
 * **The queue is a ref, mirrored into state for rendering.** The pump has to
 * know what is queued *now*, synchronously, at the moment an item finishes;
 * React state is only readable as of the last render, so driving this from
 * state would mean draining the queue from an effect and cascading a render per
 * item. The ref is the source of truth and `commit` is the only writer.
 */
export function useUploadQueue(): UploadQueue {
  const queryClient = useQueryClient();
  const [items, setItems] = useState<readonly UploadItem[]>([]);

  const itemsRef = useRef<readonly UploadItem[]>([]);
  const isPumpingRef = useRef(false);
  const activeIdRef = useRef<string | null>(null);
  const controllerRef = useRef<AbortController | null>(null);

  const commit = useCallback((next: readonly UploadItem[]) => {
    itemsRef.current = next;
    setItems(next);
  }, []);

  const patch = useCallback(
    (id: string, changes: Partial<UploadItem>) => {
      commit(itemsRef.current.map((item) => (item.id === id ? { ...item, ...changes } : item)));
    },
    [commit],
  );

  const runOne = useCallback(
    async (item: UploadItem) => {
      activeIdRef.current = item.id;

      const controller = new AbortController();
      controllerRef.current = controller;

      patch(item.id, { status: 'transferring', startedAt: Date.now(), transferredBytes: 0 });

      try {
        const result = await uploadDocument({
          file: item.file,
          signal: controller.signal,
          onTransferProgress: (loaded, total) => {
            // Once the last byte is out, the browser has nothing further to
            // report — but the server has only just begun. The item moves to
            // `processing`, which shows an indeterminate bar rather than a
            // determinate one parked at 100%.
            const finishedTransferring = total !== undefined && loaded >= total;

            patch(item.id, {
              transferredBytes: loaded,
              totalBytes: total,
              status: finishedTransferring ? 'processing' : 'transferring',
            });
          },
        });

        patch(item.id, { status: 'succeeded', result });

        // The corpus now contains something it did not a moment ago, and the
        // Documents screen caches its listing. Invalidating here is what makes
        // "upload, then look at the list" show the document rather than a
        // cached answer from before it existed. The key lives in shared/ so
        // this does not become an import of another feature.
        await queryClient.invalidateQueries({ queryKey: documentsQueryKey });
      } catch (error) {
        if (controller.signal.aborted) {
          patch(item.id, { status: 'cancelled' });
        } else if (isApiRequestError(error)) {
          patch(item.id, { status: 'failed', problem: error.problem });
        } else {
          patch(item.id, { status: 'failed' });
          throw error;
        }
      } finally {
        activeIdRef.current = null;
        controllerRef.current = null;
      }
    },
    [patch, queryClient],
  );

  const pump = useCallback(async () => {
    if (isPumpingRef.current) {
      return;
    }

    isPumpingRef.current = true;

    try {
      for (;;) {
        const next = itemsRef.current.find((item) => item.status === 'queued');

        if (!next) {
          return;
        }

        await runOne(next);
      }
    } finally {
      isPumpingRef.current = false;
    }
  }, [runOne]);

  // Abort whatever is in flight if the queue itself goes away. The queue lives
  // above the panel precisely so that closing the panel does not do this.
  useEffect(
    () => () => {
      controllerRef.current?.abort();
    },
    [],
  );

  const enqueue = useCallback(
    (files: readonly File[]) => {
      commit([...itemsRef.current, ...files.map(createUploadItem)]);
      void pump();
    },
    [commit, pump],
  );

  const cancel = useCallback(
    (id: string) => {
      if (activeIdRef.current === id) {
        controllerRef.current?.abort();
        return;
      }

      patch(id, { status: 'cancelled' });
    },
    [patch],
  );

  const remove = useCallback(
    (id: string) => {
      commit(itemsRef.current.filter((item) => item.id !== id));
    },
    [commit],
  );

  const clearFinished = useCallback(() => {
    commit(itemsRef.current.filter(isActive));
  }, [commit]);

  return {
    items,
    activeCount: items.filter(isActive).length,
    enqueue,
    cancel,
    remove,
    clearFinished,
  };
}
