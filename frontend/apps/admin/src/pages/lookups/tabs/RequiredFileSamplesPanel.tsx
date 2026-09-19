import { Alert, Button, Dialog, Field, Input, LoadingState } from '@dv/ui';
import { Clock, Eye, Paperclip, Plus, Trash2, Upload } from 'lucide-react';
import { useEffect, useRef, useState, type KeyboardEvent } from 'react';
import { useTranslation } from 'react-i18next';
import { adminSession, Permissions } from '@/features/auth/session';
import type { RequiredFileSampleDto } from '@/features/lookups/api';
import {
  fetchRequiredFileSample,
  useDeleteRequiredFileSample,
  useUploadRequiredFileSample,
  type ReferenceFileScope,
} from '@/features/lookups/requiredFileSamples';
import { formatFileSize } from '@/shared/lib/format';
import { useApiErrorMessage } from '@/shared/lib/useApiError';
import { useLanguage } from '@/shared/lib/useLanguage';

/** Mirrors RequiredFileSampleLimits on the server, so the panel refuses what the API would. */
export const MAX_REFERENCE_FILES = 10;
const MAX_BYTES = 5 * 1024 * 1024;
const ACCEPT = '.pdf,.jpg,.jpeg,.png,.docx,.xlsx';

/**
 * A reference file chosen on a document that has not been saved yet. It is held here and uploaded
 * once the owner's save has given the document an id to belong to.
 */
export interface PendingReferenceFile {
  key: string;
  labelAr: string;
  labelEn: string;
  file: File;
}

/** What each owner's editor may do, and what it says about files waiting for the save. */
const SCOPE_RULES: Record<
  ReferenceFileScope,
  { update: string; create: string; pendingHint: string }
> = {
  serviceType: {
    update: Permissions.ServiceTypesUpdate,
    create: Permissions.ServiceTypesCreate,
    pendingHint: 'lookups.referenceDocsPending',
  },
  paymentMethod: {
    update: Permissions.PaymentMethodsUpdate,
    create: Permissions.PaymentMethodsCreate,
    pendingHint: 'payments.referenceDocsPending',
  },
};

/** Enter in a label box would otherwise submit the whole form around this panel. */
function keepEnter(event: KeyboardEvent<HTMLInputElement>) {
  if (event.key === 'Enter') event.preventDefault();
}

interface Props {
  /** Absent until the document has been saved; files chosen before then wait in `pending`. */
  requiredFileId?: string;
  samples: RequiredFileSampleDto[];
  onChange: (samples: RequiredFileSampleDto[]) => void;
  pending: PendingReferenceFile[];
  onPendingChange: (pending: PendingReferenceFile[]) => void;
  testIdSuffix: string | number;
  /** Whose documents these are: decides the endpoints and the permissions. */
  scope?: ReferenceFileScope;
}

/** What the preview needs, whether the file is stored or still only on this machine. */
interface PreviewTarget {
  fileName: string;
  contentType: string;
  load: () => Promise<Blob>;
}

/**
 * The labelled reference files on one required document — an example of what to upload, or a form
 * to fill in — which applicants can preview beside the upload.
 *
 * On a saved document a file is stored as soon as it is added. On a new one it waits in the form
 * and is uploaded straight after the owner is saved, so a service type or a payment method can be
 * created with its reference files in one go.
 */
