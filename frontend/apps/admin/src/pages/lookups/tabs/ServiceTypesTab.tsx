import { SUPPORTED_LANGUAGES } from '@dv/i18n';
import { Alert, Button, Field, Input, Select, Spinner, cn } from '@dv/ui';
import { ArrowLeft, Trash2 } from 'lucide-react';
import { useEffect, useState, type FormEvent } from 'react';
import { useTranslation } from 'react-i18next';
import {
  useAuthorities,
  useCurrencies,
  useServiceTypes,
  useSubTransactionTypes,
  useTransactionTypes,
  type ServiceTypeDto,
} from '@/features/lookups/api';
import { ConfirmDialog } from '@/shared/ui/CrudDialog';
import { DataTable } from '@/shared/ui/DataTable';
import { formatNumber } from '@/shared/lib/format';
import { useApiErrorMessage } from '@/shared/lib/useApiError';
import { NewButton } from '../LookupsPage';
import { useLookupTab } from '../useLookupTab';
import {
  RequiredDocumentsEditor,
  type RequiredFileInput,
} from './RequiredDocumentsEditor';
import { LocalizedNameFields, LookupCodeField, isValidLookupCode, Notice, ParentFilter } from './shared';
import { actionColumn, nameColumns, type LookupCaps } from './columns';

/** Full-width multiline field style, matching the app's input chrome. */
const TEXTAREA_CLASS =
  'flex w-full rounded-xl border-0 bg-white px-3.5 py-2.5 text-sm text-ink-900 shadow-soft ring-1 ring-ink-200 transition-shadow placeholder:text-ink-300 focus:outline-none focus:ring-2 focus:ring-brand-500';

/**
 * One currency's prices while they are being edited.
 *
 * The amounts are strings rather than numbers so that "not filled in yet" stays distinguishable
 * from "free". Zero is a legitimate price, and a numeric field that defaults to 0 quietly turns
 * every currency the operator never looked at into a free service.
 */
interface CostInput {
  currencyId: string;
  cost: string;
  expressCost: string;
}

interface ServiceTypeBody {
  id?: string;
  code: string;
  verificationAuthorityId: string;
  subTransactionTypeId: string;
  nameAr: string;
  nameEn: string;
  descriptionAr: string;
  descriptionEn: string;
  executionTimeDays: number;
  enableExpress: boolean;
  expressNoteAr: string;
  expressNoteEn: string;
  isActive: boolean;
  showOnLanding: boolean;
  costs: CostInput[];
  requiredFiles: RequiredFileInput[];
  outputLanguages: string[];
}

/** What the API actually receives: the same shape with the amounts parsed. */
type ServiceTypePayload = Omit<ServiceTypeBody, 'costs'> & {
  costs: { currencyId: string; cost: number; expressCost: number }[];
};

const EMPTY: ServiceTypeBody = {
  code: '',
  verificationAuthorityId: '',
  subTransactionTypeId: '',
  nameAr: '',
  nameEn: '',
  descriptionAr: '',
  descriptionEn: '',
  executionTimeDays: 7,
  enableExpress: false,
  expressNoteAr: '',
  expressNoteEn: '',
  isActive: true,
  showOnLanding: true,
  // Filled in from the currencies in scope once a sub-type is chosen.
  costs: [],
  requiredFiles: [],
  // A new service offers every language until an administrator narrows it.
  outputLanguages: SUPPORTED_LANGUAGES.map((language) => language.code),
};

