import { FileText } from 'lucide-react';

import { DocumentsTable } from '@/features/documents/components/DocumentsTable';
import { useDocuments } from '@/features/documents/model/useDocuments';
import { Alert } from '@/shared/components/Alert';
import { EmptyState } from '@/shared/components/EmptyState';
import { ProblemDetailsView } from '@/shared/components/ProblemDetailsView';
import { Skeleton } from '@/shared/components/Skeleton';

/** Placeholder rows while the corpus loads. Width varies so it is not a barcode. */
const SKELETON_WIDTHS = ['w-3/5', 'w-2/5', 'w-4/5', 'w-1/2', 'w-3/4'];

/**
 * The Documents screen.
 *
 * It asks the API for the corpus and reports what it gets back — nothing here
 * is derived, judged, or stubbed. Three outcomes, three renderings: a corpus
 * with documents, a corpus with none, and a listing that failed. The middle one
 * is deliberately not an error: "nothing has been ingested" is an answer.
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

      {query.isError && (
        <Alert variant="danger" title="The corpus could not be loaded">
          <ProblemDetailsView problem={query.error.problem} />
        </Alert>
      )}

      {query.isSuccess && query.data.documents.length === 0 && (
        <EmptyState
          icon={FileText}
          title="The corpus is empty"
          description="Nothing has been ingested yet. Use Upload in the top bar to add a PDF; it becomes searchable as soon as it finishes indexing."
        />
      )}

      {query.isSuccess && query.data.documents.length > 0 && (
        <section className="flex flex-col gap-2">
          <h2 className="eyebrow text-fg-subtle">
            Corpus · {query.data.count} document{query.data.count === 1 ? '' : 's'}
            {query.data.truncated && ', newest first'}
          </h2>

          {query.data.truncated && (
            <p className="text-ui text-fg-muted">
              This is one page of the corpus. There may be more documents than are shown here —
              the API returns the most recent uploads first and does not page.
            </p>
          )}

          <DocumentsTable documents={query.data.documents} />
        </section>
      )}
    </div>
  );
}
