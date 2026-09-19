import { Button } from '@dv/ui';
import { Paperclip, X } from 'lucide-react';
import { useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { PaymentMethodDocumentDto } from '@/entities/wallet/api';
import { DocumentFields, validateFieldValue } from '@/features/wizard/DocumentFields';
import { formatSize } from '@/features/wizard/FileDropzone';
import { FilePreview } from '@/features/wizard/FilePreview';

/** Formats the platform takes when a document names none of its own; the server falls back alike. */
const DEFAULT_EXTENSIONS = ['.pdf', '.jpg', '.jpeg', '.png'];

type Translate = (key: string, options?: Record<string, unknown>) => string;

const extensionsOf = (document: PaymentMethodDocumentDto) =>
  document.allowedExtensions.length > 0 ? document.allowedExtensions : DEFAULT_EXTENSIONS;

/**
 * What is still missing on the method's documents, one line per document, for the dialog's
 * "still needed" list. Mirrors the server's checks so the applicant is never turned away at submit.
 */
export function missingDocumentItems(
  documents: PaymentMethodDocumentDto[],
  files: Record<string, File[]>,
  values: Record<string, string>,
  t: Translate,
): string[] {
  const outstanding: string[] = [];

  for (const document of documents) {
    if (document.isMandatory && (files[document.id]?.length ?? 0) === 0) {
      outstanding.push(t('wallet.needDocument', { name: document.name }));
    }

    if (document.fields.some((field) => validateFieldValue(field, values[field.id], t) !== null)) {
      outstanding.push(t('wallet.needDetails', { name: document.name }));
    }
  }

  return outstanding;
}

interface Props {
  documents: PaymentMethodDocumentDto[];
  files: Record<string, File[]>;
  values: Record<string, string>;
  onFilesChange: (files: Record<string, File[]>) => void;
  onValuesChange: (values: Record<string, string>) => void;
}

/**
 * The documents the chosen payment method asks for: an administrator's reference files to look at,
 * the files themselves within each document's own limits, and the details typed beside them.
 */
export function DepositDocuments({
  documents,
  files,
  values,
  onFilesChange,
  onValuesChange,
}: Props) {
  const { t } = useTranslation();

  if (documents.length === 0) return null;

  return (
    <section className="space-y-3" data-testid="deposit-documents">
      <div>
        <p className="text-sm font-medium">{t('wallet.documentsTitle')}</p>
        <p className="text-xs text-muted-foreground">{t('wallet.documentsHint')}</p>
      </div>

      {documents.map((document) => (
        <DocumentCard
          key={document.id}
          document={document}
          files={files[document.id] ?? []}
          values={values}
          onFilesChange={(next) => onFilesChange({ ...files, [document.id]: next })}
          onValueChange={(fieldId, value) => onValuesChange({ ...values, [fieldId]: value })}
        />
      ))}
    </section>
  );
}

function DocumentCard({
  document,
  files,
  values,
  onFilesChange,
  onValueChange,
}: {
  document: PaymentMethodDocumentDto;
  files: File[];
  values: Record<string, string>;
  onFilesChange: (files: File[]) => void;
  onValueChange: (fieldId: string, value: string) => void;
}) {
  const { t } = useTranslation();
  const inputRef = useRef<HTMLInputElement>(null);
  const [error, setError] = useState<string | null>(null);

  const accepted = extensionsOf(document);
  const isFull = files.length >= document.maxFiles;
  // Errors appear once the applicant has started on this document, not before they have read it.
  const touched = files.length > 0 || document.fields.some((field) => (values[field.id] ?? '') !== '');

  function pick(picked: File[]) {
    setError(null);
    const kept: File[] = [];

    for (const file of picked) {
      const extension = file.name.slice(file.name.lastIndexOf('.')).toLowerCase();
      if (!accepted.includes(extension)) {
        setError(t('wallet.documentWrongType', { name: file.name }));
        continue;
      }
      if (file.size > document.maxSizeBytes) {
        setError(
          t('wallet.documentTooLarge', { name: file.name, size: formatSize(document.maxSizeBytes) }),
        );
        continue;
      }
      kept.push(file);
    }

    onFilesChange([...files, ...kept].slice(0, document.maxFiles));
  }

  return (
    <div
      className="space-y-3 rounded-xl border border-border p-3"
      data-testid={`deposit-document-${document.id}`}
    >
      <div>
        <p className="text-sm font-medium">
          {document.name}
          {document.isMandatory && <span className="ms-1 text-destructive">*</span>}
        </p>
        <p className="text-xs text-muted-foreground">
          {t('wallet.documentLimits', {
            count: document.maxFiles,
            size: formatSize(document.maxSizeBytes),
            types: accepted.join(', '),
          })}
        </p>
      </div>

      {document.samples.length > 0 && (
        <div className="space-y-2 rounded-lg bg-muted/50 p-3">
          <div>
            <p className="text-xs font-semibold">{t('wizard.files.referenceDocuments')}</p>
            <p className="text-xs text-muted-foreground">
              {t('wizard.files.referenceDocumentsHint')}
            </p>
          </div>
          <ul className="space-y-1.5">
            {document.samples.map((sample) => (
              <li
                key={sample.id}
                className="flex flex-wrap items-center justify-between gap-2 rounded-md bg-white px-3 py-2 text-sm"
              >
                <span className="min-w-0">
                  <span className="block truncate font-medium">{sample.label}</span>
                  <span className="block truncate text-xs text-muted-foreground" dir="ltr">
                    {sample.fileName} · {formatSize(sample.sizeBytes)}
                  </span>
                </span>
                <FilePreview
                  fileId={sample.id}
                  fileName={sample.fileName}
                  contentType={sample.contentType}
                  path={`required-files/samples/${sample.id}/file`}
                />
              </li>
            ))}
          </ul>
        </div>
      )}

      <input
        ref={inputRef}
        type="file"
        multiple={document.maxFiles > 1}
        accept={accepted.join(',')}
        className="hidden"
        data-testid={`deposit-document-input-${document.id}`}
        onChange={(event) => {
          const picked = Array.from(event.target.files ?? []);
          event.target.value = '';
          pick(picked);
        }}
      />

      <Button
        type="button"
        variant="outline"
        size="sm"
        disabled={isFull}
        onClick={() => inputRef.current?.click()}
      >
        <Paperclip className="size-4" aria-hidden="true" />
        {t('wallet.chooseFiles')}
      </Button>

      {files.length > 0 && (
        <ul className="space-y-1">
          {files.map((file, index) => (
            <li
              key={`${file.name}-${index}`}
              className="flex items-center justify-between gap-2 rounded-lg bg-muted px-3 py-1.5 text-sm"
            >
              <span className="min-w-0 truncate">
                {file.name}
                <span className="ms-2 text-xs text-muted-foreground">{formatSize(file.size)}</span>
              </span>
              <button
                type="button"
                aria-label={t('common.remove')}
                onClick={() => onFilesChange(files.filter((_, i) => i !== index))}
                className="shrink-0 rounded p-0.5 text-muted-foreground hover:text-destructive"
              >
                <X className="size-4" aria-hidden="true" />
              </button>
            </li>
          ))}
        </ul>
      )}

      {error && <p className="text-xs text-destructive">{error}</p>}

      <DocumentFields
        fields={document.fields}
        values={values}
        showErrors={touched}
        onChange={onValueChange}
        testIdPrefix={`deposit-field-${document.id}`}
      />
    </div>
  );
}