export function ServiceTypesTab({ caps }: { caps: LookupCaps }) {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage ?? 'en';
  const toMessage = useApiErrorMessage();
  const tab = useLookupTab<ServiceTypeDto, ServiceTypePayload>('service-types');
  const list = useServiceTypes(tab.params);
  const authorities = useAuthorities({ page: 1, pageSize: 200, isActive: true });
  const subTypes = useSubTransactionTypes({ page: 1, pageSize: 200, isActive: true });
  const currencies = useCurrencies({ page: 1, pageSize: 200, isActive: true });
  const transactionTypes = useTransactionTypes({ page: 1, pageSize: 200, isActive: true });
  const [form, setForm] = useState<ServiceTypeBody>(EMPTY);

  useEffect(() => {
    if (tab.editing) {
      setForm({
        id: tab.editing.id,
        code: tab.editing.code ?? '',
        verificationAuthorityId: tab.editing.verificationAuthorityId,
        subTransactionTypeId: tab.editing.subTransactionTypeId,
        nameAr: tab.editing.nameAr,
        nameEn: tab.editing.nameEn,
        descriptionAr: tab.editing.descriptionAr ?? '',
        descriptionEn: tab.editing.descriptionEn ?? '',
        executionTimeDays: tab.editing.executionTimeDays,
        enableExpress: tab.editing.enableExpress,
        expressNoteAr: tab.editing.expressNoteAr ?? '',
        expressNoteEn: tab.editing.expressNoteEn ?? '',
        isActive: tab.editing.isActive,
        showOnLanding: tab.editing.showOnLanding,
        costs:
          tab.editing.costs?.length > 0
            ? tab.editing.costs.map((cost) => ({
                currencyId: cost.currencyId,
                cost: String(cost.cost),
                expressCost: String(cost.expressCost),
              }))
            : [],
        requiredFiles: tab.editing.requiredFiles.map((file) => ({
          id: file.id,
          nameAr: file.nameAr,
          nameEn: file.nameEn,
          isMandatory: file.isMandatory,
          maxSizeBytes: file.maxSizeBytes,
          maxFiles: file.maxFiles,
          // Already resolved by the API, so a document saved before formats were configurable
          // arrives carrying the platform default rather than an empty set.
          allowedFileTypes: [...(file.allowedFileTypes ?? [])],
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
        })),
        outputLanguages: [...(tab.editing.outputLanguages ?? [])],
      });
    } else {
      setForm(EMPTY);
    }
  }, [tab.editing, tab.isCreating]);

  const authoritySubTypeIds = new Set(
    authorities.data?.items.find((a) => a.id === form.verificationAuthorityId)
      ?.subTransactionTypeIds ?? [],
  );
  const subTypeOptions = (subTypes.data?.items ?? []).filter(
    (sub) => !form.verificationAuthorityId || authoritySubTypeIds.has(sub.id),
  );

  // Prices may only be quoted in a currency the service can actually be sold in: the chosen
  // sub-type belongs to a transaction type, that transaction type runs in a set of countries, and
  // each country has its own currencies. Anything outside that set would never be selectable by an
  // applicant, so offering it here only invites a price nobody can pay in.
  const parentTransactionTypeId = subTypes.data?.items.find(
    (sub) => sub.id === form.subTransactionTypeId,
  )?.transactionTypeId;

  const transactionTypeCountryIds = new Set(
    transactionTypes.data?.items.find((type) => type.id === parentTransactionTypeId)?.countryIds ??
      [],
  );

  const currencyOptions = (currencies.data?.items ?? []).filter((currency) =>
    currency.countryIds.some((countryId) => transactionTypeCountryIds.has(countryId)),
  );

  // Recomputed each render, so effects key off the ids rather than the array identity.
  const currencyScopeKey = currencyOptions
    .map((currency) => currency.id)
    .sort()
    .join(',');

  /**
   * Keeps one price row per currency in scope.
   *
   * The operator does not choose which currencies to price — the sub-type decides that — so the
   * rows are generated rather than added by hand. Amounts already typed survive; a currency that
   * drops out of scope keeps its row only while it still holds a saved price, so an existing price
   * is never silently discarded, and never silently kept either.
   */
  useEffect(() => {
    if (!tab.isDialogOpen) return;

    setForm((previous) => {
      const byCurrency = new Map(previous.costs.map((cost) => [cost.currencyId, cost]));

      const inScope = currencyOptions.map(
        (currency) =>
          byCurrency.get(currency.id) ?? { currencyId: currency.id, cost: '', expressCost: '' },
      );

      const scopeIds = new Set(currencyOptions.map((currency) => currency.id));
      const strays = previous.costs.filter(
        (cost) => cost.currencyId && !scopeIds.has(cost.currencyId) && cost.cost !== '',
      );

      const next = [...inScope, ...strays];

      const unchanged =
        next.length === previous.costs.length
        && next.every((cost, index) => cost === previous.costs[index]);

      return unchanged ? previous : { ...previous, costs: next };
    });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [currencyScopeKey, tab.isDialogOpen]);

  const currencyName = (currencyId: string) =>
    currencies.data?.items.find((currency) => currency.id === currencyId);

  /** Currencies still waiting for a price — what stands between the operator and saving. */
  const unpriced = form.costs.filter((cost) => cost.cost.trim() === '');
  const unpricedExpress = form.enableExpress
    ? form.costs.filter((cost) => Number(cost.expressCost) <= 0)
    : [];

  /**
   * Switching sub-type moves the service to a different transaction type, and therefore to a
   * different set of countries and currencies. The price rows follow on their own — the effect
   * above rebuilds them for whatever is now in scope.
   */
  function changeSubType(subTransactionTypeId: string) {
    setForm((previous) => ({ ...previous, subTransactionTypeId }));
  }

  function patchCost(currencyId: string, patch: Partial<CostInput>) {
    setForm((previous) => ({
      ...previous,
      costs: previous.costs.map((cost) =>
        cost.currencyId === currencyId ? { ...cost, ...patch } : cost,
      ),
    }));
  }

  const canSubmit =
    form.costs.length > 0
    && unpriced.length === 0
    && unpricedExpress.length === 0
    // A service with no documents asks the applicant to upload nothing, and the review queue
    // then receives an application with nothing to verify.
    && form.requiredFiles.length > 0
    && isValidLookupCode(form.code);

  function handleSubmit(event: FormEvent) {
    event.preventDefault();
    if (!canSubmit) return;

    // The amounts are held as text so an empty field stays distinct from a free service; the API
    // takes numbers.
    tab.submit({
      ...form,
      costs: form.costs.map((cost) => ({
        currencyId: cost.currencyId,
        cost: Number(cost.cost),
        expressCost: form.enableExpress ? Number(cost.expressCost) : 0,
      })),
    });
  }

  // While creating or editing, the form takes over the whole tab as a full-width page rather
  // than a cramped modal — the required-files list and long descriptions need the room.
  if (tab.isDialogOpen) {
    return (
      <div className="space-y-6">
        <div className="flex items-center gap-3">
          <Button type="button" variant="outline" size="sm" onClick={tab.closeDialog}>
            <ArrowLeft className="size-4" aria-hidden="true" />
            {t('common.back')}
          </Button>
          <h2 className="font-display text-lg font-semibold text-ink-950">
            {tab.editing
              ? t('lookups.editTitle', { entity: t('lookups.serviceTypes') })
              : t('lookups.createTitle', { entity: t('lookups.serviceTypes') })}
          </h2>
        </div>

        <form
          onSubmit={handleSubmit}
          noValidate
          className="space-y-6 rounded-2xl bg-white p-6 shadow-soft ring-1 ring-ink-100 sm:p-8"
        >
          {tab.save.error ? (
            <Alert variant="error" title={t('errors.genericTitle')}>
              {toMessage(tab.save.error)}
            </Alert>
          ) : null}

          <div className="grid gap-4 sm:grid-cols-2">
            <Field label={t('lookups.authority')} htmlFor="authorityId" required>
              <Select
                id="authorityId"
                value={form.verificationAuthorityId}
                onChange={(event) =>
                  setForm({
                    ...form,
                    verificationAuthorityId: event.target.value,
                    subTransactionTypeId: '',
                  })
                }
              >
                <option value="">{t('common.none')}</option>
                {authorities.data?.items.map((authority) => (
                  <option key={authority.id} value={authority.id}>
                    {authority.name}
                  </option>
                ))}
              </Select>
            </Field>

            <Field label={t('lookups.subTransactionType')} htmlFor="subTransactionTypeId" required>
              <Select
                id="subTransactionTypeId"
                value={form.subTransactionTypeId}
                disabled={!form.verificationAuthorityId}
                onChange={(event) => changeSubType(event.target.value)}
              >
                <option value="">{t('common.none')}</option>
                {subTypeOptions.map((subType) => (
                  <option key={subType.id} value={subType.id}>
                    {subType.name}
                  </option>
                ))}
              </Select>
            </Field>
          </div>

          <LookupCodeField value={form.code} onChange={(code) => setForm({ ...form, code })} />

          <LocalizedNameFields
            nameAr={form.nameAr}
            nameEn={form.nameEn}
            isActive={form.isActive}
            onChange={(patch) => setForm({ ...form, ...patch })}
          />

          <div className="space-y-4">
            <Field label={t('lookups.descriptionAr')} htmlFor="descriptionAr">
              <textarea
                id="descriptionAr"
                dir="rtl"
                rows={4}
                className={TEXTAREA_CLASS}
                value={form.descriptionAr}
                onChange={(event) => setForm({ ...form, descriptionAr: event.target.value })}
              />
            </Field>
            <Field label={t('lookups.descriptionEn')} htmlFor="descriptionEn">
              <textarea
                id="descriptionEn"
                dir="ltr"
                rows={4}
                className={TEXTAREA_CLASS}
                value={form.descriptionEn}
                onChange={(event) => setForm({ ...form, descriptionEn: event.target.value })}
              />
            </Field>
          </div>

          <Field label={t('lookups.executionTime')} htmlFor="executionTimeDays" required>
            <Input
              id="executionTimeDays"
              type="number"
              min={1}
              className="max-w-xs"
              value={form.executionTimeDays}
              onChange={(event) =>
                setForm({ ...form, executionTimeDays: Number(event.target.value) })
              }
            />
          </Field>

          <label className="flex items-center gap-2 text-sm">
            <input
              type="checkbox"
              className="size-4 rounded border-border"
              checked={form.enableExpress}
              onChange={(event) =>
                setForm({
                  ...form,
                  enableExpress: event.target.checked,
                  costs: form.costs.map((cost) => ({
                    ...cost,
                    expressCost: event.target.checked ? cost.expressCost : '',
                  })),
                })
              }
              data-testid="enable-express"
            />
            {t('lookups.enableExpress')}
          </label>

          {/* Independent of the express toggle: the note is shown to applicants whenever it is
              filled in, so it stays editable for services that do not offer express at all. */}
          <fieldset className="space-y-3">
            <legend className="text-sm font-medium">{t('lookups.applicantNote')}</legend>
            <p className="text-xs text-muted-foreground">{t('lookups.applicantNoteHint')}</p>

            <div className="grid gap-4 sm:grid-cols-2">
              <Field label={t('lookups.applicantNoteEn')} htmlFor="expressNoteEn">
                <textarea
                  id="expressNoteEn"
                  rows={2}
                  maxLength={500}
                  dir="ltr"
                  className={TEXTAREA_CLASS}
                  value={form.expressNoteEn}
                  onChange={(event) => setForm({ ...form, expressNoteEn: event.target.value })}
                />
              </Field>

              <Field label={t('lookups.applicantNoteAr')} htmlFor="expressNoteAr">
                <textarea
                  id="expressNoteAr"
                  rows={2}
                  maxLength={500}
                  dir="rtl"
                  className={TEXTAREA_CLASS}
                  value={form.expressNoteAr}
                  onChange={(event) => setForm({ ...form, expressNoteAr: event.target.value })}
                />
              </Field>
            </div>
          </fieldset>

          <label className="flex items-center gap-2 text-sm">
            <input
              type="checkbox"
              className="size-4 rounded border-border"
              checked={form.showOnLanding}
              onChange={(event) => setForm({ ...form, showOnLanding: event.target.checked })}
              data-testid="show-on-landing"
            />
            {t('lookups.showOnLanding')}
          </label>

          <fieldset className="space-y-3">
            <div className="flex flex-wrap items-baseline justify-between gap-2">
              <legend className="text-sm font-medium">{t('lookups.outputLanguages')}</legend>
              <div className="flex gap-2">
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  onClick={() =>
                    setForm({
                      ...form,
                      outputLanguages: SUPPORTED_LANGUAGES.map((language) => language.code),
                    })
                  }
                >
                  {t('roles.selectAll')}
                </Button>
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  onClick={() => setForm({ ...form, outputLanguages: [] })}
                >
                  {t('roles.clearAll')}
                </Button>
              </div>
            </div>
            <p className="text-xs text-muted-foreground">{t('lookups.outputLanguagesHint')}</p>

            <div className="flex flex-wrap gap-2">
              {SUPPORTED_LANGUAGES.map((language) => {
                const checked = form.outputLanguages.includes(language.code);
                return (
                  <label
                    key={language.code}
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
                      data-testid={`output-language-${language.code}`}
                      onChange={() =>
                        setForm((previous) => ({
                          ...previous,
                          outputLanguages: checked
                            ? previous.outputLanguages.filter((code) => code !== language.code)
                            : [...previous.outputLanguages, language.code],
                        }))
                      }
                    />
                    <span>{language.name}</span>
                    <span className="text-muted-foreground" dir="ltr">
                      ({language.code})
                    </span>
                  </label>
                );
              })}
            </div>
          </fieldset>

          <fieldset className="space-y-3">
            <div className="flex flex-wrap items-baseline justify-between gap-2">
              <legend className="text-sm font-medium">{t('lookups.costs')}</legend>

              {form.costs.length > 0 && (
                <span
                  className={cn(
                    'rounded-md px-2 py-0.5 text-xs font-semibold',
                    unpriced.length === 0
                      ? 'bg-brand-50 text-brand-700'
                      : 'bg-amber-50 text-amber-700',
                  )}
                  data-testid="cost-progress"
                >
                  {t('lookups.costsPriced', {
                    priced: form.costs.length - unpriced.length,
                    total: form.costs.length,
                  })}
                </span>
              )}
            </div>

            <p className="text-xs text-muted-foreground">{t('lookups.costsHint')}</p>

            {!form.subTransactionTypeId && (
              <Alert variant="info">{t('lookups.selectSubTypeForCurrencies')}</Alert>
            )}

            {form.subTransactionTypeId && currencyOptions.length === 0 && (
              <Alert variant="warning">{t('lookups.noCurrenciesForTransactionType')}</Alert>
            )}

            {form.costs.length > 0 && (
              <div className="overflow-hidden rounded-lg border border-border">
                {/* Column headings sit once above the rows rather than repeating per currency,
                    which is what turns this from a stack of forms into a price list. */}
                <div className="hidden bg-muted/40 px-3 py-2 text-xs font-semibold text-muted-foreground sm:grid sm:grid-cols-[minmax(10rem,1fr)_1fr_1fr_2.5rem] sm:gap-3">
                  <span>{t('lookups.currencies')}</span>
                  <span>{t('lookups.cost')}</span>
                  <span>{t('lookups.expressCost')}</span>
                  <span />
                </div>

                <div className="divide-y divide-border">
                  {form.costs.map((cost) => {
                    const currency = currencyName(cost.currencyId);
                    const inScope = currencyOptions.some((c) => c.id === cost.currencyId);
                    const missing = cost.cost.trim() === '';

                    return (
                      <div
                        key={cost.currencyId}
                        className={cn(
                          'grid items-center gap-3 px-3 py-2 sm:grid-cols-[minmax(10rem,1fr)_1fr_1fr_2.5rem]',
                          missing && 'bg-amber-50/60',
                        )}
                        data-testid={`cost-row-${currency?.code ?? cost.currencyId}`}
                      >
                        <div className="text-sm">
                          <span className="font-mono font-semibold" dir="ltr">
                            {currency?.code ?? '—'}
                          </span>
                          <span className="ps-2 text-muted-foreground">{currency?.name}</span>
                          {!inScope && (
                            <span className="ms-2 rounded bg-ink-100 px-1.5 py-0.5 text-[10px] font-semibold text-ink-500">
                              {t('lookups.currencyOutOfScope')}
                            </span>
                          )}
                        </div>

                        <Input
                          aria-label={`${t('lookups.cost')} ${currency?.code ?? ''}`}
                          type="number"
                          min={0}
                          step="0.01"
                          dir="ltr"
                          placeholder="0.00"
                          invalid={missing}
                          value={cost.cost}
                          onChange={(event) =>
                            patchCost(cost.currencyId, { cost: event.target.value })
                          }
                          data-testid={`cost-${currency?.code ?? cost.currencyId}`}
                        />

                        <Input
                          aria-label={`${t('lookups.expressCost')} ${currency?.code ?? ''}`}
                          type="number"
                          min={0}
                          step="0.01"
                          dir="ltr"
                          placeholder={form.enableExpress ? '0.00' : '—'}
                          disabled={!form.enableExpress}
                          invalid={form.enableExpress && Number(cost.expressCost) <= 0}
                          value={cost.expressCost}
                          onChange={(event) =>
                            patchCost(cost.currencyId, { expressCost: event.target.value })
                          }
                          data-testid={`express-${currency?.code ?? cost.currencyId}`}
                        />

                        {/* Only a price left over from a currency this service can no longer be
                            sold in may be removed; the rest are not the operator's to choose. */}
                        {inScope ? (
                          <span />
                        ) : (
                          <Button
                            type="button"
                            variant="ghost"
                            size="sm"
                            aria-label={t('lookups.removeCost')}
                            onClick={() =>
                              setForm({
                                ...form,
                                costs: form.costs.filter(
                                  (row) => row.currencyId !== cost.currencyId,
                                ),
                              })
                            }
                          >
                            <Trash2 className="size-4" aria-hidden="true" />
                          </Button>
                        )}
                      </div>
                    );
                  })}
                </div>
              </div>
            )}

            {unpriced.length > 0 && (
              <Alert variant="warning" data-testid="costs-incomplete">
                {t('lookups.costsMissing', {
                  currencies: unpriced
                    .map((cost) => currencyName(cost.currencyId)?.code ?? '?')
                    .join(', '),
                })}
              </Alert>
            )}

            {unpriced.length === 0 && unpricedExpress.length > 0 && (
              <Alert variant="warning" data-testid="missing-express">
                {t('lookups.expressCostsMissing', {
                  currencies: unpricedExpress
                    .map((cost) => currencyName(cost.currencyId)?.code ?? '?')
                    .join(', '),
                })}
              </Alert>
            )}
          </fieldset>

          <RequiredDocumentsEditor
            documents={form.requiredFiles}
            onChange={(requiredFiles) => setForm({ ...form, requiredFiles })}
          />

          <div className="flex justify-end gap-3 border-t border-ink-100 pt-5">
            <Button type="button" variant="outline" onClick={tab.closeDialog}>
              {t('common.cancel')}
            </Button>
            {/* Saving is blocked rather than allowed-and-rejected: a service missing a currency
                does not fail loudly later, it quietly stops appearing for those applicants. */}
            <Button
              type="submit"
              disabled={tab.save.isPending || !canSubmit}
              data-testid="dialog-submit"
            >
              {tab.save.isPending && <Spinner />}
              {t('common.save')}
            </Button>
          </div>
        </form>
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <Notice message={tab.notice} onDismiss={tab.dismissNotice} />

      <DataTable
        data={list.data}
        isPending={list.isPending}
        rowKey={(row) => row.id}
        onSearch={tab.setSearch}
        onPageChange={tab.setPage}
        onPageSizeChange={tab.setPageSize}
        onSortChange={tab.setSort}
        filters={
          <ParentFilter
            label={t('lookups.authority')}
            value={tab.params.verificationAuthorityId}
            options={authorities.data?.items ?? []}
            onChange={(verificationAuthorityId) =>
              tab.setParams((p) => ({ ...p, verificationAuthorityId, page: 1 }))
            }
          />
        }
        toolbar={
          caps.canCreate ? (
            <NewButton
              label={t('lookups.createTitle', { entity: t('lookups.serviceTypes') })}
              onClick={() => tab.setIsCreating(true)}
            />
          ) : null
        }
        columns={[
          {
            key: 'code',
            header: t('lookups.code'),
            getValue: (row) => row.code,
            render: (row) => (
              <span className="font-mono text-xs" dir="ltr">
                {row.code || '—'}
              </span>
            ),
          },
          ...nameColumns<ServiceTypeDto>(t),
          {
            key: 'cost',
            header: t('lookups.costs'),
            render: (row) =>
              (row.costs?.length ?? 0) === 0 ? (
                <span className="text-subtle">—</span>
              ) : (
                <div className="flex flex-wrap gap-1" dir="ltr">
                  {row.costs.map((cost) => (
                    <span
                      key={cost.currencyId}
                      className="rounded bg-muted px-1.5 py-0.5 font-mono text-xs text-muted-foreground"
                    >
                      {cost.currencyCode ?? '?'}: {formatNumber(cost.cost, locale)}
                    </span>
                  ))}
                </div>
              ),
          },
          {
            key: 'express',
            header: t('lookups.enableExpress'),
            align: 'center',
            render: (row) => (row.enableExpress ? '✓' : '—'),
          },
          {
            key: 'time',
            header: t('lookups.executionTime'),
            align: 'end',
            render: (row) => row.executionTimeDays,
          },
          {
            key: 'files',
            header: t('lookups.requiredFiles'),
            align: 'center',
            render: (row) => row.requiredFiles.length,
          },
          actionColumn<ServiceTypeDto>(t, caps, tab.setEditing, tab.setToDelete, (row) => row.nameEn),
        ]}
      />

      <ConfirmDialog
        open={tab.toDelete !== null}
        title={t('lookups.deleteTitle')}
        body={t('lookups.deleteBody')}
        confirmLabel={t('common.delete')}
        onClose={() => tab.setToDelete(null)}
        onConfirm={tab.confirmDelete}
        isPending={tab.remove.isPending}
        destructive
      />
    </div>
  );
}
