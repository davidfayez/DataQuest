import { Dialog, LoadingState, cn } from '@dv/ui';
import { FileText, Paperclip } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { apiClient } from '@/shared/api/client';
import { formatFileSize } from '@/shared/lib/format';

interface Props {
  applicationId: string;
  fileId: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  /** The requirement this file was uploaded against, where it is known. */
  requirementName?: string | null;
}

/**
 * One uploaded document on the review step, shown rather than named.
 *
 * The last thing before submitting is the moment to notice that page two went up twice, or that
 * the photo is of the wrong certificate — and a list of filenames cannot show that. Images render
 * as thumbnails and open full size; a PDF keeps its icon, since a browser cannot thumbnail one
 * without a rendering library and a broken image would be worse than a clear label.
 *
 * The bytes come through the API client rather than a plain `src`, because the endpoint is
 * order-scoped and the access token lives in memory.
 */
export function DocumentPreviewCard({
  applicationId,
  fileId,
  fileName,
  contentType,
  sizeBytes,
  requirementName,
}: Props) {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage ?? 'en';

  const [url, setUrl] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);
  const [open, setOpen] = useState(false);

  const isImage = contentType.startsWith('image/');

  useEffect(() => {
    if (!isImage) return undefined;

    let objectUrl: string | null = null;
    let cancelled = false;

    apiClient
      .getBlob(`applications/${applicationId}/files/${fileId}`)
      .then((blob) => {
        if (cancelled) return;
        objectUrl = URL.createObjectURL(blob);
        setUrl(objectUrl);
      })
      .catch(() => {
        // Falls back to the plain card rather than leaving a broken image where a document
        // should be.
        if (!cancelled) setFailed(true);
      });

    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [applicationId, fileId, isImage]);

  const previewable = isImage && !failed;

  return (
    <li>
      <button
        type="button"
        onClick={() => previewable && setOpen(true)}
        disabled={previewable && !url}
        aria-label={previewable ? t('wizard.review.openDocument', { name: fileName }) : undefined}
        className={cn(
          'flex w-full items-center gap-3 rounded-xl border border-border p-3 text-start transition-colors',
          previewable ? 'hover:border-primary hover:bg-muted/40' : 'cursor-default',
        )}
        data-testid={`review-file-${fileName}`}
      >
        <span className="flex size-14 shrink-0 items-center justify-center overflow-hidden rounded-lg bg-muted">
          {url ? (
            <img
              src={url}
              alt=""
              className="size-full object-cover"
              // A file can carry an image content type and still not decode — a truncated
              // upload, or bytes that only start like a PNG. Falling back to the icon beats
              // showing a broken-image glyph where a document should be.
              onError={() => setFailed(true)}
            />
          ) : previewable ? (
            <Paperclip className="size-5 animate-pulse text-muted-foreground" aria-hidden="true" />
          ) : (
            <FileText className="size-6 text-muted-foreground" aria-hidden="true" />
          )}
        </span>

        <span className="min-w-0 flex-1">
          <span className="block truncate text-sm font-medium" dir="ltr" title={fileName}>
            {fileName}
          </span>
          <span className="block text-xs text-muted-foreground">
            {requirementName ? `${requirementName} · ` : ''}
            {formatFileSize(sizeBytes, locale)}
          </span>
        </span>
      </button>

      {previewable && (
        <Dialog
          open={open}
          onClose={() => setOpen(false)}
          title={fileName}
          className="w-[min(56rem,calc(100vw-2rem))]"
        >
          {url ? (
            <img
              src={url}
              alt={fileName}
              className="mx-auto max-h-[70vh] w-auto rounded-lg object-contain"
              onError={() => setFailed(true)}
              data-testid={`review-preview-${fileName}`}
            />
          ) : (
            <LoadingState label={t('common.loading')} />
          )}
        </Dialog>
      )}
    </li>
  );
}
