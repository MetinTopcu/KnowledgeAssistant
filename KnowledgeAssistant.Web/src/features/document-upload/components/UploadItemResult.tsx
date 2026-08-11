import { type UploadDocumentResponse } from '@/features/document-upload/model/uploadDocumentResponse';

interface UploadItemResultProps {
  readonly result: UploadDocumentResponse;
}

/**
 * What a successful ingestion produced, as the server reported it.
 *
 * `chunkCount` leads because it is the field that says whether the document is
 * actually findable — a PDF can store and index perfectly while yielding
 * nothing searchable. It is shown as a number and left uninterpreted: judging
 * whether a count is *too low* for a given file needs a threshold the API does
 * not define, and a client-invented one is a guess wearing the same typography
 * as a fact.
 */
export function UploadItemResult({ result }: UploadItemResultProps) {
  return (
    <dl className="flex flex-col gap-1 text-mono-ui text-fg-muted">
      <div className="flex gap-2">
        <dt className="w-16 shrink-0">chunks</dt>
        <dd className="text-fg-default">{result.chunkCount}</dd>
      </div>
      <div className="flex gap-2">
        <dt className="w-16 shrink-0">id</dt>
        <dd className="break-all text-fg-muted select-all">{result.documentId}</dd>
      </div>
    </dl>
  );
}
