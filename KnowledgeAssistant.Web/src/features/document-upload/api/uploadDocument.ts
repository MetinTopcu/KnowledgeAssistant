import {
  uploadDocumentResponseSchema,
  type UploadDocumentResponse,
} from '@/features/document-upload/model/uploadDocumentResponse';
import { httpClient } from '@/shared/api/httpClient';

/** The multipart field name the server binds to. */
const FILE_FIELD = 'file';

export interface UploadDocumentOptions {
  readonly file: File;
  readonly signal: AbortSignal;
  /** Called as bytes leave the browser. `total` is absent if the browser cannot say. */
  readonly onTransferProgress: (loaded: number, total: number | undefined) => void;
}

/**
 * Uploads one PDF.
 *
 * No `Content-Type` is set: Axios derives `multipart/form-data` from the
 * FormData body and appends the boundary. Setting it by hand produces a header
 * without a boundary, which the server cannot parse.
 *
 * There is no client-side check of size or extension. The server owns those
 * rules — `UploadDocumentCommandValidator` — and a copy here would be a second
 * definition free to drift from the first.
 */
export async function uploadDocument({
  file,
  signal,
  onTransferProgress,
}: UploadDocumentOptions): Promise<UploadDocumentResponse> {
  const formData = new FormData();
  formData.append(FILE_FIELD, file);

  const response = await httpClient.post('/api/documents', formData, {
    signal,
    onUploadProgress: (event) => {
      onTransferProgress(event.loaded, event.total);
    },
  });

  return uploadDocumentResponseSchema.parse(response.data);
}
