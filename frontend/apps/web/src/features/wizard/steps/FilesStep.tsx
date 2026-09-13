import { Alert, Button, LoadingState } from '@dv/ui';
import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  useApplication,
  useSaveDocumentValues,
  useUploadApplicationFile,
} from '@/entities/application/api';
import type { ApplicationDetailsDto } from '@/entities/application/types';
import { useApiErrorMessage } from '@/shared/lib/useApiError';
import { DocumentFields, validateFieldValue } from '../DocumentFields';
import { FileDropzone, formatSize, MAX_FILE_BYTES, type UploadState } from '../FileDropzone';
import { FilePreview } from '../FilePreview';

interface Props {
  applicationId: string | null;
  isSavingDraft: boolean;
  saveError: unknown;
  onRetrySave: () => void;
  onNext: () => void;
  onBack: () => void;
  onSaveDraft?: () => void;
}

/** Per-slot upload state, keyed by "{serviceLineId}:{requiredFileId}:{slotIndex}". */
type UploadStates = Record<string, { state: UploadState; error?: string }>;

const slotKey = (serviceLineId: string, requiredFileId: string, slot: number) =>
  `${serviceLineId}:${requiredFileId}:${slot}`;

/** Entered field answers, keyed by "{serviceLineId}:{fieldId}". */
type FieldValues = Record<string, string>;

const valueKey = (serviceLineId: string, fieldId: string) => `${serviceLineId}:${fieldId}`;

/**
 * The draft must exist before anything can be uploaded — files are attached to a service line,
 * and those ids only exist once the server has persisted the application. The parent saves the
 * draft on entry to this step; this component consumes the resulting id.
 */
