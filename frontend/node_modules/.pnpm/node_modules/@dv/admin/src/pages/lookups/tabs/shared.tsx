import { Alert, Button, Field, Input, Select } from '@dv/ui';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';

/** The bilingual name pair and active flag every lookup carries. */
export function LocalizedNameFields({
  nameAr,
  nameEn,
  isActive,
  onChange,
  showActive = true,
}: {
  nameAr: string;
  nameEn: string;
  isActive: boolean;
  onChange: (patch: { nameAr?: string; nameEn?: string; isActive?: boolean }) => void;
  /** Off on the full-page forms, which show status in their own panel instead. */
  showActive?: boolean;
}) {
  const { t } = useTranslation();

  return (
    <>
      <div className="grid gap-4 sm:grid-cols-2">
        {/* Each name is typed in its own script direction regardless of the panel's language. */}
        <Field label={t('lookups.nameAr')} htmlFor="nameAr" required>
          <Input
            id="nameAr"
            dir="rtl"
            value={nameAr}
            onChange={(event) => onChange({ nameAr: event.target.value })}
          />
        </Field>

        <Field label={t('lookups.nameEn')} htmlFor="nameEn" required>
          <Input
            id="nameEn"
            dir="ltr"
            value={nameEn}
            onChange={(event) => onChange({ nameEn: event.target.value })}
          />
        </Field>
      </div>

      {showActive && (
        <label className="flex items-center gap-2 text-sm">
          <input
            type="checkbox"
            className="size-4 rounded border-border"
            checked={isActive}
            onChange={(event) => onChange({ isActive: event.target.checked })}
          />
          {t('lookups.isActive')}
        </label>
      )}
    </>
  );
}

/**
 * A lookup's unique code: letters and digits, with hyphens or underscores between, 2 to 30
 * characters. Mirrors the server's rule so Save is held back before a request is wasted; the
 * server still decides, including whether another row already has it.
 */
export function isValidLookupCode(code: string): boolean {
  return /^[A-Z0-9][A-Z0-9_-]{1,29}$/.test(code.trim().toUpperCase());
}

/**
 * The code input. Typed upper-case and without spaces, because that is how the code is stored —
 * showing it any other way would let someone save "edu" and find "EDU" in the list.
 */
export function LookupCodeField({
  value,
  onChange,
}: {
  value: string;
  onChange: (code: string) => void;
}) {
  const { t } = useTranslation();
  const invalid = value !== '' && !isValidLookupCode(value);

  return (
    <Field
      label={t('lookups.code')}
      htmlFor="code"
      required
      hint={t('lookups.codeHint')}
      error={invalid ? t('lookups.codeInvalid') : undefined}
    >
      <Input
        id="code"
        dir="ltr"
        maxLength={30}
        className="max-w-xs font-mono uppercase"
        value={value}
        invalid={invalid}
        onChange={(event) => onChange(event.target.value.toUpperCase().replace(/\s+/g, ''))}
        data-testid="lookup-code"
      />
    </Field>
  );
}

/** Multiline field chrome matching the app's inputs; @dv/ui has no textarea of its own. */
const TEXTAREA_CLASS =
  'flex w-full rounded-xl border-0 bg-white px-3.5 py-2.5 text-sm text-ink-900 shadow-soft ring-1 ring-ink-200 transition-shadow placeholder:text-ink-300 focus:outline-none focus:ring-2 focus:ring-brand-500';

/**
 * The bilingual description pair — required in both languages on the lookups that carry one.
 *
 * Each is written in its own script direction whatever language the panel is in, like the names.
 */
export function LocalizedDescriptionFields({
  descriptionAr,
  descriptionEn,
  onChange,
}: {
  descriptionAr: string;
  descriptionEn: string;
  onChange: (patch: { descriptionAr?: string; descriptionEn?: string }) => void;
}) {
  const { t } = useTranslation();

  return (
    <div className="grid gap-4 sm:grid-cols-2">
      <Field label={t('lookups.descriptionAr')} htmlFor="descriptionAr" required>
        <textarea
          id="descriptionAr"
          dir="rtl"
          rows={4}
          maxLength={2000}
          className={TEXTAREA_CLASS}
          value={descriptionAr}
          onChange={(event) => onChange({ descriptionAr: event.target.value })}
          data-testid="description-ar"
        />
      </Field>

      <Field label={t('lookups.descriptionEn')} htmlFor="descriptionEn" required>
        <textarea
          id="descriptionEn"
          dir="ltr"
          rows={4}
          maxLength={2000}
          className={TEXTAREA_CLASS}
          value={descriptionEn}
          onChange={(event) => onChange({ descriptionEn: event.target.value })}
          data-testid="description-en"
        />
      </Field>
    </div>
  );
}

