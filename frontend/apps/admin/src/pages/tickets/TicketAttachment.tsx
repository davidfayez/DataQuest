import { Button, Dialog, LoadingState, cn } from '@dv/ui';
import { Download, FileText, Paperclip } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  downloadTicketFile,
  fetchTicketFile,
  isPreviewable,
  type TicketFileDto,
} from '@/features/tickets/api';
import { formatFileSize } from '@/shared/lib/format';

/**
 * One attachment on a ticket.
 *
 * An image shows as a thumbnail that opens full size; anything else stays a labelled download.
 * Support are looking at screenshots of a problem far more often than at documents, and making
 * them download each one to find out which is which is the slow way to read a ticket.
 *
 * The bytes are fetched through the API client rather than linked, because the endpoint needs the
 * bearer token and that lives in memory — a plain src would 401.
 */
export function TicketAttachment({
  ticketId,
  file,
}: {
  ticketId: string;
  file: TicketFileDto;
}) {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage ?? 'en';

  const [url, setUrl] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);

  const previewable = isPreviewable(file.contentType);

  useEffect(() => {
    if (!previewable) return undefined;

    let objectUrl: string | null = null;
    let cancelled = false;

    fetchTicketFile(ticketId, file.id)
      .then((blob) => {
        if (cancelled) return;
        objectUrl = URL.createObjectURL(blob);
        setUrl(objectUrl);
      })
      .catch(() => {
        // A thumbnail that cannot load falls back to the download chip rather than leaving a
        // broken image where an attachment should be.
        if (!cancelled) setFailed(true);
      });

    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [ticketId, file.id, previewable]);

  async function download() {
    setBusy(true);
    try {
      await downloadTicketFile(ticketId, file);
    } finally {
      setBusy(false);
    }
  }

  if (previewable && !failed) {
    return (
      <li>
        <button
          type="button"
          onClick={() => setOpen(true)}
          disabled={!url}
          title={file.fileName}
          className="group relative block size-24 overflow-hidden rounded-lg border border-border bg-ink-50 transition-colors hover:border-primary disabled:cursor-wait"
          data-testid={`preview-${file.fileName}`}
        >
          {url ? (
            <img src={url} alt={file.fileName} className="size-full object-cover" />
          ) : (
            <span className="flex size-full items-center justify-center">
              <Paperclip className="size-4 animate-pulse text-subtle" aria-hidden="true" />
            </span>
          )}

          {/* The name only on hover: a grid of thumbnails is scanned, not read. */}
          <span className="absolute inset-x-0 bottom-0 truncate bg-ink-950/70 px-1.5 py-1 text-[10px] text-white opacity-0 transition-opacity group-hover:opacity-100">
            {file.fileName}
          </span>
        </button>

        <Dialog
          open={open}
          onClose={() => setOpen(false)}
          title={file.fileName}
          className="w-[min(56rem,calc(100vw-2rem))]"
          footer={
            <Button variant="outline" onClick={download} disabled={busy}>
              <Download className="size-4" aria-hidden="true" />
              {t('common.download')}
            </Button>
          }
        >
          {url ? (
            <img
              src={url}
              alt={file.fileName}
              className="mx-auto max-h-[70vh] w-auto rounded-lg object-contain"
              data-testid={`preview-full-${file.fileName}`}
            />
          ) : (
            <LoadingState label={t('common.loading')} />
          )}
        </Dialog>
      </li>
    );
  }

  return (
    <li>
      <button
        type="button"
        onClick={download}
        disabled={busy}
        className={cn(
          'inline-flex items-center gap-2 rounded-lg border border-border px-3 py-1.5 text-xs',
          'hover:bg-ink-50 disabled:opacity-50',
        )}
        data-testid={`file-${file.fileName}`}
      >
        <FileText className="size-3.5" aria-hidden="true" />
        <span className="max-w-[12rem] truncate" dir="ltr">
          {file.fileName}
        </span>
        <span className="text-subtle">{formatFileSize(file.sizeBytes, locale)}</span>
      </button>
    </li>
  );
}

/**
 * A file chosen in the reply composer, before anything has been uploaded.
 *
 * Previewed from the local File itself — the whole point is to catch the wrong screenshot before
 * it is sent to somebody, which is too late once it has been.
 */
export function PendingAttachment({ file, onRemove }: { file: File; onRemove: () => void }) {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage ?? 'en';
  const [url, setUrl] = useState<string | null>(null);

  const isImage = file.type.startsWith('image/');

  useEffect(() => {
    if (!isImage) return undefined;

    const objectUrl = URL.createObjectURL(file);
    setUrl(objectUrl);

    // Revoked on unmount, or every file ever picked and dropped leaks a handle.
    return () => {
      URL.revokeObjectURL(objectUrl);
      setUrl(null);
    };
  }, [file, isImage]);

  return (
    <li className="flex items-center gap-2 rounded-lg border border-border p-2">
      <span className="flex size-12 shrink-0 items-center justify-center overflow-hidden rounded-md bg-ink-50">
        {url ? (
          <img src={url} alt="" className="size-full object-cover" />
        ) : (
          <FileText className="size-5 text-subtle" aria-hidden="true" />
        )}
      </span>

      <span className="min-w-0 flex-1">
        <span className="block truncate text-xs font-medium" dir="ltr" title={file.name}>
          {file.name}
        </span>
        <span className="block text-[11px] text-subtle">{formatFileSize(file.size, locale)}</span>
      </span>

      <Button
        type="button"
        variant="ghost"
        size="sm"
        aria-label={t('common.remove')}
        onClick={onRemove}
        data-testid={`pending-remove-${file.name}`}
      >
        ×
      </Button>
    </li>
  );
}
