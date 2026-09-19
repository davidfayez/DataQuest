import { Alert, Button, Field, Input, Select, cn } from '@dv/ui';
import { Plus, Trash2 } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import type { RequiredFileSampleDto } from '@/features/lookups/api';
import type { ReferenceFileScope } from '@/features/lookups/requiredFileSamples';
import { RequiredFileSamplesPanel, type PendingReferenceFile } from './RequiredFileSamplesPanel';

/** Mirrors the API's RequiredFieldType enum. Numeric so it matches the wire format exactly. */
export enum RequiredFieldType {
  Text = 0,
  Number = 1,
  Date = 2,
  Dropdown = 3,
}

/** Mirrors the API's RequiredFieldDateRule enum. */
export enum RequiredFieldDateRule {
  Any = 0,
  PastOnly = 1,
  FutureOnly = 2,
}

export interface FieldOptionInput {
  value: string;
  labelAr: string;
  labelEn: string;
}

export interface RequiredFieldInput {
  id?: string;
  nameAr: string;
  nameEn: string;
  fieldType: RequiredFieldType;
  isRequired: boolean;
  sortOrder: number;
  minLength: number | null;
  maxLength: number | null;
  pattern: string | null;
  minValue: number | null;
  maxValue: number | null;
  dateRule: RequiredFieldDateRule;
  minDate: string | null;
  maxDate: string | null;
  options: FieldOptionInput[];
}

/**
 * The upload formats a document may accept, and the extensions each stands for.
 *
 * Modern Office only: the 1990s .doc and .xls share one file signature with every other OLE
 * compound file, so accepting them would mean trusting the extension — and they are the formats
 * that carry macros. Mirrors `DocumentFileTypes` on the server.
 */
export const DOCUMENT_FILE_TYPES = [
  { code: 'pdf', extensions: '.pdf' },
  { code: 'jpg', extensions: '.jpg, .jpeg' },
  { code: 'png', extensions: '.png' },
  { code: 'word', extensions: '.docx' },
  { code: 'excel', extensions: '.xlsx' },
] as const;

/** What a document accepts when nothing is chosen, matching the server's fallback. */
const DEFAULT_FILE_TYPES = ['pdf', 'jpg', 'png'];

export interface RequiredFileInput {
  id?: string;
  nameAr: string;
  nameEn: string;
  isMandatory: boolean;
  /** Null uses the platform maximum; the server clamps anything larger. */
  maxSizeBytes: number | null;
  maxFiles: number;
  fields: RequiredFieldInput[];
  /** Codes from DOCUMENT_FILE_TYPES. Never empty — a document that accepts nothing is unusable. */
  allowedFileTypes: string[];
  /**
   * Reference files already stored on this document. Shown and managed here, but saved through
   * their own endpoints — the service-type save ignores this list.
   */
  samples?: RequiredFileSampleDto[];
  /**
   * Reference files chosen before this document was saved. Uploaded by the service-type form once
   * the save has given the document an id; never sent in the save itself.
   */
  pendingSamples?: PendingReferenceFile[];
}

export const EMPTY_DOCUMENT: RequiredFileInput = {
  nameAr: '',
  nameEn: '',
  isMandatory: true,
  maxSizeBytes: null,
  maxFiles: 1,
  fields: [],
  allowedFileTypes: [...DEFAULT_FILE_TYPES],
};

const EMPTY_FIELD: Omit<RequiredFieldInput, 'sortOrder'> = {
  nameAr: '',
  nameEn: '',
  fieldType: RequiredFieldType.Text,
  isRequired: true,
  minLength: null,
  maxLength: null,
  pattern: null,
  minValue: null,
  maxValue: null,
  dateRule: RequiredFieldDateRule.Any,
  minDate: null,
  maxDate: null,
  options: [],
};

