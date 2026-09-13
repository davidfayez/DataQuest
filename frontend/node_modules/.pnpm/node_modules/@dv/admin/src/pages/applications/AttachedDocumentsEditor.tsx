import { Button, Field, Input, Select, cn } from '@dv/ui';
import { Eye, EyeOff, Paperclip, Plus, SlidersHorizontal, Trash2 } from 'lucide-react';
import { useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { formatFileSize } from '@/shared/lib/format';
import { useLanguage } from '@/shared/lib/useLanguage';
import {
  RequiredFieldDateRule,
  RequiredFieldType,
  type FieldOptionInput,
} from '@/pages/lookups/tabs/RequiredDocumentsEditor';

/**
 * A field an administrator adds beside a document they are attaching. Same rule set the service
 * type's required fields carry — deliberately, since the server judges both with one validator —
 * plus the answer, because here the reviewer defines the field and fills it in the same breath.
 */
export interface AttachedFieldInput {
  nameAr: string;
  nameEn: string;
  fieldType: RequiredFieldType;
  isRequired: boolean;
  sortOrder: number;
  value: string;
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

export interface AttachedDocumentInput {
  nameAr: string;
  nameEn: string;
  /** Withheld documents stay inside the review team, like an internal comment. */
  isVisibleToApplicant: boolean;
  files: File[];
  fields: AttachedFieldInput[];
}

/** Mirrors AttachedDocumentLimits on the server, so the panel refuses what the API would. */
export const DOCUMENT_LIMITS = {
  maxDocuments: 10,
  maxFiles: 10,
  maxFields: 20,
  maxFileSizeBytes: 5 * 1024 * 1024,
} as const;

export const EMPTY_ATTACHED_DOCUMENT: AttachedDocumentInput = {
  nameAr: '',
  nameEn: '',
  isVisibleToApplicant: true,
  files: [],
  fields: [],
};

const EMPTY_ATTACHED_FIELD: Omit<AttachedFieldInput, 'sortOrder'> = {
  nameAr: '',
  nameEn: '',
  fieldType: RequiredFieldType.Text,
  isRequired: true,
  value: '',
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

function toNumber(value: string): number | null {
  if (value.trim() === '') return null;
  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

/** Total files across every document — the server caps the request, not each document. */
export function countFiles(documents: AttachedDocumentInput[]): number {
  return documents.reduce((total, doc) => total + doc.files.length, 0);
}

interface Props {
  documents: AttachedDocumentInput[];
  onChange: (documents: AttachedDocumentInput[]) => void;
  disabled?: boolean;
}

/**
 * Attaches documents to an application as part of a review decision: several files per document,
 * each with the details the reviewer recorded beside it.
 *
 * The layout follows the service type's required-documents editor on purpose — an administrator
 * who has configured a service type already knows how to drive this. The differences are that
 * files are picked here rather than described, that each field carries its answer, and that a
 * document can be withheld from the applicant.
 */
export function AttachedDocumentsEditor({ documents, onChange, disabled = false }: Props) {
  const { t } = useTranslation();
  const lang = useLanguage();
  const [openRules, setOpenRules] = useState<Record<string, boolean>>({});
  const fileInputs = useRef<Record<number, HTMLInputElement | null>>({});

  const totalFiles = countFiles(documents);

  function patchDocument(index: number, patch: Partial<AttachedDocumentInput>) {
    onChange(documents.map((doc, i) => (i === index ? { ...doc, ...patch } : doc)));
  }

  function patchField(docIndex: number, fieldIndex: number, patch: Partial<AttachedFieldInput>) {
    patchDocument(docIndex, {
      fields: documents[docIndex]!.fields.map((field, i) =>
        i === fieldIndex ? { ...field, ...patch } : field,
      ),
    });
  }

  /** Renders the answer box for the field's own type, so a date is a date picker and so on. */
  function renderValueInput(docIndex: number, fieldIndex: number, field: AttachedFieldInput) {
    const id = `doc-field-value-${docIndex}-${fieldIndex}`;
    const onValue = (value: string) => patchField(docIndex, fieldIndex, { value });

    if (field.fieldType === RequiredFieldType.Dropdown) {
      return (
        <Select
          id={id}
          data-testid={`doc-field-value-${docIndex}-${fieldIndex}`}
          value={field.value}
          disabled={disabled}
          onChange={(event) => onValue(event.target.value)}
        >
          <option value="">{t('applications.documents.selectValue')}</option>
          {field.options.map((option, i) => (
            <option key={i} value={option.value}>
              {(lang === 'ar' ? option.labelAr : option.labelEn) || option.value}
            </option>
          ))}
        </Select>
      );
    }

    return (
      <Input
        id={id}
        data-testid={`doc-field-value-${docIndex}-${fieldIndex}`}
        type={
          field.fieldType === RequiredFieldType.Number
            ? 'number'
            : field.fieldType === RequiredFieldType.Date
              ? 'date'
              : 'text'
        }
        dir={field.fieldType === RequiredFieldType.Text ? undefined : 'ltr'}
        value={field.value}
        disabled={disabled}
        onChange={(event) => onValue(event.target.value)}
      />
    );
  }

  return (
    <fieldset className="space-y-4" data-testid="attached-documents-editor">
      <legend className="text-sm font-medium">{t('applications.documents.legend')}</legend>
      <p className="text-xs text-muted-foreground">{t('applications.documents.hint')}</p>

      {documents.map((doc, docIndex) => (
        <div key={docIndex} className="space-y-4 rounded-xl border border-border p-4">
          <div className="grid gap-2 sm:grid-cols-[1fr_1fr_auto]">
            <Input
              dir="rtl"
              placeholder={t('lookups.nameAr')}
              value={doc.nameAr}
              disabled={disabled}
              data-testid={`doc-name-ar-${docIndex}`}
              onChange={(event) => patchDocument(docIndex, { nameAr: event.target.value })}
            />
            <Input
              dir="ltr"
              placeholder={t('lookups.nameEn')}
              value={doc.nameEn}
              disabled={disabled}
              data-testid={`doc-name-en-${docIndex}`}
              onChange={(event) => patchDocument(docIndex, { nameEn: event.target.value })}
            />
            <Button
              type="button"
              variant="ghost"
              size="sm"
              disabled={disabled}
              aria-label={t('applications.documents.removeDocument')}
              data-testid={`remove-doc-${docIndex}`}
              onClick={() => onChange(documents.filter((_, i) => i !== docIndex))}
            >
              <Trash2 className="size-4" aria-hidden="true" />
            </Button>
          </div>

          {/* Amber when withheld, matching the internal-comment styling elsewhere on this page, so
              "the applicant will not see this" always looks the same. */}
          <label
            className={cn(
              'flex w-fit items-center gap-2 rounded-lg border px-3 py-2 text-sm',
              doc.isVisibleToApplicant
                ? 'border-border bg-background'
                : 'border-warning/50 bg-warning/10',
            )}
          >
            <input
              type="checkbox"
              className="size-4 rounded border-border"
              checked={doc.isVisibleToApplicant}
              disabled={disabled}
              data-testid={`doc-visible-${docIndex}`}
              onChange={(event) =>
                patchDocument(docIndex, { isVisibleToApplicant: event.target.checked })
              }
            />
            {doc.isVisibleToApplicant ? (
              <Eye className="size-4" aria-hidden="true" />
            ) : (
              <EyeOff className="size-4 text-warning" aria-hidden="true" />
            )}
            {doc.isVisibleToApplicant
              ? t('applications.documents.visible')
              : t('applications.documents.internal')}
          </label>

          <div className="space-y-2">
            <input
              ref={(element) => {
                fileInputs.current[docIndex] = element;
              }}
              type="file"
              multiple
              className="sr-only"
              accept=".pdf,.jpg,.jpeg,.png"
              data-testid={`doc-files-${docIndex}`}
              onChange={(event) => {
                const picked = Array.from(event.target.files ?? []);
                patchDocument(docIndex, { files: [...doc.files, ...picked] });
                event.target.value = '';
              }}
            />

            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={disabled || totalFiles >= DOCUMENT_LIMITS.maxFiles}
              data-testid={`add-files-${docIndex}`}
              onClick={() => fileInputs.current[docIndex]?.click()}
            >
              <Paperclip className="size-4" aria-hidden="true" />
              {t('applications.documents.addFiles')}
            </Button>

            {doc.files.length === 0 ? (
              <p className="text-xs text-muted-foreground">
                {t('applications.documents.noFilesYet')}
              </p>
            ) : (
              <ul className="space-y-1">
                {doc.files.map((file, fileIndex) => {
                  const tooLarge = file.size > DOCUMENT_LIMITS.maxFileSizeBytes;

                  return (
                    <li
                      key={`${file.name}-${fileIndex}`}
                      className={cn(
                        'flex items-center justify-between gap-3 rounded-lg border px-3 py-2 text-sm',
                        tooLarge ? 'border-destructive/50 bg-destructive/10' : 'border-border',
                      )}
                    >
                      <span className="min-w-0 truncate">{file.name}</span>
                      <span className="flex items-center gap-2 whitespace-nowrap text-xs text-muted-foreground">
                        {formatFileSize(file.size, lang)}
                        {tooLarge ? (
                          <span className="text-destructive">
                            {t('applications.documents.fileTooLarge')}
                          </span>
                        ) : null}
                        <Button
                          type="button"
                          variant="ghost"
                          size="sm"
                          disabled={disabled}
                          aria-label={t('applications.documents.removeFile')}
                          onClick={() =>
                            patchDocument(docIndex, {
                              files: doc.files.filter((_, i) => i !== fileIndex),
                            })
                          }
                        >
                          <Trash2 className="size-4" aria-hidden="true" />
                        </Button>
                      </span>
                    </li>
                  );
                })}
              </ul>
            )}
          </div>

          <div className="space-y-3 rounded-lg bg-muted/40 p-3">
            <p className="text-sm font-medium">{t('applications.documents.details')}</p>
            <p className="text-xs text-muted-foreground">
              {t('applications.documents.detailsHint')}
            </p>

            {doc.fields.map((field, fieldIndex) => {
              const rulesKey = `${docIndex}-${fieldIndex}`;
              const rulesOpen = openRules[rulesKey] === true;

              return (
                <div
                  key={fieldIndex}
                  className="space-y-3 rounded-lg border border-border bg-background p-3"
                >
                  <div className="grid gap-2 sm:grid-cols-[1fr_1fr_auto_auto]">
                    <Input
                      dir="rtl"
                      placeholder={t('lookups.fieldNameAr')}
                      value={field.nameAr}
                      disabled={disabled}
                      onChange={(event) =>
                        patchField(docIndex, fieldIndex, { nameAr: event.target.value })
                      }
                    />
                    <Input
                      dir="ltr"
                      placeholder={t('lookups.fieldNameEn')}
                      value={field.nameEn}
                      disabled={disabled}
                      data-testid={`doc-field-name-en-${docIndex}-${fieldIndex}`}
                      onChange={(event) =>
                        patchField(docIndex, fieldIndex, { nameEn: event.target.value })
                      }
                    />
                    <label className="flex items-center gap-2 whitespace-nowrap text-sm">
                      <input
                        type="checkbox"
                        className="size-4 rounded border-border"
                        checked={field.isRequired}
                        disabled={disabled}
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
                      disabled={disabled}
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

                  <div className="grid gap-3 sm:grid-cols-2">
                    <Field
                      label={t('lookups.fieldType')}
                      htmlFor={`doc-field-type-${docIndex}-${fieldIndex}`}
                    >
                      <Select
                        id={`doc-field-type-${docIndex}-${fieldIndex}`}
                        value={field.fieldType}
                        disabled={disabled}
                        onChange={(event) =>
                          // The answer is cleared with the type: a date is not a valid dropdown
                          // choice, and carrying it over would fail validation on the server.
                          patchField(docIndex, fieldIndex, {
                            fieldType: Number(event.target.value) as RequiredFieldType,
                            value: '',
                          })
                        }
                      >
                        <option value={RequiredFieldType.Text}>{t('lookups.fieldTypeText')}</option>
                        <option value={RequiredFieldType.Number}>
                          {t('lookups.fieldTypeNumber')}
                        </option>
                        <option value={RequiredFieldType.Date}>{t('lookups.fieldTypeDate')}</option>
                        <option value={RequiredFieldType.Dropdown}>
                          {t('lookups.fieldTypeDropdown')}
                        </option>
                      </Select>
                    </Field>

                    <Field
                      label={t('applications.documents.fieldValue')}
                      htmlFor={`doc-field-value-${docIndex}-${fieldIndex}`}
                    >
                      {renderValueInput(docIndex, fieldIndex, field)}
                    </Field>
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
                            disabled={disabled}
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
                            disabled={disabled}
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
                            disabled={disabled}
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
                            disabled={disabled}
                            aria-label={t('lookups.removeOption')}
                            onClick={() =>
                              patchField(docIndex, fieldIndex, {
                                // Dropping the chosen option must drop the answer with it.
                                value:
                                  field.value === field.options[optionIndex]?.value
                                    ? ''
                                    : field.value,
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
                        disabled={disabled}
                        data-testid={`add-doc-option-${docIndex}-${fieldIndex}`}
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

                  {/* The rule builder is folded away by default. A reviewer answering their own
                      field rarely needs to constrain it, but the rules are stored and enforced
                      exactly as a service type's are when they do. */}
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    disabled={disabled}
                    data-testid={`toggle-rules-${docIndex}-${fieldIndex}`}
                    onClick={() => setOpenRules((open) => ({ ...open, [rulesKey]: !rulesOpen }))}
                  >
                    <SlidersHorizontal className="size-4" aria-hidden="true" />
                    {rulesOpen
                      ? t('applications.documents.hideRules')
                      : t('applications.documents.showRules')}
                  </Button>

                  {rulesOpen && (
                    <div className="grid gap-3 border-t border-border pt-3 sm:grid-cols-3">
                      {field.fieldType === RequiredFieldType.Text && (
                        <>
                          <Field label={t('lookups.minLength')} htmlFor={`doc-min-len-${rulesKey}`}>
                            <Input
                              id={`doc-min-len-${rulesKey}`}
                              type="number"
                              min={0}
                              value={field.minLength ?? ''}
                              disabled={disabled}
                              onChange={(event) =>
                                patchField(docIndex, fieldIndex, {
                                  minLength: toNumber(event.target.value),
                                })
                              }
                            />
                          </Field>
                          <Field label={t('lookups.maxLength')} htmlFor={`doc-max-len-${rulesKey}`}>
                            <Input
                              id={`doc-max-len-${rulesKey}`}
                              type="number"
                              min={0}
                              value={field.maxLength ?? ''}
                              disabled={disabled}
                              onChange={(event) =>
                                patchField(docIndex, fieldIndex, {
                                  maxLength: toNumber(event.target.value),
                                })
                              }
                            />
                          </Field>
                          <Field
                            label={t('lookups.pattern')}
                            htmlFor={`doc-pattern-${rulesKey}`}
                            hint={t('lookups.patternHint')}
                          >
                            <Input
                              id={`doc-pattern-${rulesKey}`}
                              dir="ltr"
                              value={field.pattern ?? ''}
                              disabled={disabled}
                              onChange={(event) =>
                                patchField(docIndex, fieldIndex, {
                                  pattern: event.target.value || null,
                                })
                              }
                            />
                          </Field>
                        </>
                      )}

                      {field.fieldType === RequiredFieldType.Number && (
                        <>
                          <Field label={t('lookups.minValue')} htmlFor={`doc-min-val-${rulesKey}`}>
                            <Input
                              id={`doc-min-val-${rulesKey}`}
                              type="number"
                              value={field.minValue ?? ''}
                              disabled={disabled}
                              onChange={(event) =>
                                patchField(docIndex, fieldIndex, {
                                  minValue: toNumber(event.target.value),
                                })
                              }
                            />
                          </Field>
                          <Field label={t('lookups.maxValue')} htmlFor={`doc-max-val-${rulesKey}`}>
                            <Input
                              id={`doc-max-val-${rulesKey}`}
                              type="number"
                              value={field.maxValue ?? ''}
                              disabled={disabled}
                              onChange={(event) =>
                                patchField(docIndex, fieldIndex, {
                                  maxValue: toNumber(event.target.value),
                                })
                              }
                            />
                          </Field>
                        </>
                      )}

                      {field.fieldType === RequiredFieldType.Date && (
                        <>
                          <Field label={t('lookups.dateRule')} htmlFor={`doc-date-rule-${rulesKey}`}>
                            <Select
                              id={`doc-date-rule-${rulesKey}`}
                              value={field.dateRule}
                              disabled={disabled}
                              onChange={(event) =>
                                patchField(docIndex, fieldIndex, {
                                  dateRule: Number(event.target.value) as RequiredFieldDateRule,
                                })
                              }
                            >
                              <option value={RequiredFieldDateRule.Any}>
                                {t('lookups.dateAny')}
                              </option>
                              <option value={RequiredFieldDateRule.PastOnly}>
                                {t('lookups.datePast')}
                              </option>
                              <option value={RequiredFieldDateRule.FutureOnly}>
                                {t('lookups.dateFuture')}
                              </option>
                            </Select>
                          </Field>
                          <Field label={t('lookups.minDate')} htmlFor={`doc-min-date-${rulesKey}`}>
                            <Input
                              id={`doc-min-date-${rulesKey}`}
                              type="date"
                              value={field.minDate ?? ''}
                              disabled={disabled}
                              onChange={(event) =>
                                patchField(docIndex, fieldIndex, {
                                  minDate: event.target.value || null,
                                })
                              }
                            />
                          </Field>
                          <Field label={t('lookups.maxDate')} htmlFor={`doc-max-date-${rulesKey}`}>
                            <Input
                              id={`doc-max-date-${rulesKey}`}
                              type="date"
                              value={field.maxDate ?? ''}
                              disabled={disabled}
                              onChange={(event) =>
                                patchField(docIndex, fieldIndex, {
                                  maxDate: event.target.value || null,
                                })
                              }
                            />
                          </Field>
                        </>
                      )}
                    </div>
                  )}
                </div>
              );
            })}

            <Button
              type="button"
              variant="outline"
              size="sm"
              disabled={disabled || doc.fields.length >= DOCUMENT_LIMITS.maxFields}
              data-testid={`add-doc-field-${docIndex}`}
              onClick={() =>
                patchDocument(docIndex, {
                  fields: [
                    ...doc.fields,
                    { ...EMPTY_ATTACHED_FIELD, sortOrder: doc.fields.length },
                  ],
                })
              }
            >
              <Plus className="size-4" aria-hidden="true" />
              {t('applications.documents.addField')}
            </Button>
          </div>
        </div>
      ))}

      <div className="flex flex-wrap items-center gap-3">
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={disabled || documents.length >= DOCUMENT_LIMITS.maxDocuments}
          data-testid="add-attached-document"
          onClick={() => onChange([...documents, { ...EMPTY_ATTACHED_DOCUMENT }])}
        >
          <Plus className="size-4" aria-hidden="true" />
          {t('applications.documents.addDocument')}
        </Button>

        {documents.length > 0 && (
          <span className="text-xs text-muted-foreground">
            {/* Named "files" rather than "count": i18next reserves `count` for plural selection,
                which these bundles do not define for this key. */}
            {t('applications.documents.fileCount', {
              files: totalFiles,
              max: DOCUMENT_LIMITS.maxFiles,
            })}
          </span>
        )}
      </div>
    </fieldset>
  );
}

/**
 * Flattens the editor's state into the multipart body the API binds by name. The indexed keys are
 * what ASP.NET model binding expects for a nested list, which is why they are built by hand rather
 * than serialised as JSON: the files have to ride in the same request as the metadata describing
 * them.
 */
export function appendDocumentsToForm(form: FormData, documents: AttachedDocumentInput[]) {
  documents.forEach((doc, docIndex) => {
    const docKey = `Documents[${docIndex}]`;

    form.append(`${docKey}.NameAr`, doc.nameAr.trim());
    form.append(`${docKey}.NameEn`, doc.nameEn.trim());
    form.append(`${docKey}.IsVisibleToApplicant`, String(doc.isVisibleToApplicant));

    doc.files.forEach((file) => form.append(`${docKey}.Files`, file, file.name));

    doc.fields.forEach((field, fieldIndex) => {
      const fieldKey = `${docKey}.Fields[${fieldIndex}]`;

      form.append(`${fieldKey}.NameAr`, field.nameAr.trim());
      form.append(`${fieldKey}.NameEn`, field.nameEn.trim());
      form.append(`${fieldKey}.FieldType`, String(field.fieldType));
      form.append(`${fieldKey}.IsRequired`, String(field.isRequired));
      form.append(`${fieldKey}.SortOrder`, String(fieldIndex));
      form.append(`${fieldKey}.Value`, field.value);
      form.append(`${fieldKey}.DateRule`, String(field.dateRule));

      // Omitted rather than sent empty: an empty string does not bind to a nullable number, and
      // the server reads a missing key as "no limit".
      if (field.minLength !== null) form.append(`${fieldKey}.MinLength`, String(field.minLength));
      if (field.maxLength !== null) form.append(`${fieldKey}.MaxLength`, String(field.maxLength));
      if (field.pattern) form.append(`${fieldKey}.Pattern`, field.pattern);
      if (field.minValue !== null) form.append(`${fieldKey}.MinValue`, String(field.minValue));
      if (field.maxValue !== null) form.append(`${fieldKey}.MaxValue`, String(field.maxValue));
      if (field.minDate) form.append(`${fieldKey}.MinDate`, field.minDate);
      if (field.maxDate) form.append(`${fieldKey}.MaxDate`, field.maxDate);

      if (field.fieldType === RequiredFieldType.Dropdown) {
        field.options.forEach((option, optionIndex) => {
          const optionKey = `${fieldKey}.Options[${optionIndex}]`;
          form.append(`${optionKey}.Value`, option.value.trim());
          form.append(`${optionKey}.LabelAr`, option.labelAr.trim());
          form.append(`${optionKey}.LabelEn`, option.labelEn.trim());
        });
      }
    });
  });
}
