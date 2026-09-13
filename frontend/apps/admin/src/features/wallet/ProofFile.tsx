import { Button, Dialog, LoadingState } from '@dv/ui';
import { Download, Eye, FileText, Image as ImageIcon } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  downloadWalletRequestFile,
  fetchWalletRequestFile,
  isPreviewableProof,
  type WalletRequestFile,
} from '@/features/wallet/api';
import { formatFileSize } from '@/shared/lib/format';

/**
 * One receipt on the request history page, with both things a reviewer does to it.
 *
 * Preview and download are separate actions because they answer different questions: preview is
 * "is this the transfer they claim", answered in a second without leaving the page; download is
 * "I need this for the file". Opening the blob in a new tab did neither well — it left the page
 * and still gave no way to keep the document.
 *
 * The bytes are only fetched when preview is asked for, so a request with several scans does not
 * pull all of them down to render a list of names.
 */
export function ProofFile({
  requestId,
  file,
}: {
  requestId: string;
  file: WalletRequestFile;
}) {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage ?? 'en';

  const [open, setOpen] = useState(false);
  const [url, setUrl] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);
  const [busy, setBusy] = useState(false);
  const [pixels, setPixels] = useState<{ width: number; height: number } | null>(null);

  const previewable = isPreviewableProof(file.contentType);
  const isImage = file.contentType.startsWith('image/');

  useEffect(() => {
    if (!open || url) return undefined;

    let objectUrl: string | null = null;
    let cancelled = false;

    fetchWalletRequestFile(requestId, file.id)
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
  }, [open, url, requestId, file.id]);

  async function download() {
    setBusy(true);
    try {
      await downloadWalletRequestFile(requestId, file);
    } finally {
      setBusy(false);
    }
  }

  return (
    <li className="rounded-xl ring-1 ring-ink-100/80 p-3">
      <div className="flex items-start gap-2">
        <span className="mt-0.5 text-ink-400" aria-hidden="true">
          {isImage ? <ImageIcon className="size-4" /> : <FileText className="size-4" />}
        </span>

        <div className="min-w-0 flex-1">
          <p className="truncate text-sm font-medium text-ink-950" dir="ltr" title={file.fileName}>
            {file.fileName}
          </p>
          <p className="text-xs text-ink-400">{formatFileSize(file.sizeBytes, locale)}</p>
        </div>
      </div>

      <div className="mt-3 flex flex-wrap gap-2">
        {/* Offered only where the browser can render the bytes; a preview that opens an empty
            frame is worse than no button. */}
        {previewable && (
          <Button
            variant="outline"
            size="sm"
            onClick={() => setOpen(true)}
            data-testid={`preview-${file.fileName}`}
          >
            <Eye className="size-4" aria-hidden="true" />
            {t('walletRequests.preview')}
          </Button>
        )}

        <Button
          variant="outline"
          size="sm"
          disabled={busy}
          onClick={() => void download()}
          data-testid={`download-${file.fileName}`}
        >
          <Download className="size-4" aria-hidden="true" />
          {t('walletRequests.download')}
        </Button>
      </div>

      {/* Mounted only while open: the shared Dialog keeps its children in the DOM either
          way, so a closed one still contributed the filename as a heading. */}
      {previewable && open && (
        <Dialog
          open={open}
          onClose={() => setOpen(false)}
          title={file.fileName}
          className="w-[min(64rem,calc(100vw-2rem))]"
          footer={
            <Button variant="outline" onClick={() => void download()} disabled={busy}>
              <Download className="size-4" aria-hidden="true" />
              {t('walletRequests.download')}
            </Button>
          }
        >
          {failed ? (
            <p className="py-8 text-center text-sm text-ink-400">
              {t('walletRequests.previewFailed')}
            </p>
          ) : url ? (
            isImage ? (
              // Scaled to the dialog rather than drawn at native size. A receipt that happens to
              // be tiny — a placeholder, a thumbnail somebody attached by mistake — rendered as a
              // speck of a few pixels, which reads as a preview that does not work rather than as
              // a very small file. The measured size below says which it is.
              <figure className="space-y-2">
                <div className="flex items-center justify-center rounded-lg bg-cream/60 p-3 ring-1 ring-ink-100">
                  <img
                    src={url}
                    alt={file.fileName}
                    className="max-h-[65vh] w-full object-contain"
                    // A file can carry an image content type and still not decode — a truncated
                    // upload, or bytes that only start like a JPEG.
                    onError={() => setFailed(true)}
                    onLoad={(event) =>
                      setPixels({
                        width: event.currentTarget.naturalWidth,
                        height: event.currentTarget.naturalHeight,
                      })
                    }
                    data-testid={`preview-image-${file.fileName}`}
                  />
                </div>

                {pixels && (
                  <figcaption className="text-center text-xs text-ink-400">
                    {t('walletRequests.imageSize', {
                      width: pixels.width,
                      height: pixels.height,
                      size: formatFileSize(file.sizeBytes, locale),
                    })}
                  </figcaption>
                )}
              </figure>
            ) : (
              <iframe
                src={url}
                title={file.fileName}
                className="h-[70vh] w-full rounded-lg ring-1 ring-ink-100"
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