export function RequiredFileSamplesPanel({
  requiredFileId,
  samples,
  onChange,
  pending,
  onPendingChange,
  testIdSuffix,
  scope = 'serviceType',
}: Props) {
  const { t } = useTranslation();
  const language = useLanguage();
  const toMessage = useApiErrorMessage();
  const upload = useUploadRequiredFileSample(scope);
  const remove = useDeleteRequiredFileSample(scope);
  const rules = SCOPE_RULES[scope];
  const canEdit =
    adminSession.has(rules.update) || (!requiredFileId && adminSession.has(rules.create));

  const inputRef = useRef<HTMLInputElement>(null);
  const [labelAr, setLabelAr] = useState('');
  const [labelEn, setLabelEn] = useState('');
  const [file, setFile] = useState<File | null>(null);
  const [localError, setLocalError] = useState<string | null>(null);
  const [preview, setPreview] = useState<PreviewTarget | null>(null);

  const isSaved = Boolean(requiredFileId);
  const atLimit = samples.length + pending.length >= MAX_REFERENCE_FILES;
  const canAdd = Boolean(file) && labelAr.trim() !== '' && labelEn.trim() !== '';

  function choose(next: File | undefined) {
    setLocalError(null);
    if (!next) return;

    // Checked here as well as on the server, so an obviously wrong file is refused before it is
    // carried across the network.
    if (next.size > MAX_BYTES) {
      setLocalError(t('lookups.referenceDocTooLarge'));
      return;
    }

    setFile(next);
  }

  function clearInputs() {
    setLabelAr('');
    setLabelEn('');
    setFile(null);
  }

  function add() {
    if (!file || !canAdd) return;
    setLocalError(null);

    if (!requiredFileId) {
      onPendingChange([
        ...pending,
        {
          key: `${Date.now()}-${Math.random().toString(36).slice(2)}`,
          labelAr: labelAr.trim(),
          labelEn: labelEn.trim(),
          file,
        },
      ]);
      clearInputs();
      return;
    }

    upload.mutate(
      { requiredFileId, labelAr, labelEn, file },
      {
        onSuccess: (created) => {
          onChange([...samples, created]);
          clearInputs();
        },
      },
    );
  }

  function removeSample(sample: RequiredFileSampleDto) {
    remove.mutate(sample.id, {
      onSuccess: () => onChange(samples.filter((item) => item.id !== sample.id)),
    });
  }

  return (
    <div className="space-y-3 rounded-lg bg-muted/40 p-3" data-testid={`samples-${testIdSuffix}`}>
      <div>
        <p className="text-sm font-medium">{t('lookups.referenceDocs')}</p>
        <p className="text-xs text-muted-foreground">{t('lookups.referenceDocsHint')}</p>
      </div>

      {!isSaved && pending.length > 0 && <Alert variant="info">{t(rules.pendingHint)}</Alert>}

      {samples.length === 0 && pending.length === 0 ? (
        <p className="text-xs text-subtle">{t('lookups.referenceDocsEmpty')}</p>
      ) : (
        <ul className="space-y-1.5">
          {samples.map((sample) => (
            <li
              key={sample.id}
              className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-border bg-white px-3 py-2"
              data-testid={`sample-${sample.id}`}
            >
              <FileSummary
                label={language === 'ar' ? sample.labelAr : sample.labelEn}
                fileName={sample.fileName}
                size={formatFileSize(sample.sizeBytes, language)}
              />
              <span className="flex gap-1">
                <PreviewButton
                  testId={`sample-preview-${sample.id}`}
                  onClick={() =>
                    setPreview({
                      fileName: sample.fileName,
                      contentType: sample.contentType,
                      load: () => fetchRequiredFileSample(sample.id, scope),
                    })
                  }
                />
                {canEdit && (
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    aria-label={t('lookups.referenceDocRemove')}
                    disabled={remove.isPending}
                    onClick={() => removeSample(sample)}
                    data-testid={`sample-remove-${sample.id}`}
                  >
                    <Trash2 className="size-4" aria-hidden="true" />
                  </Button>
                )}
              </span>
            </li>
          ))}

          {pending.map((item) => (
            <li
              key={item.key}
              className="flex flex-wrap items-center justify-between gap-2 rounded-md border border-dashed border-brand-300 bg-white px-3 py-2"
              data-testid={`sample-pending-${testIdSuffix}`}
            >
              <FileSummary
                label={language === 'ar' ? item.labelAr : item.labelEn}
                fileName={item.file.name}
                size={formatFileSize(item.file.size, language)}
                badge={t('lookups.referenceDocPendingBadge')}
              />
              <span className="flex gap-1">
                <PreviewButton
                  testId={`sample-pending-preview-${item.key}`}
                  onClick={() =>
                    setPreview({
                      fileName: item.file.name,
                      contentType: item.file.type,
                      load: () => Promise.resolve(item.file),
                    })
                  }
                />
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  aria-label={t('lookups.referenceDocRemove')}
                  onClick={() => onPendingChange(pending.filter((entry) => entry.key !== item.key))}
                >
                  <Trash2 className="size-4" aria-hidden="true" />
                </Button>
              </span>
            </li>
          ))}
        </ul>
      )}

      {canEdit && atLimit && <Alert variant="info">{t('lookups.referenceDocLimit')}</Alert>}

      {canEdit && !atLimit && (
        <div className="grid items-end gap-2 rounded-lg border border-dashed border-border bg-white p-3 sm:grid-cols-[1fr_1fr_auto_auto]">
          <Field label={t('lookups.referenceDocLabelAr')} htmlFor={`sample-label-ar-${testIdSuffix}`}>
            <Input
              id={`sample-label-ar-${testIdSuffix}`}
              dir="rtl"
              maxLength={200}
              value={labelAr}
              onKeyDown={keepEnter}
              onChange={(event) => setLabelAr(event.target.value)}
              data-testid={`sample-label-ar-${testIdSuffix}`}
            />
          </Field>
          <Field label={t('lookups.referenceDocLabelEn')} htmlFor={`sample-label-en-${testIdSuffix}`}>
            <Input
              id={`sample-label-en-${testIdSuffix}`}
              dir="ltr"
              maxLength={200}
              value={labelEn}
              onKeyDown={keepEnter}
              onChange={(event) => setLabelEn(event.target.value)}
              data-testid={`sample-label-en-${testIdSuffix}`}
            />
          </Field>

          <input
            ref={inputRef}
            type="file"
            accept={ACCEPT}
            className="hidden"
            data-testid={`sample-file-${testIdSuffix}`}
            onChange={(event) => {
              const next = event.target.files?.[0];
              event.target.value = '';
              choose(next);
            }}
          />
          <Button
            type="button"
            variant="outline"
            className="max-w-56"
            onClick={() => inputRef.current?.click()}
          >
            <Paperclip className="size-4 shrink-0" aria-hidden="true" />
            <span className="truncate">{file ? file.name : t('lookups.referenceDocChooseFile')}</span>
          </Button>
          <Button
            type="button"
            disabled={!canAdd || upload.isPending}
            onClick={add}
            data-testid={`sample-upload-${testIdSuffix}`}
          >
            {isSaved ? (
              <Upload className="size-4" aria-hidden="true" />
            ) : (
              <Plus className="size-4" aria-hidden="true" />
            )}
            {isSaved ? t('lookups.referenceDocAdd') : t('lookups.referenceDocQueue')}
          </Button>
        </div>
      )}

      {localError && <Alert variant="error">{localError}</Alert>}
      {upload.isError && <Alert variant="error">{toMessage(upload.error)}</Alert>}
      {remove.isError && <Alert variant="error">{toMessage(remove.error)}</Alert>}

      {preview && <FilePreviewDialog target={preview} onClose={() => setPreview(null)} />}
    </div>
  );
}

