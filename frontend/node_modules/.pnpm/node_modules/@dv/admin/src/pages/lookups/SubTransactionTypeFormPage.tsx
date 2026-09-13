import { AdminPageHeader, AdminPanel, Alert, Button, Field, LoadingState, SearchableSelect } from '@dv/ui';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import { adminSession, Permissions } from '@/features/auth/session';
import {
  useCountries,
  useSaveLookup,
  useSubTransactionTypes,
  useTransactionTypes,
  type SubTransactionTypeDto,
} from '@/features/lookups/api';
import { useApiErrorMessage } from '@/shared/lib/useApiError';
import { FormPageLayout, StatusPanel, SummaryPanel } from '@/shared/ui/FormPageLayout';
import { IdChecklist, LocalizedDescriptionFields, LocalizedNameFields, LookupCodeField, isValidLookupCode } from './tabs/shared';

interface SubTypeBody {
  id?: string;
  code: string;
  transactionTypeId: string;
  countryIds: string[];
  nameAr: string;
  nameEn: string;
  descriptionAr: string;
  descriptionEn: string;
  isActive: boolean;
}

const EMPTY: SubTypeBody = {
  code: '',
  transactionTypeId: '',
  countryIds: [],
  nameAr: '',
  nameEn: '',
  descriptionAr: '',
  descriptionEn: '',
  isActive: true,
};

const LIST_PATH = '/lookups/subTransactionTypes';

/**
 * Create or edit a sub-transaction type on its own page rather than in a dialog.
 *
 * The row is handed over in router state when arriving from the list. Opening the URL directly
 * has no state, so the list is fetched and the record found in it — lookups have no by-id
 * endpoint.
 */
