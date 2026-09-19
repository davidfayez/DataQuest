import { Alert, Button, Dialog, LoadingState } from '@dv/ui';
import { Eye } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { apiClient } from '@/shared/api/client';
import { useApiErrorMessage } from '@/shared/lib/useApiError';

interface Props {
  fileId: string;
  fileName: string;
  contentType: string;
  /** For one of the applicant's own uploads. */
  applicationId?: string;
  /** For any other file — a reference file on a document — the API path to fetch it from. */
  path?: string;
  /** The button's text; "Preview" when not given. */
  label?: string;
}

/**
 * Shows a document without leaving the wizard.
 *
 * The download endpoints need the bearer token, which lives in memory — a plain `<img src>` or
 * `<iframe src>` cannot carry it. So the bytes are fetched through the API client and shown from an
 * object URL, which is revoked as soon as the dialog closes.
 */
export function FilePreview({ applicationId, fileId, fileName, contentType, path, label }: Props) {
  const source = path ?? `applications/${applicationId}/files/${fileId}`;
  const { t } = useTranslation();
  const toMessage = useApiErrorMessage();

  const [open, setOpen] = useState(false);
  const [objectUrl, setObjectUrl] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  // Held in a ref, not read as a dependency: useApiErrorMessage returns a new function on every
  // render, and depending on it re-ran the effect mid-load — the cleanup then revoked the object
  // URL while the browser was still fetching it, which aborted the image or PDF.
  const toMessageRef = useRef(toMessage);
  toMessageRef.current = toMessage;

  useEffect(() => {
    if (!open) return;

    let cancelled = false;
    let created: string | null = null;

    setIsLoading(true);
    setError(null);

    apiClient
      .getBlob(source)
      .then((blob) => {
        if (cancelled) return;
        // The server's content type is authoritative; the blob's may be empty.
        created = URL.createObjectURL(contentType ? new Blob([blob], { type: contentType }) : blob);
        setObjectUrl(created);
      })
      .catch((caught: unknown) => {
        if (!cancelled) setError(toMessageRef.current(caught));
      })
      .finally(() => {
        if (!cancelled) setIsLoading(false);
      });

    return () => {
      cancelled = true;
      // Revoked on close so a long wizard session does not accumulate blobs in memory.
      if (created) URL.revokeObjectURL(created);
      setObjectUrl(null);
    };
  }, [open, source, contentType]);

  return (
    <>
      <Button
        type="button"
        variant="outline"
        size="sm"
        onClick={() => setOpen(true)}
        data-testid={`preview-${fileId}`}
      >
        <Eye className="size-4" aria-hidden="true" />
        {label ?? t('wizard.files.preview')}
      </Button>

      {/* Mounted only while it is open. A closed Dialog still renders its title, so every preview
          on the step left the file name in the page a second time — read out by screen readers,
          and matched by anything looking for that name. */}
      {open && (
        <Dialog
          open={open}
          onClose={() => setOpen(false)}
          title={fileName}
          className="w-[min(64rem,calc(100vw-2rem))]"
          footer={
            <div className="flex flex-wrap justify-end gap-3">
              {objectUrl && (
                <a
                  href={objectUrl}
                  download={fileName}
                  className="inline-flex h-10 items-center justify-center rounded-lg border border-border px-4 text-sm font-medium hover:bg-muted"
                >
                  {t('wizard.files.download')}
                </a>
              )}
              <Button type="button" variant="outline" onClick={() => setOpen(false)}>
                {t('common.close')}
              </Button>
            </div>
          }
        >
          {isLoading && <LoadingState label={t('common.loading')} />}

          {error && (
            <Alert variant="error" title={t('errors.genericTitle')}>
              {error}
            </Alert>
          )}

          {!isLoading && !error && objectUrl && (
            <div className="max-h-[70vh] overflow-auto rounded-lg bg-muted p-2">
              {contentType.startsWith('image/') ? (
                <img src={objectUrl} alt={fileName} className="mx-auto max-w-full rounded" />
              ) : contentType === 'application/pdf' ? (
                <iframe
                  src={objectUrl}
                  title={fileName}
                  className="h-[70vh] w-full rounded border-0"
                />
              ) : (
                // Reached only if the server ever stores a type the browser cannot render.
                <Alert variant="info">{t('wizard.files.previewUnavailable')}</Alert>
              )}
            </div>
          )}
        </Dialog>
      )}
    </>
  );
}
