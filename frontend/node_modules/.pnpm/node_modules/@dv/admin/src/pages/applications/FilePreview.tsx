import { Dialog, LoadingState } from '@dv/ui';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { apiClient } from '@/shared/api/client';
import { useApiErrorMessage } from '@/shared/lib/useApiError';

interface PreviewFile {
  id: string;
  fileName: string;
  contentType: string;
}

/**
 * Inline preview for the two formats the platform accepts. PDFs render in an `<iframe>` and images
 * in an `<img>`. Both stream through the permission-checked admin download endpoint as an
 * authenticated blob — a plain URL cannot carry the in-memory access token. The blob's own MIME
 * type decides how it is shown, so a document is never mis-rendered because the stored content type
 * disagreed with the bytes.
 */
export function FilePreview({
  file,
  applicationId,
  onClose,
}: {
  file: PreviewFile | null;
  applicationId: string;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const toMessage = useApiErrorMessage();
  const [objectUrl, setObjectUrl] = useState<string | null>(null);
  const [blobType, setBlobType] = useState<string>('');
  // The raw thrown value is kept in state and localized during render, so the fetch effect does not
  // depend on the (per-render) message formatter and re-run itself into a loop.
  const [error, setError] = useState<unknown>(null);

  useEffect(() => {
    if (!file) {
      setObjectUrl(null);
      setError(null);
      return;
    }

    let cancelled = false;
    let url: string | null = null;

    setObjectUrl(null);
    setError(null);

    apiClient
      .getBlob(`admin/applications/${applicationId}/files/${file.id}`)
      .then((blob) => {
        if (cancelled) return;
        url = URL.createObjectURL(blob);
        setBlobType(blob.type || file.contentType);
        setObjectUrl(url);
      })
      .catch((cause) => {
        if (!cancelled) setError(cause);
      });

    return () => {
      cancelled = true;
      if (url) URL.revokeObjectURL(url);
    };
  }, [file, applicationId]);

  if (!file) return null;

  const errorMessage = error ? toMessage(error) : null;
  const isPdf = (blobType || file.contentType) === 'application/pdf';

  return (
    <Dialog
      open
      onClose={onClose}
      title={file.fileName}
      className="w-[min(56rem,calc(100vw-2rem))]"
    >
      <div className="h-[70vh] overflow-auto rounded-lg border border-border bg-muted">
        {errorMessage ? (
          <div className="space-y-3 p-6 text-center text-sm">
            <p className="text-destructive">{errorMessage}</p>
          </div>
        ) : !objectUrl ? (
          <LoadingState label={t('common.loading')} />
        ) : isPdf ? (
          <iframe src={objectUrl} title={file.fileName} className="h-full w-full border-0" />
        ) : (
          <img src={objectUrl} alt={file.fileName} className="mx-auto max-h-full" />
        )}
      </div>

      {objectUrl && !error ? (
        <div className="mt-3 flex justify-end gap-4 text-sm">
          <a
            href={objectUrl}
            target="_blank"
            rel="noopener noreferrer"
            className="text-primary hover:underline"
          >
            {t('applications.openInNewTab')}
          </a>
          <a href={objectUrl} download={file.fileName} className="text-primary hover:underline">
            {t('applications.download')}
          </a>
        </div>
      ) : null}
    </Dialog>
  );
}
