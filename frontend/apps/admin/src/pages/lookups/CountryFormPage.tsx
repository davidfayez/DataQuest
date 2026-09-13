import { AdminPageHeader, AdminPanel, Alert, Button, Field, Input, LoadingState } from '@dv/ui';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import { adminSession, Permissions } from '@/features/auth/session';
import {
  useCountries,
  useCountryCurrencies,
  useCurrencies,
  type CountryDto,
} from '@/features/lookups/api';
import { apiClient } from '@/shared/api/client';
import { useApiErrorMessage } from '@/shared/lib/useApiError';
import { FormPageLayout, StatusPanel, SummaryPanel } from '@/shared/ui/FormPageLayout';
import { IdChecklist, LocalizedNameFields } from './tabs/shared';

interface CountryBody {
  id?: string;
  code: string;
  phoneCode: string;
  nameAr: string;
  nameEn: string;
  isActive: boolean;
}

// phoneCode starts empty, not '+': the calling prefix is optional, and pre-filling a bare '+'
// meant an untouched field submitted a value that fails the format rule.
const EMPTY: CountryBody = { code: '', phoneCode: '', nameAr: '', nameEn: '', isActive: true };

const LIST_PATH = '/lookups/countries';

/**
 * Create or edit a country on its own page rather than in a dialog.
 *
 * The row is handed over in router state when arriving from the list. Opening the URL directly
 * has no state, so the list is fetched and the record found in it — lookups have no by-id
 * endpoint.
 */
export function CountryFormPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { id } = useParams<{ id: string }>();
  const location = useLocation();
  const queryClient = useQueryClient();
  const toMessage = useApiErrorMessage();

  const isEdit = Boolean(id);
  const handedOver = (location.state as { country?: CountryDto } | null)?.country;

  const list = useCountries({ page: 1, pageSize: 300 });
  const existing =
    handedOver ?? (isEdit ? list.data?.items.find((row) => row.id === id) : undefined);

  const [form, setForm] = useState<CountryBody>(EMPTY);
  const [currencyIds, setCurrencyIds] = useState<Set<string>>(new Set());

  // Every active currency to offer (the whole catalogue, not just the first page), and — when
  // editing — the ones already mapped to this country.
  const allCurrencies = useCurrencies({ page: 1, pageSize: 200, isActive: true });
  const existingCurrencies = useCountryCurrencies(isEdit ? id : undefined);

  useEffect(() => {
    if (!existing) return;

    setForm({
      id: existing.id,
      code: existing.code,
      phoneCode: existing.phoneCode,
      nameAr: existing.nameAr,
      nameEn: existing.nameEn,
      isActive: existing.isActive,
    });
  }, [existing]);

  useEffect(() => {
    if (!isEdit) return;
    setCurrencyIds(new Set((existingCurrencies.data ?? []).map((currency) => currency.id)));
  }, [isEdit, existingCurrencies.data]);

  const canSubmit = isEdit
    ? adminSession.has(Permissions.CountriesUpdate)
    : adminSession.has(Permissions.CountriesCreate);

  // Save the country and its currency list together: upsert the country first (a new one needs an
  // id before currencies can be attached), then replace its currency set.
  const save = useMutation({
    mutationFn: async (body: CountryBody) => {
      const country = await apiClient.post<CountryDto>('admin/lookups/countries', body);
      await apiClient.put(`admin/lookups/countries/${country.id}/currencies`, {
        currencyIds: [...currencyIds],
      });
      return country;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['admin'] });
      // The notice belongs to the list, so it travels back in router state.
      navigate(LIST_PATH, { state: { notice: t('lookups.saved') } });
    },
  });

  function toggleCurrency(currencyId: string) {
    setCurrencyIds((previous) => {
      const next = new Set(previous);
      if (next.has(currencyId)) next.delete(currencyId);
      else next.add(currencyId);
      return next;
    });
  }

  function goBack() {
    navigate(LIST_PATH);
  }

  if (isEdit && !existing && !list.isPending) {
    return (
      <div className="animate-fade-in space-y-6">
        <AdminPageHeader title={t('lookups.countries')} subtitle={t('lookups.subtitle')} />
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

  const selectedCurrencyLabels = (allCurrencies.data?.items ?? [])
    .filter((currency) => currencyIds.has(currency.id))
    .map((currency) => `${currency.name} (${currency.code})`);

  // The heading names one record, so it uses the singular entity — the list button stays plural.
  return (
    <FormPageLayout
      title={
        isEdit
          ? t('lookups.editTitle', { entity: t('lookups.country') })
          : t('lookups.createTitle', { entity: t('lookups.country') })
      }
      subtitle={t('lookups.countryFormHint')}
      listLabel={t('lookups.countries')}
      onBack={goBack}
      onSubmit={() => save.mutate(form)}
      isPending={save.isPending}
      canSubmit={canSubmit}
      error={save.isError ? save.error : null}
      errorMessage={toMessage}
      aside={
        <>
          <StatusPanel
            isActive={form.isActive}
            onChange={(isActive) => setForm({ ...form, isActive })}
          />
          <SummaryPanel
            title={t('lookups.currencies')}
            items={selectedCurrencyLabels}
            emptyLabel={t('lookups.summaryEmpty')}
          />
        </>
      }
    >
      <AdminPanel title={t('lookups.sectionCodes')} subtitle={t('lookups.sectionCodesHint')}>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t('lookups.code')} htmlFor="code" required hint="ISO 3166-1 alpha-2">
            <Input
              id="code"
              dir="ltr"
              maxLength={2}
              className="w-24 uppercase"
              value={form.code}
              onChange={(event) => setForm({ ...form, code: event.target.value.toUpperCase() })}
            />
          </Field>

          <Field label={t('lookups.phoneCode')} htmlFor="phoneCode" hint="+20">
            <Input
              id="phoneCode"
              dir="ltr"
              maxLength={8}
              className="w-28 font-mono"
              value={form.phoneCode}
              onChange={(event) => setForm({ ...form, phoneCode: event.target.value })}
              placeholder="+20"
            />
          </Field>
        </div>
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

      <AdminPanel title={t('lookups.sectionCurrencies')} subtitle={t('lookups.sectionCurrenciesHint')}>
        <IdChecklist
          legend={t('lookups.currencies')}
          hint={t('lookups.currenciesHint')}
          selectedLabel={t('lookups.currenciesSelected', { count: currencyIds.size })}
          searchPlaceholder={t('lookups.currenciesSearch')}
          emptyLabel={t('lookups.noCurrencies')}
          isPending={allCurrencies.isPending}
          selectedIds={currencyIds}
          options={(allCurrencies.data?.items ?? []).map((currency) => ({
            id: currency.id,
            label: currency.name,
            // Kept as the muted half so the code stays LTR even in the Arabic layout.
            secondary: `(${currency.code})`,
            searchText: `${currency.code} ${currency.name}`,
          }))}
          onToggle={toggleCurrency}
          testIdPrefix="country-currency"
        />
      </AdminPanel>
    </FormPageLayout>
  );
}
