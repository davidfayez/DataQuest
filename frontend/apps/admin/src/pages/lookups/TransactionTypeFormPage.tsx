import { AdminPageHeader, AdminPanel, Alert, Button, LoadingState } from '@dv/ui';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import { adminSession, Permissions } from '@/features/auth/session';
import {
  useCountries,
  useSaveLookup,
  useTransactionTypes,
  type TransactionTypeDto,
} from '@/features/lookups/api';
import { useApiErrorMessage } from '@/shared/lib/useApiError';
import { FormPageLayout, StatusPanel, SummaryPanel } from '@/shared/ui/FormPageLayout';
import { IdChecklist, LocalizedDescriptionFields, LocalizedNameFields, LookupCodeField, isValidLookupCode } from './tabs/shared';

interface TransactionTypeBody {
  id?: string;
  code: string;
  countryIds: string[];
  nameAr: string;
  nameEn: string;
  descriptionAr: string;
  descriptionEn: string;
  isActive: boolean;
}

const EMPTY: TransactionTypeBody = {
  code: '',
  countryIds: [],
  nameAr: '',
  nameEn: '',
  descriptionAr: '',
  descriptionEn: '',
  isActive: true,
};

const LIST_PATH = '/lookups/transactionTypes';

/**
 * Create or edit a transaction type on its own page rather than in a dialog.
 *
 * The row is handed over in router state when arriving from the list. Opening the URL directly
 * has no state, so the list is fetched and the record found in it — lookups have no by-id
 * endpoint.
 */
export function TransactionTypeFormPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { id } = useParams<{ id: string }>();
  const location = useLocation();
  const toMessage = useApiErrorMessage();

  const isEdit = Boolean(id);
  const handedOver = (location.state as { transactionType?: TransactionTypeDto } | null)
    ?.transactionType;

  const list = useTransactionTypes({ page: 1, pageSize: 200 });
  const existing =
    handedOver ?? (isEdit ? list.data?.items.find((row) => row.id === id) : undefined);

  const countries = useCountries({ page: 1, pageSize: 200, isActive: true });
  const [form, setForm] = useState<TransactionTypeBody>(EMPTY);
  const [countryIds, setCountryIds] = useState<Set<string>>(new Set());

  const save = useSaveLookup<TransactionTypeBody, TransactionTypeDto>('transaction-types');

  useEffect(() => {
    if (!existing) return;

    setForm({
      id: existing.id,
      code: existing.code ?? '',
      countryIds: existing.countryIds ?? [],
      nameAr: existing.nameAr,
      nameEn: existing.nameEn,
      // A row saved before descriptions existed arrives with nulls; the form works in strings.
      descriptionAr: existing.descriptionAr ?? '',
      descriptionEn: existing.descriptionEn ?? '',
      isActive: existing.isActive,
    });
    setCountryIds(new Set(existing.countryIds ?? []));
  }, [existing]);

  const canSubmit = isEdit
    ? adminSession.has(Permissions.TransactionTypesUpdate)
    : adminSession.has(Permissions.TransactionTypesCreate);

  // Both descriptions are required. Kept apart from canSubmit, which is about permission: an
  // empty field should hold Save back, not tell the admin they only have view access.
  const descriptionsComplete =
    form.descriptionAr.trim() !== '' && form.descriptionEn.trim() !== '';

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
        <AdminPageHeader title={t('lookups.transactionTypes')} subtitle={t('lookups.subtitle')} />
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
          ? t('lookups.editTitle', { entity: t('lookups.transactionType') })
          : t('lookups.createTitle', { entity: t('lookups.transactionType') })
      }
      subtitle={t('lookups.transactionTypeFormHint')}
      listLabel={t('lookups.transactionTypes')}
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

      <AdminPanel title={t('lookups.sectionAppliesIn')} subtitle={t('lookups.sectionAppliesInHint')}>
        <IdChecklist
          legend={t('lookups.countries')}
          hint={t('lookups.countriesHint')}
          selectedLabel={t('lookups.countriesSelected', { count: countryIds.size })}
          searchPlaceholder={t('lookups.countriesSearch')}
          emptyLabel={t('lookups.noCountries')}
          isPending={countries.isPending}
          selectedIds={countryIds}
          options={(countries.data?.items ?? []).map((country) => ({
            id: country.id,
            label: country.name,
            searchText: `${country.code} ${country.name} ${country.nameEn} ${country.nameAr}`,
            secondary: country.code,
          }))}
          onToggle={toggleCountry}
          testIdPrefix="transaction-type-country"
        />
      </AdminPanel>
    </FormPageLayout>
  );
}