export function FilesStep({
  applicationId,
  isSavingDraft,
  saveError,
  onRetrySave,
  onNext,
  onBack,
  onSaveDraft,
}: Props) {
  const { t } = useTranslation();
  const toMessage = useApiErrorMessage();
  const application = useApplication(applicationId ?? undefined);
  const upload = useUploadApplicationFile();
  const saveValues = useSaveDocumentValues();
  const [uploads, setUploads] = useState<UploadStates>({});
  const [values, setValues] = useState<FieldValues>({});
  const [showFieldErrors, setShowFieldErrors] = useState(false);

  // Slots the server already holds a file for are shown as done, so returning to this step after
  // a reload does not look like the uploads were lost.
  useEffect(() => {
    if (!application.data) return;

    setUploads((previous) => {
      const next = { ...previous };
      for (const requirement of application.data.requiredFiles) {
        for (let slot = 0; slot < requirement.uploadedCount; slot++) {
          const key = slotKey(requirement.applicationServiceId, requirement.requiredFileId, slot);
          if (next[key]?.state !== 'uploading') next[key] = { state: 'done' };
        }
      }
      return next;
    });
  }, [application.data]);

  // Answers already stored on the server seed the form once, without clobbering anything the
  // applicant is part-way through typing.
  useEffect(() => {
    if (!application.data) return;

    setValues((previous) => {
      const next = { ...previous };
      for (const requirement of application.data.requiredFiles) {
        for (const [fieldId, value] of Object.entries(requirement.values)) {
          const key = valueKey(requirement.applicationServiceId, fieldId);
          if (next[key] === undefined) next[key] = value;
        }
      }
      return next;
    });
  }, [application.data]);

  const details = application.data as ApplicationDetailsDto | undefined;

  const fieldProblems = useMemo(() => {
    if (!details) return [] as string[];

    return details.requiredFiles.flatMap((requirement) =>
      requirement.fields
        .filter(
          (field) =>
            validateFieldValue(
              field,
              values[valueKey(requirement.applicationServiceId, field.id)],
              t,
            ) !== null,
        )
        .map(() => requirement.name),
    );
  }, [details, values, t]);

  if (isSavingDraft) {
    return <LoadingState label={t('wizard.files.savingDraft')} />;
  }

  if (saveError) {
    return (
      <div className="space-y-4">
        <Alert variant="error" title={t('errors.genericTitle')}>
          {toMessage(saveError)}
        </Alert>
        <div className="flex flex-wrap justify-end gap-3">
          <Button type="button" variant="outline" onClick={onBack}>
            {t('wizard.previous')}
          </Button>
          <Button type="button" onClick={onRetrySave}>
            {t('common.retry')}
          </Button>
        </div>
      </div>
    );
  }

  if (!applicationId || !details || application.isPending) {
    return <LoadingState label={t('common.loading')} />;
  }

  const serviceNameOf = (serviceLineId: string) =>
    details.services.find((service) => service.id === serviceLineId)?.serviceName ?? '';

  const allMandatorySatisfied = details.requiredFiles
    .filter((requirement) => requirement.isMandatory)
    .every((requirement) => requirement.isSatisfied);

  const allFieldsValid = fieldProblems.length === 0;
  const canContinue = allMandatorySatisfied && allFieldsValid;

  function handleSelect(serviceLineId: string, requiredFileId: string, slot: number, file: File) {
    const key = slotKey(serviceLineId, requiredFileId, slot);
    setUploads((previous) => ({ ...previous, [key]: { state: 'uploading' } }));

    upload.mutate(
      {
        applicationId: applicationId!,
        applicationServiceId: serviceLineId,
        requiredFileId,
        file,
      },
      {
        onSuccess: () => setUploads((previous) => ({ ...previous, [key]: { state: 'done' } })),
        onError: (error) =>
          setUploads((previous) => ({
            ...previous,
            [key]: { state: 'error', error: toMessage(error) },
          })),
      },
    );
  }

  /**
   * Persists every answer before advancing. Values are saved in one call rather than per
   * keystroke, so a half-typed entry never reaches the server.
   */
  function handleNext() {
    setShowFieldErrors(true);
    if (!canContinue) return;

    const payload = details!.requiredFiles.flatMap((requirement) =>
      requirement.fields.map((field) => ({
        applicationServiceId: requirement.applicationServiceId,
        requiredFileFieldId: field.id,
        value: values[valueKey(requirement.applicationServiceId, field.id)] ?? '',
      })),
    );

    if (payload.length === 0) {
      onNext();
      return;
    }

    saveValues.mutate(
      { applicationId: applicationId!, values: payload },
      { onSuccess: () => onNext() },
    );
  }

  return (
    <div className="space-y-6">
      <header className="space-y-1">
        <h2 className="text-lg font-semibold">{t('wizard.files.title')}</h2>
        <p className="text-sm text-muted-foreground">
          {t('wizard.files.subtitle', { maxSize: formatSize(MAX_FILE_BYTES) })}
        </p>
      </header>

      <Alert variant="success">{t('wizard.files.draftSaved')}</Alert>

      {saveValues.isError && (
        <Alert variant="error" title={t('errors.genericTitle')}>
          {toMessage(saveValues.error)}
        </Alert>
      )}

      <div className="space-y-5">
        {details.requiredFiles.map((requirement) => {
          const documentKey = `${requirement.applicationServiceId}:${requirement.requiredFileId}`;

          // Files arrive newest-last from the server, so the nth slot shows the nth upload.
          const documentFiles = details.files
            .filter(
              (file) =>
                file.requiredFileId === requirement.requiredFileId &&
                file.applicationServiceId === requirement.applicationServiceId,
            )
            .sort((a, b) => a.uploadedAtUtc.localeCompare(b.uploadedAtUtc));

          return (
            <div key={documentKey} className="space-y-4 rounded-xl border border-border p-4">
              <div className="space-y-1">
                <div className="flex items-baseline justify-between gap-2">
                  <p className="text-sm font-medium">
                    {requirement.name}
                    {requirement.isMandatory && (
                      <span aria-hidden="true" className="ms-1 text-destructive">
                        *
                      </span>
                    )}
                  </p>
                  <p className="text-xs text-muted-foreground">
                    {t('wizard.files.forService', {
                      service: serviceNameOf(requirement.applicationServiceId),
                    })}
                  </p>
                </div>
                <p className="text-xs text-muted-foreground">
                  {t('wizard.files.uploadProgress', {
                    uploaded: documentFiles.length,
                    total: requirement.maxFiles,
                    maxSize: formatSize(requirement.maxSizeBytes),
                  })}
                </p>
              </div>

              {/* One input per file the document accepts, so "2 files allowed" reads as two
                  visible slots rather than a single box the applicant has to reuse. */}
              <div className="space-y-2">
                {Array.from({ length: requirement.maxFiles }, (_, slot) => {
                  const key = slotKey(
                    requirement.applicationServiceId,
                    requirement.requiredFileId,
                    slot,
                  );
                  const status = uploads[key];
                  const uploadedFile = documentFiles[slot];

                  return (
                    <div key={key} className="space-y-2">
                    <FileDropzone
                      testId={`dropzone-${requirement.requiredFileId}-${slot}`}
                      label={requirement.name}
                      serviceName={serviceNameOf(requirement.applicationServiceId)}
                      isMandatory={requirement.isMandatory}
                      maxBytes={requirement.maxSizeBytes}
                      acceptedExtensions={requirement.allowedExtensions}
                      showHeader={false}
                      slotLabel={
                        requirement.maxFiles > 1
                          ? t('wizard.files.fileSlot', {
                              index: slot + 1,
                              total: requirement.maxFiles,
                            })
                          : undefined
                      }
                      state={status?.state ?? (uploadedFile ? 'done' : 'idle')}
                      uploadedFileName={uploadedFile?.fileName}
                      uploadedFileSize={
                        uploadedFile ? formatSize(uploadedFile.sizeBytes) : undefined
                      }
                      errorMessage={status?.error}
                      onSelect={(file) =>
                        handleSelect(
                          requirement.applicationServiceId,
                          requirement.requiredFileId,
                          slot,
                          file,
                        )
                      }
                    />

                    {/* Shown only once the server actually holds the file, so there is
                        something to fetch. */}
                    {uploadedFile && (
                      <div className="flex justify-end">
                        <FilePreview
                          applicationId={applicationId!}
                          fileId={uploadedFile.id}
                          fileName={uploadedFile.fileName}
                          contentType={uploadedFile.contentType}
                        />
                      </div>
                    )}
                    </div>
                  );
                })}
              </div>

              <DocumentFields
                fields={requirement.fields}
                values={Object.fromEntries(
                  requirement.fields.map((field) => [
                    field.id,
                    values[valueKey(requirement.applicationServiceId, field.id)] ?? '',
                  ]),
                )}
                showErrors={showFieldErrors}
                testIdPrefix={`field-${requirement.applicationServiceId}`}
                onChange={(fieldId, value) =>
                  setValues((previous) => ({
                    ...previous,
                    [valueKey(requirement.applicationServiceId, fieldId)]: value,
                  }))
                }
              />
            </div>
          );
        })}
      </div>

      {!allMandatorySatisfied ? (
        <Alert variant="warning">{t('wizard.files.mandatoryMissing')}</Alert>
      ) : allFieldsValid ? (
        <Alert variant="success">{t('wizard.files.allUploaded')}</Alert>
      ) : (
        <Alert variant="warning">{t('wizard.files.fieldsIncomplete')}</Alert>
      )}

      <div className="flex flex-wrap justify-end gap-3">
        <Button type="button" variant="outline" onClick={onBack}>
          {t('wizard.previous')}
        </Button>
        {onSaveDraft && (
          <Button type="button" variant="outline" onClick={onSaveDraft} disabled={isSavingDraft}>
            {t('wizard.saveDraft')}
          </Button>
        )}
        <Button
          type="button"
          onClick={handleNext}
          disabled={!allMandatorySatisfied || saveValues.isPending}
        >
          {t('wizard.next')}
        </Button>
      </div>
    </div>
  );
}
