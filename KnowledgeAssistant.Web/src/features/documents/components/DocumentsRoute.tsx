import { FileText, PlugZap } from 'lucide-react';

import { DocumentsTable } from '@/features/documents/components/DocumentsTable';
import { isCapabilityUnavailable, useDocuments } from '@/features/documents/model/useDocuments';
import { Alert } from '@/shared/components/Alert';
import { EmptyState } from '@/shared/components/EmptyState';
import { ProblemDetailsView } from '@/shared/components/ProblemDetailsView';
import { Skeleton } from '@/shared/components/Skeleton';

/** Placeholder rows while the corpus loads. Width varies so it is not a barcode. */
const SKELETON_WIDTHS = ['w-3/5', 'w-2/5', 'w-4/5', 'w-1/2', 'w-3/4'];

/**
 * The Documents screen.
 *
 * It asks the API for the corpus and reports what it gets back. Today that is a
 * 405, because `api/documents` is routed for `POST` only — so the screen says
 * exactly that, in the empty state, rather than showing an invented list or a
 * misleading "no documents yet". Nothing here is stubbed: when the endpoint
 * gains a `GET`, the table below starts rendering real rows with no change to
 * this file.
 */
export function DocumentsRoute() {
  const query = useDocuments();

  return (
    <div className="flex flex-col gap-4 p-6">
      {query.isPending && (
        <div className="flex flex-col gap-2 rounded-sm border p-3" aria-busy>
          {SKELETON_WIDTHS.map((width) => (
            <Skeleton key={width} className={`h-4 ${width}`} />
          ))}
        </div>
      )}

      {query.isError && isCapabilityUnavailable(query.error) && (
        <EmptyState
          icon={PlugZap}
          title="The document list is not available yet"
          description="This API can ingest documents but cannot yet list them: GET /api/documents is not implemented. Uploads still work and are indexed — they simply cannot be shown here until the endpoint exists."
        />
      )}

      {query.isError && !isCapabilityUnavailable(query.error) && (
        <Alert variant="danger" title="The corpus could not be loaded">
          <ProblemDetailsView problem={query.error.problem} />
        </Alert>
      )}

      {query.isSuccess && query.data.length === 0 && (
        <EmptyState
          icon={FileText}
          title="The corpus is empty"
          description="Nothing has been ingested yet. Use Upload in the top bar to add a PDF; it becomes searchable as soon as it finishes indexing."
        />
      )}

      {query.isSuccess && query.data.length > 0 && (
        <section className="flex flex-col gap-2">
          <h2 className="eyebrow text-fg-subtle">Corpus · {query.data.length} documents</h2>
          <DocumentsTable documents={query.data} />
        </section>
      )}
    </div>
  );
}
