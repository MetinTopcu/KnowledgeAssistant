import { judgeChunkYield } from '@/features/document-upload/model/chunkYield';
import { type UploadDocumentResponse } from '@/features/document-upload/model/uploadDocumentResponse';
import { Alert } from '@/shared/components/Alert';

interface UploadItemResultProps {
  readonly result: UploadDocumentResponse;
}

/**
 * What a successful ingestion produced.
 *
 * `chunkCount` leads because it is the only field that says whether the
 * document is actually findable. A PDF can upload, store, and index perfectly
 * while yielding nothing searchable, and every other signal in the response
 * reports success.
 */
export function UploadItemResult({ result }: UploadItemResultProps) {
  const verdict = judgeChunkYield(result);

  return (
    <div className="flex flex-col gap-2">
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

      {verdict === 'none' && (
        <Alert variant="warning" title="No searchable passages">
          The file was stored and indexed, but text extraction produced nothing, so nothing in it
          can be retrieved. This usually means the PDF is a scan and Azure Document Intelligence is
          not configured.
        </Alert>
      )}

      {verdict === 'low' && (
        <Alert variant="warning" title="Low chunk yield">
          This document produced very few passages for its size, which usually means text extraction
          only partly succeeded. Check that the answers you expect from it are retrievable.
        </Alert>
      )}
    </div>
  );
}
