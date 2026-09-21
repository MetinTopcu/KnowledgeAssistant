import { ArrowDown, ArrowUp, FileText } from 'lucide-react';
import { useMemo, useState } from 'react';

import { type DocumentSummary } from '@/features/documents/model/documentSummary';

type SortKey = 'fileName' | 'blobName' | 'uploadedAtUtc';
type SortDirection = 'asc' | 'desc';

interface DocumentsTableProps {
  readonly documents: readonly DocumentSummary[];
}

function compare(left: DocumentSummary, right: DocumentSummary, key: SortKey): number {
  return left[key].localeCompare(right[key]);
}

/**
 * The corpus, as a data grid.
 *
 * Deliberately the same table idiom as the evidence table — 36px rows, a
 * `canvas.subtle` header, 1px row rules, eyebrow column labels, sortable
 * headers — so the two screens read as one product rather than as two tables
 * that happen to share a palette. No column is derived or judged: every cell is
 * a field the server sent, formatted but not interpreted.
 *
 * Size and chunk count are absent because the listing endpoint does not return
 * them: the search index never stored them. A column of dashes would be a
 * worse answer than no column, and a zero would be a false one.
 */
export function DocumentsTable({ documents }: DocumentsTableProps) {
  const [sortKey, setSortKey] = useState<SortKey>('uploadedAtUtc');
  const [direction, setDirection] = useState<SortDirection>('desc');

  const sorted = useMemo(() => {
    const factor = direction === 'asc' ? 1 : -1;
    return [...documents].sort((left, right) => compare(left, right, sortKey) * factor);
  }, [documents, sortKey, direction]);

  const toggleSort = (key: SortKey) => {
    if (key === sortKey) {
      setDirection((current) => (current === 'asc' ? 'desc' : 'asc'));
      return;
    }

    setSortKey(key);
    setDirection(key === 'fileName' ? 'asc' : 'desc');
  };

  const SortIcon = direction === 'asc' ? ArrowUp : ArrowDown;

  const renderSortableHeader = (key: SortKey, label: string) => (
    <button
      type="button"
      onClick={() => {
        toggleSort(key);
      }}
      className="flex items-center gap-1 text-fg-subtle hover:text-fg-default"
    >
      <span className="eyebrow">{label}</span>
      {sortKey === key && <SortIcon size={12} strokeWidth={1.5} aria-hidden />}
    </button>
  );

  return (
    <div className="overflow-x-auto rounded-sm border">
      <table className="w-full table-fixed border-collapse">
        <caption className="sr-only">Documents ingested into the corpus</caption>
        <thead className="bg-canvas-subtle">
          <tr className="border-b">
            <th scope="col" className="px-3 text-left">
              {renderSortableHeader('fileName', 'Document')}
            </th>
            <th scope="col" className="w-64 px-3 text-left">
              {renderSortableHeader('blobName', 'Stored as')}
            </th>
            <th scope="col" className="w-44 px-3 text-left">
              {renderSortableHeader('uploadedAtUtc', 'Ingested')}
            </th>
            <th scope="col" className="w-40 px-3 text-left text-fg-subtle">
              <span className="eyebrow">Document id</span>
            </th>
          </tr>
        </thead>
        <tbody>
          {sorted.map((document) => {
            const ingestedAt = new Date(document.uploadedAtUtc);

            return (
              <tr
                key={document.documentId}
                className="h-row-default border-b border-b-border-muted"
              >
                <td className="px-3">
                  <span className="flex min-w-0 items-center gap-2">
                    <FileText
                      size={16}
                      strokeWidth={1.5}
                      className="shrink-0 text-fg-subtle"
                      aria-hidden
                    />
                    <span className="truncate text-ui" title={document.fileName}>
                      {document.fileName}
                    </span>
                  </span>
                </td>

                <td className="px-3">
                  <span
                    className="block truncate font-mono text-mono-ui text-fg-subtle"
                    title={document.blobName}
                  >
                    {document.blobName}
                  </span>
                </td>

                <td className="px-3">
                  <time
                    dateTime={document.uploadedAtUtc}
                    className="font-mono text-mono-ui text-fg-muted"
                    title={ingestedAt.toUTCString()}
                  >
                    {ingestedAt.toLocaleString()}
                  </time>
                </td>

                <td className="px-3">
                  <span
                    className="truncate font-mono text-mono-ui text-fg-subtle select-all"
                    title={document.documentId}
                  >
                    {document.documentId}
                  </span>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