/** Reads an optional number out of an input, treating a cleared box as "no limit". */
function toNumber(value: string): number | null {
  if (value.trim() === '') return null;
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

interface Props {
  documents: RequiredFileInput[];
  onChange: (documents: RequiredFileInput[]) => void;
  /** Whose documents these are: decides where reference files are stored and who may add them. */
  scope?: ReferenceFileScope;
  /**
   * Whether an empty list is fine. A service type must ask for something; a payment method may ask
   * for nothing, and then the list says so plainly instead of warning.
   */
  allowEmpty?: boolean;
  /** Off when the editor already sits in a panel that carries its own title. */
  showHeading?: boolean;
}

/**
 * Edits a set of required documents — a service type's or a payment method's: each one's upload
 * limits, the custom fields the applicant fills in beside it, and its reference files. Kept out of
 * the forms because the nested field builder is substantial on its own.
 */
export function RequiredDocumentsEditor({
  documents,
  onChange,
  scope = 'serviceType',
  allowEmpty = false,
  showHeading = true,
}: Props) {
  const { t } = useTranslation();

  function patchDocument(index: number, patch: Partial<RequiredFileInput>) {
    onChange(documents.map((doc, i) => (i === index ? { ...doc, ...patch } : doc)));
  }

  /**
   * Adds or removes one format. The last one cannot be removed: a document accepting nothing
   * could never be satisfied, and the applicant would be stuck on it with no way forward.
   */
  function toggleFileType(docIndex: number, code: string) {
    const current = documents[docIndex]!.allowedFileTypes;
    const next = current.includes(code)
      ? current.filter((value) => value !== code)
      : [...current, code];

    if (next.length === 0) return;

    patchDocument(docIndex, {
      // Kept in the platform's own order rather than click order, so the list reads the same
      // everywhere it is shown.
      allowedFileTypes: DOCUMENT_FILE_TYPES.map((type) => type.code).filter((value) =>
        next.includes(value),
      ),
    });
  }

  function patchField(docIndex: number, fieldIndex: number, patch: Partial<RequiredFieldInput>) {
    patchDocument(docIndex, {
      fields: documents[docIndex]!.fields.map((field, i) =>
        i === fieldIndex ? { ...field, ...patch } : field,
      ),
    });
  }

  return (
    <fieldset className="space-y-4">
      {showHeading ? (
        <>
          <legend className="text-sm font-medium">{t('lookups.requiredFiles')}</legend>
          <p className="text-xs text-muted-foreground">{t('lookups.requiredFilesHint')}</p>
        </>
      ) : (
        <legend className="sr-only">{t('lookups.requiredFiles')}</legend>
      )}

      {documents.length === 0 &&
        (allowEmpty ? (
          <p className="text-sm text-muted-foreground" data-testid="documents-empty">
            {t('payments.documentsEmpty')}
          </p>
        ) : (
          <Alert variant="warning" data-testid="documents-required">
            {t('lookups.requiredFilesEmpty')}
          </Alert>
        ))}

      {documents.map((doc, docIndex) => (
        <div key={docIndex} className="space-y-4 rounded-xl border border-border p-4">
          <div className="grid gap-2 sm:grid-cols-[1fr_1fr_auto_auto]">
            <Input
              dir="rtl"
              placeholder={t('lookups.nameAr')}
              value={doc.nameAr}
              onChange={(event) => patchDocument(docIndex, { nameAr: event.target.value })}
            />
            <Input
              dir="ltr"
              placeholder={t('lookups.nameEn')}
              value={doc.nameEn}
              onChange={(event) => patchDocument(docIndex, { nameEn: event.target.value })}
            />
            <label className="flex items-center gap-2 whitespace-nowrap text-sm">
              <input
                type="checkbox"
                className="size-4 rounded border-border"
                checked={doc.isMandatory}
                onChange={(event) => patchDocument(docIndex, { isMandatory: event.target.checked })}
              />
              {t('lookups.mandatory')}
            </label>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              aria-label={t('lookups.removeRequiredFile')}
              onClick={() => onChange(documents.filter((_, i) => i !== docIndex))}
            >
              <Trash2 className="size-4" aria-hidden="true" />
            </Button>
          </div>

          <div className="grid gap-4 sm:grid-cols-2">
            <Field
              label={t('lookups.maxFiles')}
              htmlFor={`doc-max-files-${docIndex}`}
              hint={t('lookups.maxFilesHint')}
            >
              <Input
                id={`doc-max-files-${docIndex}`}
                type="number"
                min={1}
                max={20}
                value={doc.maxFiles}
                onChange={(event) =>
                  patchDocument(docIndex, { maxFiles: Number(event.target.value) || 1 })
                }
              />
            </Field>

            <Field
              label={t('lookups.maxSizeKb')}
              htmlFor={`doc-max-size-${docIndex}`}
              hint={t('lookups.maxSizeKbHint')}
            >
              <Input
                id={`doc-max-size-${docIndex}`}
                type="number"
                min={1}
                value={doc.maxSizeBytes === null ? '' : Math.round(doc.maxSizeBytes / 1024)}
                onChange={(event) => {
                  const kb = toNumber(event.target.value);
                  patchDocument(docIndex, { maxSizeBytes: kb === null ? null : kb * 1024 });
                }}
              />
            </Field>
          </div>

          <Field
            label={t('lookups.documentTypes')}
            hint={t('lookups.documentTypesHint')}
            htmlFor={`doc-types-${docIndex}`}
          >
            {/* Toggle buttons rather than a <select multiple>: five options that each need a
                second line of explanation, and multi-select lists are notoriously easy to
                clear by accident with a stray click. */}
            <div id={`doc-types-${docIndex}`} className="flex flex-wrap gap-2">
              {DOCUMENT_FILE_TYPES.map((type) => {
                const selected = doc.allowedFileTypes.includes(type.code);
                const isLast = selected && doc.allowedFileTypes.length === 1;

                return (
                  <button
                    key={type.code}
                    type="button"
                    onClick={() => toggleFileType(docIndex, type.code)}
                    aria-pressed={selected}
                    disabled={isLast}
                    title={isLast ? t('lookups.documentTypesLast') : undefined}
                    data-testid={`doc-type-${docIndex}-${type.code}`}
                    className={cn(
                      'rounded-lg border px-3 py-1.5 text-start text-xs transition-colors',
                      selected
                        ? 'border-brand-500 bg-brand-50 text-brand-700'
                        : 'border-border bg-white text-ink-500 hover:border-ink-300',
                      isLast && 'cursor-not-allowed opacity-80',
                    )}
                  >
                    <span className="block font-semibold uppercase">
                      {t(`lookups.fileTypes.${type.code}`)}
                    </span>
                    <span className="block font-mono text-[10px] opacity-70" dir="ltr">
                      {type.extensions}
                    </span>
                  </button>
                );
              })}
            </div>
          </Field>

          <RequiredFileSamplesPanel
            requiredFileId={doc.id}
            samples={doc.samples ?? []}
            onChange={(samples) => patchDocument(docIndex, { samples })}
            pending={doc.pendingSamples ?? []}
            onPendingChange={(pendingSamples) => patchDocument(docIndex, { pendingSamples })}
            testIdSuffix={docIndex}
            scope={scope}
          />

          <div className="space-y-3 rounded-lg bg-muted/40 p-3">
            <p className="text-sm font-medium">{t('lookups.documentFields')}</p>
            <p className="text-xs text-muted-foreground">{t('lookups.documentFieldsHint')}</p>

            {doc.fields.map((field, fieldIndex) => (
              <div
                key={fieldIndex}
                className="space-y-3 rounded-lg border border-border bg-white p-3"
              >
                <div className="grid gap-2 sm:grid-cols-[1fr_1fr_auto_auto]">
                  <Input
                    dir="rtl"
                    placeholder={t('lookups.fieldNameAr')}
                    value={field.nameAr}
                    onChange={(event) =>
                      patchField(docIndex, fieldIndex, { nameAr: event.target.value })
                    }
                  />
                  <Input
                    dir="ltr"
                    data-testid={`field-name-en-${docIndex}-${fieldIndex}`}
                    placeholder={t('lookups.fieldNameEn')}
                    value={field.nameEn}
                    onChange={(event) =>
                      patchField(docIndex, fieldIndex, { nameEn: event.target.value })
                    }
                  />
                  <label className="flex items-center gap-2 whitespace-nowrap text-sm">
                    <input
                      type="checkbox"
                      className="size-4 rounded border-border"
                      checked={field.isRequired}
                      onChange={(event) =>
                        patchField(docIndex, fieldIndex, { isRequired: event.target.checked })
                      }
                    />
                    {t('lookups.fieldRequired')}
                  </label>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    aria-label={t('lookups.removeField')}
                    onClick={() =>
                      patchDocument(docIndex, {
                        fields: doc.fields
                          .filter((_, i) => i !== fieldIndex)
                          .map((f, i) => ({ ...f, sortOrder: i })),
                      })
                    }
                  >
                    <Trash2 className="size-4" aria-hidden="true" />
                  </Button>
                </div>

                <div className="grid gap-3 sm:grid-cols-3">
                  <Field label={t('lookups.fieldType')} htmlFor={`field-type-${docIndex}-${fieldIndex}`}>
                    <Select
                      id={`field-type-${docIndex}-${fieldIndex}`}
                      value={field.fieldType}
                      onChange={(event) =>
                        patchField(docIndex, fieldIndex, {
                          fieldType: Number(event.target.value) as RequiredFieldType,
                        })
                      }
                    >
                      <option value={RequiredFieldType.Text}>{t('lookups.fieldTypeText')}</option>
                      <option value={RequiredFieldType.Number}>{t('lookups.fieldTypeNumber')}</option>
                      <option value={RequiredFieldType.Date}>{t('lookups.fieldTypeDate')}</option>
                      <option value={RequiredFieldType.Dropdown}>
                        {t('lookups.fieldTypeDropdown')}
                      </option>
                    </Select>
                  </Field>

                  {field.fieldType === RequiredFieldType.Text && (
                    <>
                      <Field label={t('lookups.minLength')} htmlFor={`min-len-${docIndex}-${fieldIndex}`}>
                        <Input
                          id={`min-len-${docIndex}-${fieldIndex}`}
                          type="number"
                          min={0}
                          value={field.minLength ?? ''}
                          onChange={(event) =>
                            patchField(docIndex, fieldIndex, { minLength: toNumber(event.target.value) })
                          }
                        />
                      </Field>
                      <Field label={t('lookups.maxLength')} htmlFor={`max-len-${docIndex}-${fieldIndex}`}>
                        <Input
                          id={`max-len-${docIndex}-${fieldIndex}`}
                          type="number"
                          min={0}
                          value={field.maxLength ?? ''}
                          onChange={(event) =>
                            patchField(docIndex, fieldIndex, { maxLength: toNumber(event.target.value) })
                          }
                        />
                      </Field>
                      <Field
                        label={t('lookups.pattern')}
                        htmlFor={`pattern-${docIndex}-${fieldIndex}`}
                        hint={t('lookups.patternHint')}
                        className="sm:col-span-3"
                      >
                        <Input
                          id={`pattern-${docIndex}-${fieldIndex}`}
                          dir="ltr"
                          value={field.pattern ?? ''}
                          onChange={(event) =>
                            patchField(docIndex, fieldIndex, { pattern: event.target.value || null })
                          }
                        />
                      </Field>
                    </>
                  )}

                  {field.fieldType === RequiredFieldType.Number && (
                    <>
                      <Field label={t('lookups.minValue')} htmlFor={`min-val-${docIndex}-${fieldIndex}`}>
                        <Input
                          id={`min-val-${docIndex}-${fieldIndex}`}
                          type="number"
                          value={field.minValue ?? ''}
                          onChange={(event) =>
                            patchField(docIndex, fieldIndex, { minValue: toNumber(event.target.value) })
                          }
                        />
                      </Field>
                      <Field label={t('lookups.maxValue')} htmlFor={`max-val-${docIndex}-${fieldIndex}`}>
                        <Input
                          id={`max-val-${docIndex}-${fieldIndex}`}
                          type="number"
                          value={field.maxValue ?? ''}
                          onChange={(event) =>
                            patchField(docIndex, fieldIndex, { maxValue: toNumber(event.target.value) })
                          }
                        />
                      </Field>
                    </>
                  )}

                  {field.fieldType === RequiredFieldType.Date && (
                    <>
                      <Field label={t('lookups.dateRule')} htmlFor={`date-rule-${docIndex}-${fieldIndex}`}>
                        <Select
                          id={`date-rule-${docIndex}-${fieldIndex}`}
                          value={field.dateRule}
                          onChange={(event) =>
                            patchField(docIndex, fieldIndex, {
                              dateRule: Number(event.target.value) as RequiredFieldDateRule,
                            })
                          }
                        >
                          <option value={RequiredFieldDateRule.Any}>{t('lookups.dateAny')}</option>
                          <option value={RequiredFieldDateRule.PastOnly}>
                            {t('lookups.datePast')}
                          </option>
                          <option value={RequiredFieldDateRule.FutureOnly}>
                            {t('lookups.dateFuture')}
                          </option>
                        </Select>
                      </Field>
                      <Field label={t('lookups.minDate')} htmlFor={`min-date-${docIndex}-${fieldIndex}`}>
                        <Input
                          id={`min-date-${docIndex}-${fieldIndex}`}
                          type="date"
                          value={field.minDate ?? ''}
                          onChange={(event) =>
                            patchField(docIndex, fieldIndex, { minDate: event.target.value || null })
                          }
                        />
                      </Field>
                      <Field label={t('lookups.maxDate')} htmlFor={`max-date-${docIndex}-${fieldIndex}`}>
                        <Input
                          id={`max-date-${docIndex}-${fieldIndex}`}
                          type="date"
                          value={field.maxDate ?? ''}
                          onChange={(event) =>
                            patchField(docIndex, fieldIndex, { maxDate: event.target.value || null })
                          }
                        />
                      </Field>
                    </>
                  )}
                </div>

                {field.fieldType === RequiredFieldType.Dropdown && (
                  <div className="space-y-2">
                    <p className="text-xs font-medium">{t('lookups.fieldOptions')}</p>

                    {field.options.map((option, optionIndex) => (
                      <div key={optionIndex} className="grid gap-2 sm:grid-cols-[1fr_1fr_1fr_auto]">
                        <Input
                          dir="ltr"
                          placeholder={t('lookups.optionValue')}
                          value={option.value}
                          onChange={(event) =>
                            patchField(docIndex, fieldIndex, {
                              options: field.options.map((o, i) =>
                                i === optionIndex ? { ...o, value: event.target.value } : o,
                              ),
                            })
                          }
                        />
                        <Input
                          dir="ltr"
                          placeholder={t('lookups.optionLabelEn')}
                          value={option.labelEn}
                          onChange={(event) =>
                            patchField(docIndex, fieldIndex, {
                              options: field.options.map((o, i) =>
                                i === optionIndex ? { ...o, labelEn: event.target.value } : o,
                              ),
                            })
                          }
                        />
                        <Input
                          dir="rtl"
                          placeholder={t('lookups.optionLabelAr')}
                          value={option.labelAr}
                          onChange={(event) =>
                            patchField(docIndex, fieldIndex, {
                              options: field.options.map((o, i) =>
                                i === optionIndex ? { ...o, labelAr: event.target.value } : o,
                              ),
                            })
                          }
                        />
                        <Button
                          type="button"
                          variant="ghost"
                          size="sm"
                          aria-label={t('lookups.removeOption')}
                          onClick={() =>
                            patchField(docIndex, fieldIndex, {
                              options: field.options.filter((_, i) => i !== optionIndex),
                            })
                          }
                        >
                          <Trash2 className="size-4" aria-hidden="true" />
                        </Button>
                      </div>
                    ))}

                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      onClick={() =>
                        patchField(docIndex, fieldIndex, {
                          options: [...field.options, { value: '', labelAr: '', labelEn: '' }],
                        })
                      }
                    >
                      <Plus className="size-4" aria-hidden="true" />
                      {t('lookups.addOption')}
                    </Button>
                  </div>
                )}
              </div>
            ))}

            <Button
              type="button"
              variant="outline"
              size="sm"
              data-testid={`add-field-${docIndex}`}
              onClick={() =>
                patchDocument(docIndex, {
                  fields: [...doc.fields, { ...EMPTY_FIELD, sortOrder: doc.fields.length }],
                })
              }
            >
              <Plus className="size-4" aria-hidden="true" />
              {t('lookups.addField')}
            </Button>
          </div>
        </div>
      ))}

      <Button
        type="button"
        variant="outline"
        size="sm"
        data-testid="add-required-file"
        onClick={() => onChange([...documents, { ...EMPTY_DOCUMENT }])}
      >
        <Plus className="size-4" aria-hidden="true" />
        {t('lookups.addRequiredFile')}
      </Button>
    </fieldset>
  );
}
