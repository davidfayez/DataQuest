import type { RequiredFileDto, RequiredFileSampleDto } from '@/features/lookups/api';
import type { UploadRequiredFileSampleInput } from '@/features/lookups/requiredFileSamples';
import type { RequiredFileInput } from './RequiredDocumentsEditor';

/**
 * A saved required document as the editor holds it. Shared by the service-type and payment-method
 * forms, which load the same shape from the API.
 */
export function toRequiredFileInput(file: RequiredFileDto): RequiredFileInput {
  return {
    id: file.id,
    nameAr: file.nameAr,
    nameEn: file.nameEn,
    isMandatory: file.isMandatory,
    maxSizeBytes: file.maxSizeBytes,
    maxFiles: file.maxFiles,
    // Already resolved by the API, so a document saved before formats were configurable arrives
    // carrying the platform default rather than an empty set.
    allowedFileTypes: [...(file.allowedFileTypes ?? [])],
    samples: file.samples ?? [],
    fields: (file.fields ?? []).map((field) => ({
      id: field.id,
      nameAr: field.nameAr,
      nameEn: field.nameEn,
      fieldType: field.fieldType,
      isRequired: field.isRequired,
      sortOrder: field.sortOrder,
      minLength: field.minLength,
      maxLength: field.maxLength,
      pattern: field.pattern,
      minValue: field.minValue,
      maxValue: field.maxValue,
      dateRule: field.dateRule,
      minDate: field.minDate,
      maxDate: field.maxDate,
      options: field.options.map((option) => ({
        value: option.value,
        labelAr: option.labelAr,
        labelEn: option.labelEn,
      })),
    })),
  };
}

/** The documents as the save endpoint takes them: reference files travel on their own endpoint. */
export function stripDocumentExtras(documents: RequiredFileInput[]): RequiredFileInput[] {
  return documents.map((document) => {
    const body = { ...document };
    delete body.samples;
    delete body.pendingSamples;
    return body;
  });
}

/**
 * Uploads the reference files chosen on documents that had no id before the save.
 *
 * The documents the save created are matched back to the form by their names — the response lists
 * documents in its own order, not the form's. Every file is attempted; the ones that did not go up
 * are reported by name so the form can say so rather than lose them quietly.
 */
export async function uploadPendingReferenceFiles(
  formDocuments: RequiredFileInput[],
  savedDocuments: RequiredFileDto[],
  upload: (input: UploadRequiredFileSampleInput) => Promise<RequiredFileSampleDto>,
  describe: (cause: unknown) => string,
): Promise<{ uploaded: Map<string, RequiredFileSampleDto[]>; failures: string[] }> {
  const knownIds = new Set(formDocuments.flatMap((document) => (document.id ? [document.id] : [])));
  const created = savedDocuments.filter((file) => !knownIds.has(file.id));
  const uploaded = new Map<string, RequiredFileSampleDto[]>();
  const failures: string[] = [];

  for (const document of formDocuments) {
    const pending = document.pendingSamples ?? [];
    if (pending.length === 0) continue;

    let target = document.id ? savedDocuments.find((file) => file.id === document.id) : undefined;
    if (!target) {
      const index = created.findIndex(
        (file) => file.nameAr === document.nameAr.trim() && file.nameEn === document.nameEn.trim(),
      );
      if (index >= 0) [target] = created.splice(index, 1);
    }

    for (const item of pending) {
      if (!target) {
        failures.push(item.file.name);
        continue;
      }

      try {
        const stored = await upload({
          requiredFileId: target.id,
          labelAr: item.labelAr,
          labelEn: item.labelEn,
          file: item.file,
        });
        uploaded.set(target.id, [...(uploaded.get(target.id) ?? []), stored]);
      } catch (cause) {
        failures.push(`${item.file.name} (${describe(cause)})`);
      }
    }
  }

  return { uploaded, failures };
}
