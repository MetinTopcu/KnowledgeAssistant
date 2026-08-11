import { Upload } from 'lucide-react';
import { useRef, useState, type DragEvent } from 'react';

import { cn } from '@/shared/lib/cn';

/**
 * Filters the file picker's default view. Not validation: the user can still
 * switch it to "All files", and the server remains the only thing that decides
 * what is acceptable.
 *
 * Both forms are listed because engines disagree on which they honour — some
 * match the MIME type, some the extension.
 */
const PICKER_HINT = 'application/pdf,.pdf';

/**
 * What the server accepts, stated so a user learns the limits before spending
 * time uploading rather than after.
 *
 * These are **display copy mirroring the server's rules, not a second
 * implementation of them**. Nothing here rejects a file: an oversized or
 * non-PDF upload is still sent and still refused by
 * `UploadDocumentCommandValidator`, which remains the only authority. If those
 * constants change server-side, this text goes stale — which is a wrong label,
 * not a wrong behaviour.
 */
const ACCEPTED_DESCRIPTION = 'PDF only · maximum 20 MB';

interface UploadDropZoneProps {
  readonly onFilesSelected: (files: readonly File[]) => void;
}

export function UploadDropZone({ onFilesSelected }: UploadDropZoneProps) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [isDragActive, setIsDragActive] = useState(false);

  // Dragging over a child fires dragleave on the parent. Counting enter and
  // leave instead of toggling on each keeps the highlight from flickering.
  const dragDepth = useRef(0);

  const handleDragEnter = (event: DragEvent<HTMLButtonElement>) => {
    event.preventDefault();
    dragDepth.current += 1;
    setIsDragActive(true);
  };

  const handleDragLeave = (event: DragEvent<HTMLButtonElement>) => {
    event.preventDefault();
    dragDepth.current -= 1;

    if (dragDepth.current <= 0) {
      dragDepth.current = 0;
      setIsDragActive(false);
    }
  };

  const handleDrop = (event: DragEvent<HTMLButtonElement>) => {
    event.preventDefault();
    dragDepth.current = 0;
    setIsDragActive(false);

    const files = Array.from(event.dataTransfer.files);
    if (files.length > 0) {
      onFilesSelected(files);
    }
  };

  return (
    <>
      {/* A button, so Enter and Space open the picker with no key handling of
          our own — the keyboard path is the same code path as the mouse. */}
      <button
        type="button"
        onClick={() => inputRef.current?.click()}
        onDragEnter={handleDragEnter}
        onDragLeave={handleDragLeave}
        onDragOver={(event) => {
          // Without this the browser refuses the drop and opens the file.
          event.preventDefault();
        }}
        onDrop={handleDrop}
        className={cn(
          'flex h-30 w-full flex-col items-center justify-center gap-2 rounded-sm border border-dashed px-4',
          'transition-colors duration-instant ease-standard',
          isDragActive
            ? 'border-accent-fg bg-accent-subtle text-accent-fg'
            : 'border-border-strong text-fg-muted hover:bg-canvas-hover hover:text-fg-default',
        )}
      >
        <Upload size={20} strokeWidth={1.5} aria-hidden />
        <span className="text-ui">
          Drop PDFs here, or <span className="text-accent-fg underline">browse</span>
        </span>
        <span className="text-caption text-fg-subtle">{ACCEPTED_DESCRIPTION}</span>
      </button>

      <input
        ref={inputRef}
        type="file"
        accept={PICKER_HINT}
        multiple
        className="sr-only"
        tabIndex={-1}
        onChange={(event) => {
          const files = Array.from(event.target.files ?? []);
          if (files.length > 0) {
            onFilesSelected(files);
          }

          // Reset, so selecting the same file twice in a row still fires.
          event.target.value = '';
        }}
      />
    </>
  );
}
