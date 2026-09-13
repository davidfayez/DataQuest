import { Button, Dialog, LoadingState } from '@dv/ui';
import { Download, Eye } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { ApplicationFileDto } from '@/entities/application/types';
import { apiClient } from '@/shared/api/client';
import { formatDateTime, formatFileSize } from '@/shared/lib/format';

/**
 * Only what a row needs, so the uploaded documents and the verified results — which the API
 * describes with different shapes — can share this component.
 */
type RowFile = Pick<
  ApplicationFileDto,
  'id' | 'fileName' | 'downloadName' | 'contentType' | 'sizeBytes' | 'uploadedAtUtc'
>;

/** What a browser can render itself, given the bytes. */
function canPreview(contentType: string): boolean {
  return contentType.startsWith('image/') || contentType === 'application/pdf';
}

/**
 * One document on an application, with the two things that can be done to it.
 *
 * Both actions fetch through the API client rather than linking at the endpoint: it is behind the
 * applicant policy and the access token lives in memory, so a plain href arrives without it and
 * comes back 401.
 *
 * The bytes are fetched when preview is asked for, not on render — a list of scans would otherwise
 * pull every one of them down to show a row of filenames.
 */
export function ApplicationFileRow({
  applicationId,
  file,
}: {
  applicationId: string;
  file: RowFile;
}) {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage ?? 'en';

  const [open, setOpen] = useState(false);
  const [url, setUrl] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);
  const [busy, setBusy] = useState(false);

  const previewable = canPreview(file.contentType);
  const isImage = file.contentType.startsWith('image/');

  useEffect(() => {
    if (!open || url) return undefined;

    let objectUrl: string | null = null;
    let cancelled = false;

    apiClient
      .getBlob(`applications/${applicationId}/files/${file.id}`)
      .then((blob) => {
        if (cancelled) return;
        objectUrl = URL.createObjectURL(blob);
        setUrl(objectUrl);
      })
      .catch(() => {
        if (!cancelled) setFailed(true);
      });

    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [open, url, applicationId, file.id]);

  async function download() {
    setBusy(true);
    try {
      const blob = await apiClient.getBlob(`applications/${applicationId}/files/${file.id}`);
      const href = URL.createObjectURL(blob);

      const link = document.createElement('a');
      link.href = href;
      // Named for the application, order and document rather than whatever it was called
      // on the uploader's own computer.
      link.download = file.downloadName;
      document.body.append(link);
      link.click();
      link.remove();

      // Revoked on the next tick: doing it synchronously can beat the click in some browsers.
      setTimeout(() => URL.revokeObjectURL(href), 0);
    } finally {
      setBusy(false);
    }
  }

  return (
    <li className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-border p-3">
      <div className="min-w-0">
        <p className="truncate text-sm font-medium" dir="ltr">
          {file.fileName}
        </p>
        <p className="text-xs text-muted-foreground">
          {formatFileSize(file.sizeBytes, locale)} · {formatDateTime(file.uploadedAtUtc, locale)}
        </p>
      </div>

      <div className="flex flex-wrap gap-2">
        {/* Offered only where the browser can actually render the file; a preview that opens an
            empty box is worse than no button at all. */}
        {previewable && (
          <Button
            variant="outline"
            size="sm"
            onClick={() => setOpen(true)}
            data-testid={`preview-${file.fileName}`}
          >
            <Eye className="size-4" aria-hidden="true" />
            {t('details.preview')}
          </Button>
        )}

        <Button
          variant="outline"
          size="sm"
          disabled={busy}
          onClick={download}
          data-testid={`download-${file.fileName}`}
        >
          <Download className="size-4" aria-hidden="true" />
          {t('details.download')}
        </Button>
      </div>

      {/* Mounted only while it is open. The shared Dialog keeps its children in the DOM either
          way, so a closed one still contributed an <h2> of the filename — the name appeared twice
          on the page, once as the row and once as a heading for a dialog nobody had opened, which
          a screen reader reads out and a test cannot tell apart. */}
      {previewable && open && (
        <Dialog
          open={open}
          onClose={() => setOpen(false)}
          title={file.fileName}
          className="w-[min(64rem,calc(100vw-2rem))]"
          footer={
            <Button variant="outline" onClick={download} disabled={busy}>
              <Download className="size-4" aria-hidden="true" />
              {t('details.download')}
            </Button>
          }
        >
          {failed ? (
            <p className="py-8 text-center text-sm text-muted-foreground">
              {t('details.previewFailed')}
            </p>
          ) : url ? (
            isImage ? (
              <img
                src={url}
                alt={file.fileName}
                className="mx-auto max-h-[70vh] w-auto max-w-full rounded-lg object-contain"
                // A file can carry an image content type and still not decode — a truncated
                // upload, or bytes that only start like a PNG.
                onError={() => setFailed(true)}
                data-testid={`preview-image-${file.fileName}`}
              />
            ) : (
              <iframe
                src={url}
                title={file.fileName}
                className="h-[70vh] w-full rounded-lg border border-border"
                data-testid={`preview-pdf-${file.fileName}`}
              />
            )
          ) : (
            <LoadingState label={t('common.loading')} />
          )}
        </Dialog>
      )}
    </li>
  );
}