/** Dismissible confirmation banner shown after a save or delete. */
export function Notice({ message, onDismiss }: { message: string | null; onDismiss: () => void }) {
  const { t } = useTranslation();
  if (!message) return null;

  return (
    <Alert variant="success" data-testid="lookup-notice">
      <div className="flex items-center justify-between gap-3">
        <span>{message}</span>
        <Button variant="ghost" size="sm" onClick={onDismiss}>
          {t('common.close')}
        </Button>
      </div>
    </Alert>
  );
}

/** Parent filter used by the country-scoped and authority-scoped tabs. */
export function ParentFilter({
  label,
  value,
  options,
  onChange,
}: {
  label: string;
  value: string | undefined;
  options: Array<{ id: string; name: string }>;
  onChange: (value: string | undefined) => void;
}) {
  const { t } = useTranslation();

  return (
    <Field label={label} htmlFor="parent-filter" className="w-56">
      <Select
        id="parent-filter"
        value={value ?? ''}
        onChange={(event) => onChange(event.target.value || undefined)}
      >
        <option value="">{t('common.all')}</option>
        {options.map((option) => (
          <option key={option.id} value={option.id}>
            {option.name}
          </option>
        ))}
      </Select>
    </Field>
  );
}

/** Searchable multi-select checklist used for country/currency mappings. */
export function IdChecklist({
  legend,
  hint,
  selectedLabel,
  searchPlaceholder,
  emptyLabel,
  isPending,
  selectedIds,
  options,
  onToggle,
  testIdPrefix,
  secondary,
}: {
  legend: string;
  hint: string;
  selectedLabel: string;
  searchPlaceholder: string;
  emptyLabel: string;
  isPending: boolean;
  selectedIds: Set<string>;
  options: Array<{ id: string; label: string; searchText: string; secondary?: string }>;
  onToggle: (id: string) => void;
  testIdPrefix: string;
  secondary?: (option: { id: string; label: string }) => string | undefined;
}) {
  const { t } = useTranslation();
  const [search, setSearch] = useState('');

  const visible = options.filter((option) => {
    if (selectedIds.has(option.id)) return true;
    const term = search.trim().toLowerCase();
    if (!term) return true;
    return option.searchText.toLowerCase().includes(term);
  });

  return (
    <fieldset className="space-y-2">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <legend className="text-sm font-medium">{legend}</legend>
        <span className="text-xs text-muted-foreground">{selectedLabel}</span>
      </div>
      <p className="text-xs text-muted-foreground">{hint}</p>

      {isPending ? (
        <p className="text-sm text-muted-foreground">{t('common.loading')}</p>
      ) : options.length === 0 ? (
        <p className="text-sm text-muted-foreground">{emptyLabel}</p>
      ) : (
        <>
          <Input
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder={searchPlaceholder}
            aria-label={searchPlaceholder}
          />
          <div className="flex max-h-52 flex-wrap gap-2 overflow-y-auto rounded-lg border border-border p-2">
            {visible.map((option) => {
              const checked = selectedIds.has(option.id);
              const secondaryText = secondary?.(option) ?? option.secondary;
              return (
                <label
                  key={option.id}
                  className={
                    'flex cursor-pointer items-center gap-2 rounded-lg border px-3 py-2 text-sm transition-colors ' +
                    (checked
                      ? 'border-primary-border bg-primary-muted text-primary'
                      : 'border-border hover:bg-muted')
                  }
                >
                  <input
                    type="checkbox"
                    className="size-4 rounded border-border-strong"
                    checked={checked}
                    onChange={() => onToggle(option.id)}
                    data-testid={`${testIdPrefix}-${option.id}`}
                  />
                  <span>{option.label}</span>
                  {secondaryText ? (
                    <span className="text-muted-foreground" dir="ltr">
                      {secondaryText}
                    </span>
                  ) : null}
                </label>
              );
            })}
            {visible.length === 0 && (
              <p className="p-2 text-sm text-muted-foreground">{t('table.empty')}</p>
            )}
          </div>
        </>
      )}
    </fieldset>
  );
}
