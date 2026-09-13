import { cn, Spinner } from '@dv/ui';
import { CheckCircle2, FileUp, RotateCw, TriangleAlert } from 'lucide-react';
import { useId, useRef, useState, type DragEvent } from 'react';
import { useTranslation } from 'react-i18next';

/** Mirrors the server's rules so an invalid file is rejected before it is ever uploaded. */
export const MAX_FILE_BYTES = 5 * 1024 * 1024;
/**
 * What the platform accepts when a document names nothing of its own. The server resolves the same
 * fallback, so the two agree about a document configured before formats were configurable.
 */
const DEFAULT_EXTENSIONS = ['.pdf', '.jpg', '.jpeg', '.png'];

export type UploadState = 'idle' | 'uploading' | 'done' | 'error';

/**
 * Renders a byte count for display. Used both for configured caps — which are usually whole MB or
 * KB — and for the actual size of an uploaded file, which can be arbitrarily small.
 */
export function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.max(1, Math.round(bytes / 1024))} KB`;

  const megabytes = bytes / (1024 * 1024);
  return `${Number.isInteger(megabytes) ? megabytes : megabytes.toFixed(1)} MB`;
}

interface Props {
  label: string;
  serviceName: string;
  isMandatory: boolean;
  /** Per-document cap configured by an administrator; defaults to the platform maximum. */
  maxBytes?: number;
  /**
   * The extensions this particular document accepts, chosen by an administrator. Mirrors what the
   * server enforces, so a file it would refuse is rejected here rather than after the upload.
   */
  acceptedExtensions?: string[];
  /**
   * The document's own heading. Hidden when the caller draws it once above a group of slots, so a
   * multi-file document does not repeat its title on every input.
   */
  showHeader?: boolean;
  /** e.g. "File 2 of 3" — names the individual slot within a multi-file document. */
  slotLabel?: string;
  /** Shown beside the file name once uploaded, so the applicant can confirm the right file. */
  uploadedFileSize?: string;
  state: UploadState;
  uploadedFileName?: string;
  errorMessage?: string;
  onSelect: (file: File) => void;
  testId?: string;
}

export function FileDropzone({
  label,
  serviceName,
  isMandatory,
  maxBytes = MAX_FILE_BYTES,
  acceptedExtensions,
  showHeader = true,
  slotLabel,
  uploadedFileSize,
  state,
  uploadedFileName,
  errorMessage,
  onSelect,
  testId,
}: Props) {
  const { t } = useTranslation();
  const inputId = useId();
  const inputRef = useRef<HTMLInputElement>(null);
  const [isDragging, setIsDragging] = useState(false);
  const [localError, setLocalError] = useState<string | null>(null);

  // An empty list would mean "accept nothing", which is never what is meant — the server falls
  // back the same way.
  const accepted =
    acceptedExtensions && acceptedExtensions.length > 0 ? acceptedExtensions : DEFAULT_EXTENSIONS;

  /** Rejects wrong types and oversized files up front, with a localized reason. */
  function validate(file: File): string | null {
    const extension = file.name.slice(file.name.lastIndexOf('.')).toLowerCase();

    if (!accepted.includes(extension)) {
      // Naming what is allowed, because "wrong type" alone leaves the applicant guessing which
      // of their files would work.
      return t('wizard.files.rejectedType', { types: accepted.join(', ') });
    }

    if (file.size > maxBytes) {
      return t('wizard.files.rejectedSize', { maxSize: formatSize(maxBytes) });
    }

    return null;
  }

  function handleFile(file: File | undefined) {
    if (!file) return;

    const problem = validate(file);
    setLocalError(problem);

    if (!problem) onSelect(file);
  }

  function handleDrop(event: DragEvent<HTMLDivElement>) {
    event.preventDefault();
    setIsDragging(false);
    handleFile(event.dataTransfer.files[0]);
  }

  const message = localError ?? errorMessage;

  return (
    <div className="space-y-2" data-testid={testId}>
      {showHeader && (
        <div className="flex items-baseline justify-between gap-2">
          <p className="text-sm font-medium">
            {label}
            {isMandatory && (
              <span aria-hidden="true" className="ms-1 text-destructive">
                *
              </span>
            )}
          </p>
          <p className="text-xs text-muted-foreground">
            {t('wizard.files.forService', { service: serviceName })}
          </p>
        </div>
      )}

      {/* A real <input type="file"> stays in the DOM and the wrapper only forwards clicks, so
          keyboard and assistive-technology users get the native picker unchanged. */}
      <div
        onDragOver={(event) => {
          event.preventDefault();
          setIsDragging(true);
        }}
        onDragLeave={() => setIsDragging(false)}
        onDrop={handleDrop}
        onClick={() => inputRef.current?.click()}
        className={cn(
          'flex cursor-pointer items-center gap-3 rounded-lg border border-dashed p-4 transition-colors',
          isDragging && 'border-primary bg-primary/5',
          state === 'done' && 'border-success/50 bg-success/5',
          message && 'border-destructive/50 bg-destructive/5',
          !isDragging && !message && state !== 'done' && 'border-border hover:bg-muted',
        )}
      >
        {state === 'uploading' ? (
          <Spinner className="size-5 text-primary" />
        ) : state === 'done' ? (
          <CheckCircle2 className="size-5 text-success" aria-hidden="true" />
        ) : message ? (
          <TriangleAlert className="size-5 text-destructive" aria-hidden="true" />
        ) : (
          <FileUp className="size-5 text-muted-foreground" aria-hidden="true" />
        )}

        <div className="min-w-0 flex-1">
          {state === 'uploading' && <p className="text-sm">{t('wizard.files.uploading')}</p>}
          {state === 'done' && (
            <div className="min-w-0">
              <p className="truncate text-sm font-medium">
                {uploadedFileName ?? t('wizard.files.uploaded')}
              </p>
              <p className="text-xs text-muted-foreground">
                {uploadedFileSize
                  ? `${t('wizard.files.uploaded')} · ${uploadedFileSize} · ${t('wizard.files.replaceHint')}`
                  : `${t('wizard.files.uploaded')} · ${t('wizard.files.replaceHint')}`}
              </p>
            </div>
          )}
          {state !== 'uploading' && state !== 'done' && (
            <div className="min-w-0">
              {slotLabel && <p className="text-sm font-medium">{slotLabel}</p>}
              <p className="text-sm text-muted-foreground">{t('wizard.files.dropzone')}</p>
            </div>
          )}
        </div>

        {state === 'error' && <RotateCw className="size-4 text-muted-foreground" aria-hidden="true" />}

        <label htmlFor={inputId} className="sr-only">
          {label}
        </label>
        <input
          ref={inputRef}
          id={inputId}
          type="file"
          className="sr-only"
          accept={accepted.join(',')}
          onChange={(event) => {
            handleFile(event.target.files?.[0]);
            // Reset so re-selecting the same file still raises a change event.
            event.target.value = '';
          }}
        />
      </div>

      {message && (
        <p role="alert" className="text-sm text-destructive">
          {message}
        </p>
      )}
    </div>
  );
}
