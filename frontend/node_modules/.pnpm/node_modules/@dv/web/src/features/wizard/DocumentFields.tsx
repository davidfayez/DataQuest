import { Field, Input, SearchableSelect } from '@dv/ui';
import { useTranslation } from 'react-i18next';
import {
  RequiredFieldDateRule,
  RequiredFieldType,
  type RequiredFileFieldDto,
} from '@/entities/application/types';

/**
 * Validates one answer against the rules an administrator configured. Mirrors the server's
 * RequiredFileField.Validate exactly, so the wizard blocks the same values the API would reject
 * rather than letting the applicant reach submit and be turned away.
 */
export function validateFieldValue(
  field: RequiredFileFieldDto,
  value: string | undefined,
  t: (key: string, options?: Record<string, unknown>) => string,
): string | null {
  const trimmed = value?.trim() ?? '';

  if (trimmed === '') {
    return field.isRequired ? t('wizard.files.fieldRequired') : null;
  }

  if (field.fieldType === RequiredFieldType.Text) {
    if (field.minLength !== null && trimmed.length < field.minLength) {
      return t('wizard.files.fieldTooShort', { min: field.minLength });
    }
    if (field.maxLength !== null && trimmed.length > field.maxLength) {
      return t('wizard.files.fieldTooLong', { max: field.maxLength });
    }
    if (field.pattern) {
      try {
        if (!new RegExp(field.pattern).test(trimmed)) {
          return t('wizard.files.fieldPattern');
        }
      } catch {
        // A pattern the browser cannot compile is an admin error, not the applicant's — the
        // server makes the same allowance, so leave the value alone.
      }
    }
    return null;
  }

  if (field.fieldType === RequiredFieldType.Number) {
    const parsed = Number(trimmed);
    if (!Number.isFinite(parsed)) return t('wizard.files.fieldNotNumber');
    if (field.minValue !== null && parsed < field.minValue) {
      return t('wizard.files.fieldBelowMin', { min: field.minValue });
    }
    if (field.maxValue !== null && parsed > field.maxValue) {
      return t('wizard.files.fieldAboveMax', { max: field.maxValue });
    }
    return null;
  }

  if (field.fieldType === RequiredFieldType.Date) {
    const date = new Date(trimmed);
    if (Number.isNaN(date.getTime())) return t('wizard.files.fieldNotDate');

    // Compared date-only, in the same UTC terms the server uses.
    const today = new Date();
    const asDay = (d: Date) => Date.UTC(d.getFullYear(), d.getMonth(), d.getDate());
    const value_ = asDay(new Date(trimmed));
    const todayValue = asDay(today);

    if (field.dateRule === RequiredFieldDateRule.PastOnly && value_ >= todayValue) {
      return t('wizard.files.fieldMustBePast');
    }
    if (field.dateRule === RequiredFieldDateRule.FutureOnly && value_ <= todayValue) {
      return t('wizard.files.fieldMustBeFuture');
    }
    if (field.minDate && value_ < asDay(new Date(field.minDate))) {
      return t('wizard.files.fieldBeforeEarliest', { date: field.minDate });
    }
    if (field.maxDate && value_ > asDay(new Date(field.maxDate))) {
      return t('wizard.files.fieldAfterLatest', { date: field.maxDate });
    }
    return null;
  }

  if (field.fieldType === RequiredFieldType.Dropdown) {
    return field.options.some((option) => option.value === trimmed)
      ? null
      : t('wizard.files.fieldNotAnOption');
  }

  return null;
}

interface Props {
  fields: RequiredFileFieldDto[];
  values: Record<string, string>;
  /** True once the applicant has tried to move on, so errors are not shown pre-emptively. */
  showErrors: boolean;
  onChange: (fieldId: string, value: string) => void;
  testIdPrefix: string;
}

/** The custom details an administrator attached to a required document. */
export function DocumentFields({ fields, values, showErrors, onChange, testIdPrefix }: Props) {
  const { t } = useTranslation();

  if (fields.length === 0) return null;

  return (
    <div className="grid gap-4 border-t border-border pt-4 sm:grid-cols-2">
      {fields.map((field) => {
        const value = values[field.id] ?? '';
        const error = showErrors ? validateFieldValue(field, value, t) : null;
        const inputId = `${testIdPrefix}-${field.id}`;

        return (
          <Field
            key={field.id}
            label={field.name}
            htmlFor={inputId}
            required={field.isRequired}
            error={error ?? undefined}
          >
            {field.fieldType === RequiredFieldType.Dropdown ? (
              <SearchableSelect
                id={inputId}
                value={value}
                onChange={(next) => onChange(field.id, next)}
                invalid={Boolean(error)}
                placeholder={t('wizard.files.fieldSelect')}
                searchPlaceholder={t('common.search')}
                emptyMessage={t('common.noResults')}
                options={field.options.map((option) => ({
                  value: option.value,
                  label: option.label,
                }))}
              />
            ) : (
              <Input
                id={inputId}
                data-testid={inputId}
                type={
                  field.fieldType === RequiredFieldType.Date
                    ? 'date'
                    : field.fieldType === RequiredFieldType.Number
                      ? 'number'
                      : 'text'
                }
                dir={field.fieldType === RequiredFieldType.Text ? undefined : 'ltr'}
                invalid={Boolean(error)}
                min={field.fieldType === RequiredFieldType.Number ? (field.minValue ?? undefined) : undefined}
                max={field.fieldType === RequiredFieldType.Number ? (field.maxValue ?? undefined) : undefined}
                maxLength={field.maxLength ?? undefined}
                value={value}
                onChange={(event) => onChange(field.id, event.target.value)}
              />
            )}
          </Field>
        );
      })}
    </div>
  );
}
