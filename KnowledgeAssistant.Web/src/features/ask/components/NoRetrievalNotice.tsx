import { SearchX } from 'lucide-react';

/**
 * Shown when retrieval returned nothing.
 *
 * Neutral, with no red anywhere: this is a **success**. The API is explicit
 * that retrieving nothing is not an error — it returns an honest "not in the
 * corpus", zero citations, and never calls the model at all. Styling it as a
 * failure would contradict the one place this pipeline is most careful to tell
 * the truth.
 */
export function NoRetrievalNotice() {
  return (
    <div className="flex gap-2 rounded-sm border p-3">
      <SearchX size={16} strokeWidth={1.5} className="mt-0.5 shrink-0 text-fg-subtle" aria-hidden />
      <div className="flex flex-col gap-1">
        <p className="text-h3">Nothing in the corpus covers this</p>
        <p className="text-ui text-fg-muted">
          Retrieval returned no passages, so the model was never called and no tokens were spent.
          Try a broader question, or upload a document that covers it.
        </p>
      </div>
    </div>
  );
}