function FileSummary({
  label,
  fileName,
  size,
  badge,
}: {
  label: string;
  fileName: string;
  size: string;
  badge?: string;
}) {
  return (
    <span className="min-w-0 text-sm">
      <span className="flex items-center gap-2">
        <span className="truncate font-medium">{label}</span>
        {badge && (
          <span className="inline-flex shrink-0 items-center gap-1 rounded bg-brand-50 px-1.5 py-0.5 text-[10px] font-semibold text-brand-700">
            <Clock className="size-3" aria-hidden="true" />
            {badge}
          </span>
        )}
      </span>
      <span className="block truncate text-xs text-muted-foreground" dir="ltr">
        {fileName} · {size}
      </span>
    </span>
  );
}

function PreviewButton({ testId, onClick }: { testId: string; onClick: () => void }) {
  const { t } = useTranslation();

  return (
    <Button type="button" variant="outline" size="sm" onClick={onClick} data-testid={testId}>
      <Eye className="size-4" aria-hidden="true" />
      {t('lookups.referenceDocPreview')}
    </Button>
  );
}

/** Shows one file in place: PDFs in a frame, images as images, the rest as a download. */
function FilePreviewDialog({ target, onClose }: { target: PreviewTarget; onClose: () => void }) {
  const { t } = useTranslation();
  const toMessage = useApiErrorMessage();
  const [objectUrl, setObjectUrl] = useState<string | null>(null);
  // The raw thrown value is kept and localized during render, so the load effect does not depend
  // on the per-render message formatter and re-run itself.
  const [error, setError] = useState<unknown>(null);

  useEffect(() => {
    let cancelled = false;
    let url: string | null = null;

    target
      .load()
      .then((blob) => {
        if (cancelled) return;
        // The known content type is authoritative; a fetched blob's may be empty.
        url = URL.createObjectURL(
          target.contentType ? new Blob([blob], { type: target.contentType }) : blob,
        );
        setObjectUrl(url);
      })
      .catch((cause: unknown) => {
        if (!cancelled) setError(cause);
      });

    return () => {
      cancelled = true;
      if (url) URL.revokeObjectURL(url);
    };
  }, [target]);

  const isImage = target.contentType.startsWith('image/');
  const isPdf = target.contentType === 'application/pdf';

  return (
    <Dialog
      open
      onClose={onClose}
      title={target.fileName}
      className="w-[min(56rem,calc(100vw-2rem))]"
      footer={
        <div className="flex flex-wrap justify-end gap-3">
          {objectUrl && (
            <a
              href={objectUrl}
              download={target.fileName}
              className="inline-flex h-10 items-center justify-center rounded-lg border border-border px-4 text-sm font-medium hover:bg-muted"
            >
              {t('lookups.referenceDocDownload')}
            </a>
          )}
          <Button type="button" variant="outline" onClick={onClose}>
            {t('common.close')}
          </Button>
        </div>
      }
    >
      <div className="h-[70vh] overflow-auto rounded-lg border border-border bg-muted">
        {error ? (
          <p className="p-6 text-center text-sm text-destructive">{toMessage(error)}</p>
        ) : !objectUrl ? (
          <LoadingState label={t('common.loading')} />
        ) : isPdf ? (
          <iframe src={objectUrl} title={target.fileName} className="h-full w-full border-0" />
        ) : isImage ? (
          <img src={objectUrl} alt={target.fileName} className="mx-auto max-h-full" />
        ) : (
          <div className="p-6">
            <Alert variant="info">{t('lookups.referenceDocNoPreview')}</Alert>
          </div>
        )}
      </div>
    </Dialog>
  );
}
