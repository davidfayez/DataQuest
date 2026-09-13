import { Alert, Button, cn } from '@dv/ui';
import { FileText, Paperclip, Trash2 } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { TICKET_FILE_RULES } from '@/entities/ticket/api';
import { formatFileSize } from '@/shared/lib/format';

interface Props {
  files: File[];
  onChange: (files: File[]) => void;
  disabled?: boolean;
}

/**
 * Picks the files that travel with an enquiry, and shows what was picked.
 *
 * Previews come from the chosen `File` itself rather than from the server: nothing has been
 * uploaded yet at this point, and the whole purpose of a preview here is to let someone notice
 * they attached the wrong screenshot *before* they send it.
 */
export function AttachmentPicker({ files, onChange, disabled }: Props) {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage ?? 'en';
  const [rejected, setRejected] = useState<string | null>(null);

  function add(picked: FileList | null) {
    if (!picked) return;

    const incoming = [...picked];
    const kept: File[] = [];
    const problems: string[] = [];

    for (const file of incoming) {
      const extension = file.name.split('.').pop()?.toLowerCase() ?? '';

      // Checked here only to save someone a failed upload — the server sniffs the actual bytes,
      // which is the check that counts. An extension is a claim, not evidence.
      if (!TICKET_FILE_RULES.extensions.includes(extension as never)) {
        problems.push(t('contact.fileType', { name: file.name }));
        continue;
      }

      if (file.size > TICKET_FILE_RULES.maxBytes) {
        problems.push(t('contact.fileTooBig', { name: file.name }));
        continue;
      }

      kept.push(file);
    }

    const room = TICKET_FILE_RULES.maxFiles - files.length;
    if (kept.length > room) {
      problems.push(t('contact.tooManyFiles', { max: TICKET_FILE_RULES.maxFiles }));
    }

    setRejected(problems.length > 0 ? problems.join(' ') : null);
    onChange([...files, ...kept.slice(0, Math.max(room, 0))]);
  }

  const full = files.length >= TICKET_FILE_RULES.maxFiles;

  return (
    <div className="space-y-3">
      {rejected && <Alert variant="warning">{rejected}</Alert>}

      <label
        className={cn(
          'flex cursor-pointer items-center justify-center gap-2 rounded-xl border border-dashed border-border px-4 py-6 text-sm transition-colors',
          full || disabled
            ? 'cursor-not-allowed opacity-60'
            : 'hover:border-primary hover:bg-muted/40',
        )}
      >
        <Paperclip className="size-4" aria-hidden="true" />
        {full ? t('contact.attachmentsFull') : t('contact.addAttachments')}
        <input
          type="file"
          multiple
          className="sr-only"
          accept={TICKET_FILE_RULES.accept}
          disabled={disabled || full}
          onChange={(event) => {
            add(event.target.files);
            // Cleared so picking the same file again still fires a change event.
            event.target.value = '';
          }}
          data-testid="contact-files"
        />
      </label>

      <p className="text-xs text-muted-foreground">
        {t('contact.attachmentsHint', {
          max: TICKET_FILE_RULES.maxFiles,
          size: formatFileSize(TICKET_FILE_RULES.maxBytes, locale),
        })}
      </p>

      {files.length > 0 && (
        <ul className="grid gap-3 sm:grid-cols-2" data-testid="contact-file-list">
          {files.map((file, index) => (
            <AttachmentCard
              key={`${file.name}-${index}`}
              file={file}
              locale={locale}
              disabled={disabled}
              onRemove={() => onChange(files.filter((_, i) => i !== index))}
            />
          ))}
        </ul>
      )}
    </div>
  );
}

/**
 * One file chosen but not yet uploaded.
 *
 * Exported because the ticket reply composer shows the same thing for the same reason: the point
 * of a preview here is to catch the wrong screenshot before it is sent, which is too late once it
 * has been.
 */
export function AttachmentCard({
  file,
  locale,
  disabled,
  onRemove,
}: {
  file: File;
  locale: string;
  disabled?: boolean;
  onRemove: () => void;
}) {
  const { t } = useTranslation();
  const [preview, setPreview] = useState<string | null>(null);

  const isImage = file.type.startsWith('image/');

  useEffect(() => {
    if (!isImage) return undefined;

    const url = URL.createObjectURL(file);
    setPreview(url);

    // Revoked on unmount, or the page leaks a handle to every file ever picked and removed.
    return () => {
      URL.revokeObjectURL(url);
      setPreview(null);
    };
  }, [file, isImage]);

  return (
    <li className="flex items-center gap-3 rounded-xl border border-border p-3">
      <div className="flex size-14 shrink-0 items-center justify-center overflow-hidden rounded-lg bg-muted">
        {preview ? (
          <img src={preview} alt="" className="size-full object-cover" />
        ) : (
          // A PDF has no thumbnail worth generating in the browser; the icon plus the name says
          // as much as a rendered first page would at this size.
          <FileText className="size-6 text-muted-foreground" aria-hidden="true" />
        )}
      </div>

      <div className="min-w-0 flex-1">
        <p className="truncate text-sm font-medium" dir="ltr" title={file.name}>
          {file.name}
        </p>
        <p className="text-xs text-muted-foreground">{formatFileSize(file.size, locale)}</p>
      </div>

      <Button
        type="button"
        variant="ghost"
        size="sm"
        aria-label={t('contact.removeFile', { name: file.name })}
        disabled={disabled}
        onClick={onRemove}
        data-testid={`remove-file-${file.name}`}
      >
        <Trash2 className="size-4 text-destructive" aria-hidden="true" />
      </Button>
    </li>
  );
}
