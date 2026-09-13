import { Button, Dialog, LoadingState } from '@dv/ui';
import { Download, FileText, Paperclip } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  downloadWalletRequestFile,
  fetchWalletRequestFile,
  type WalletRequestFileDto,
} from '@/entities/wallet/api';
import { formatFileSize } from '@/shared/lib/format';

/**
 * One receipt uploaded with a wallet request.
 *
 * A transfer slip is nearly always a photo, and the whole point of keeping it is being able to
 * check what was sent — so an image shows as a thumbnail that opens full size, and anything else
 * stays a labelled download. The bytes go through the API client rather than a plain src, because
 * the endpoint is order-scoped and the access token lives in memory.
 */
export function WalletRequestAttachment({
  requestId,
  file,
}: {
  requestId: string;
  file: WalletRequestFileDto;
}) {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage ?? 'en';

  const [url, setUrl] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);

  const isImage = file.contentType.startsWith('image/');

  useEffect(() => {
    if (!isImage) return undefined;

    let objectUrl: string | null = null;
    let cancelled = false;

    fetchWalletRequestFile(requestId, file.id)
      .then((blob) => {
        if (cancelled) return;
        objectUrl = URL.createObjectURL(blob);
        setUrl(objectUrl);
      })
      .catch(() => {
        // Falls back to the download chip: a broken image where a receipt should be is worse
        // than a plain, working link.
        if (!cancelled) setFailed(true);
      });

    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [requestId, file.id, isImage]);

  async function download() {
    setBusy(true);
    try {
      await downloadWalletRequestFile(requestId, file);
    } finally {
      setBusy(false);
    }
  }

  if (isImage && !failed) {
    return (
      <li>
        <button
          type="button"
          onClick={() => setOpen(true)}
          disabled={!url}
          title={file.fileName}
          className="group relative block size-32 overflow-hidden rounded-xl border border-border bg-muted transition-colors hover:border-primary disabled:cursor-wait"
          data-testid={`wallet-preview-${file.fileName}`}
        >
          {url ? (
            <img
              src={url}
              alt={file.fileName}
              className="size-full object-cover"
              // A file can carry an image content type and still not decode — a truncated
              // upload, or bytes that only start like a JPEG. The chip below is the fallback.
              onError={() => setFailed(true)}
            />
          ) : (
            <span className="flex size-full items-center justify-center">
              <Paperclip className="size-4 animate-pulse text-muted-foreground" aria-hidden="true" />
            </span>
          )}

          <span className="absolute inset-x-0 bottom-0 truncate bg-black/65 px-1.5 py-1 text-[10px] text-white opacity-0 transition-opacity group-hover:opacity-100">
            {file.fileName}
          </span>
        </button>

        {/* Mounted only while open: the shared Dialog keeps its children in the DOM either way,
            so a closed one still contributed the filename as a heading. */}
        {open && (
            <Dialog
              open={open}
            onClose={() => setOpen(false)}
            title={file.fileName}
            className="w-[min(56rem,calc(100vw-2rem))]"
            footer={
              <Button variant="outline" onClick={download} disabled={busy}>
                <Download className="size-4" aria-hidden="true" />
                {t('wallet.downloadProof')}
              </Button>
            }
          >
            {url ? (
              <img
                src={url}
                alt={file.fileName}
                className="mx-auto max-h-[70vh] w-auto rounded-lg object-contain"
                onError={() => setFailed(true)}
                data-testid={`wallet-preview-full-${file.fileName}`}
              />
            ) : (
              <LoadingState label={t('common.loading')} />
            )}
          </Dialog>
        )}
      </li>
    );
  }

  return (
    <li>
      <Button
        type="button"
        variant="outline"
        size="sm"
        disabled={busy}
        onClick={download}
        data-testid={`wallet-file-${file.fileName}`}
      >
        <FileText className="size-3.5" aria-hidden="true" />
        <span className="max-w-[12rem] truncate" dir="ltr">
          {file.fileName}
        </span>
        <span className="text-muted-foreground">{formatFileSize(file.sizeBytes, locale)}</span>
      </Button>
    </li>
  );
}