export function SubTransactionTypeFormPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { id } = useParams<{ id: string }>();
  const location = useLocation();
  const toMessage = useApiErrorMessage();

  const isEdit = Boolean(id);
  const handedOver = (location.state as { subTransactionType?: SubTransactionTypeDto } | null)
    ?.subTransactionType;

  const list = useSubTransactionTypes({ page: 1, pageSize: 200 });
  const existing =
    handedOver ?? (isEdit ? list.data?.items.find((row) => row.id === id) : undefined);

  const parents = useTransactionTypes({ page: 1, pageSize: 200, isActive: true });
  const countries = useCountries({ page: 1, pageSize: 200, isActive: true });

  const [form, setForm] = useState<SubTypeBody>(EMPTY);
  const [countryIds, setCountryIds] = useState<Set<string>>(new Set());

  const save = useSaveLookup<SubTypeBody, SubTransactionTypeDto>('sub-transaction-types');

  useEffect(() => {
    if (!existing) return;

    // A row saved before descriptions existed arrives with nulls; the form works in strings.
    setForm({
      ...existing,
      id: existing.id,
      countryIds: existing.countryIds ?? [],
      code: existing.code ?? '',
      descriptionAr: existing.descriptionAr ?? '',
      descriptionEn: existing.descriptionEn ?? '',
    });
    setCountryIds(new Set(existing.countryIds ?? []));
  }, [existing]);

  // A new sub-type opens on the first transaction type rather than on a blank the server would
  // reject at submit. Runs once the parents have loaded.
  useEffect(() => {
    if (isEdit || form.transactionTypeId) return;

    const first = parents.data?.items[0];
    if (first) setForm((current) => ({ ...current, transactionTypeId: first.id }));
  }, [isEdit, parents.data, form.transactionTypeId]);

  const canSubmit = isEdit
    ? adminSession.has(Permissions.SubTransactionTypesUpdate)
    : adminSession.has(Permissions.SubTransactionTypesCreate);

  // Both descriptions are required. Kept apart from canSubmit, which is about permission: an
  // empty field should hold Save back, not tell the admin they only have view access.
  const descriptionsComplete =
    form.descriptionAr.trim() !== '' && form.descriptionEn.trim() !== '';

  // The parent decides where a sub-type may be offered, so the checklist only ever shows the
  // transaction type's own countries. The server enforces the same rule.
  const parentCountryIds = new Set(
    parents.data?.items.find((parent) => parent.id === form.transactionTypeId)?.countryIds ?? [],
  );
  const availableCountries = (countries.data?.items ?? []).filter((country) =>
    parentCountryIds.has(country.id),
  );

  /** Changing the parent drops any selection the new parent does not cover. */
  function changeParent(transactionTypeId: string) {
    const allowed = new Set(
      parents.data?.items.find((parent) => parent.id === transactionTypeId)?.countryIds ?? [],
    );
    setForm({ ...form, transactionTypeId });
    setCountryIds((previous) => new Set([...previous].filter((countryId) => allowed.has(countryId))));
  }

  function toggleCountry(countryId: string) {
    setCountryIds((previous) => {
      const next = new Set(previous);
      if (next.has(countryId)) next.delete(countryId);
      else next.add(countryId);
      return next;
    });
  }

  function goBack() {
    navigate(LIST_PATH);
  }

  function submit() {
    save.mutate(
      { ...form, countryIds: [...countryIds] },
      // The notice belongs to the list, so it travels back in router state.
      { onSuccess: () => navigate(LIST_PATH, { state: { notice: t('lookups.saved') } }) },
    );
  }

  if (isEdit && !existing && !list.isPending) {
    return (
      <div className="animate-fade-in space-y-6">
        <AdminPageHeader title={t('lookups.subTransactionTypes')} subtitle={t('lookups.subtitle')} />
        <Alert variant="error">{t('errors.notFound')}</Alert>
        <Button type="button" variant="outline" onClick={goBack}>
          {t('common.back')}
        </Button>
      </div>
    );
  }

  if (isEdit && !existing) {
    return <LoadingState label={t('common.loading')} />;
  }

  const selectedCountryNames = (countries.data?.items ?? [])
    .filter((country) => countryIds.has(country.id))
    .map((country) => country.name);

  // The heading names one record, so it uses the singular entity — the list button stays plural.
  return (
    <FormPageLayout
      title={
        isEdit
          ? t('lookups.editTitle', { entity: t('lookups.subTransactionType') })
          : t('lookups.createTitle', { entity: t('lookups.subTransactionType') })
      }
      subtitle={t('lookups.subTransactionTypeFormHint')}
      listLabel={t('lookups.subTransactionTypes')}
      onBack={goBack}
      onSubmit={submit}
      isPending={save.isPending}
      canSubmit={canSubmit}
      isComplete={descriptionsComplete && isValidLookupCode(form.code)}
      error={save.isError ? save.error : null}
      errorMessage={toMessage}
      aside={
        <>
          <StatusPanel
            isActive={form.isActive}
            onChange={(isActive) => setForm({ ...form, isActive })}
          />
          <SummaryPanel
            title={t('lookups.countries')}
            items={selectedCountryNames}
            emptyLabel={t('lookups.summaryEmpty')}
          />
        </>
      }
    >
      <AdminPanel title={t('lookups.code')} subtitle={t('lookups.codePanelHint')}>
        <LookupCodeField value={form.code} onChange={(code) => setForm({ ...form, code })} />
      </AdminPanel>

      <AdminPanel title={t('lookups.sectionNames')} subtitle={t('lookups.sectionNamesHint')}>
        <LocalizedNameFields
          nameAr={form.nameAr}
          nameEn={form.nameEn}
          isActive={form.isActive}
          onChange={(patch) => setForm({ ...form, ...patch })}
          showActive={false}
        />
      </AdminPanel>
      <AdminPanel
        title={t('lookups.description')}
        subtitle={t('lookups.sectionDescriptionHint')}
      >
        <LocalizedDescriptionFields
          descriptionAr={form.descriptionAr}
          descriptionEn={form.descriptionEn}
          onChange={(patch) => setForm({ ...form, ...patch })}
        />
      </AdminPanel>

      {/* allowOverflow: the panel's clip would slice the open dropdown off at its edge. */}
      <AdminPanel
        title={t('lookups.sectionParent')}
        subtitle={t('lookups.sectionParentHint')}
        allowOverflow
      >
        <Field
          label={t('lookups.transactionType')}
          htmlFor="transactionTypeId"
          required
          className="max-w-md"
        >
          <SearchableSelect
            id="transactionTypeId"
            value={form.transactionTypeId}
            onChange={changeParent}
            options={(parents.data?.items ?? []).map((parent) => ({
              value: parent.id,
              label: parent.name,
            }))}
            placeholder={t('lookups.selectTransactionType')}
            searchPlaceholder={t('lookups.transactionTypesSearch')}
            emptyMessage={t('lookups.noTransactionTypes')}
          />
        </Field>
      </AdminPanel>

      <AdminPanel title={t('lookups.sectionAppliesIn')} subtitle={t('lookups.sectionAppliesInHint')}>
        <IdChecklist
          legend={t('lookups.countries')}
          // The hint states the rule; the empty label explains why the list is empty right now.
          hint={t('lookups.subTypeCountriesHint')}
          selectedLabel={t('lookups.countriesSelected', { count: countryIds.size })}
          searchPlaceholder={t('lookups.countriesSearch')}
          emptyLabel={
            form.transactionTypeId
              ? t('lookups.noCountriesInTransactionType')
              : t('lookups.selectTransactionTypeForCountries')
          }
          isPending={countries.isPending || parents.isPending}
          selectedIds={countryIds}
          options={availableCountries.map((country) => ({
            id: country.id,
            label: country.name,
            searchText: `${country.code} ${country.name} ${country.nameEn} ${country.nameAr}`,
            secondary: country.code,
          }))}
          onToggle={toggleCountry}
          testIdPrefix="sub-transaction-type-country"
        />
      </AdminPanel>
    </FormPageLayout>
  );
}
