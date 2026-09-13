import { AdminPageHeader, AdminPanel, Alert, Button, Field, Input, LoadingState } from '@dv/ui';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import { adminSession, Permissions } from '@/features/auth/session';
import {
  useCountries,
  useCurrencies,
  useSaveLookup,
  type CurrencyDto,
} from '@/features/lookups/api';
import { useApiErrorMessage } from '@/shared/lib/useApiError';
import { FormPageLayout, StatusPanel, SummaryPanel } from '@/shared/ui/FormPageLayout';
import { IdChecklist, LocalizedNameFields } from './tabs/shared';

interface CurrencyBody {
  id?: string;
  code: string;
  symbol: string;
  nameAr: string;
  nameEn: string;
  isActive: boolean;
  countryIds: string[];
}

const EMPTY: CurrencyBody = {
  code: '',
  symbol: '',
  nameAr: '',
  nameEn: '',
  isActive: true,
  countryIds: [],
};

const LIST_PATH = '/lookups/currencies';

/**
 * Create or edit a currency on its own page rather than in a dialog.
 *
 * The row is handed over in router state when arriving from the list. Opening the URL directly
 * has no state, so the list is fetched and the record found in it — lookups have no by-id
 * endpoint.
 */
export function CurrencyFormPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { id } = useParams<{ id: string }>();
  const location = useLocation();
  const toMessage = useApiErrorMessage();

  const isEdit = Boolean(id);
  const handedOver = (location.state as { currency?: CurrencyDto } | null)?.currency;

  const list = useCurrencies({ page: 1, pageSize: 200 });
  const existing =
    handedOver ?? (isEdit ? list.data?.items.find((row) => row.id === id) : undefined);

  const countries = useCountries({ page: 1, pageSize: 200, isActive: true });
  const [form, setForm] = useState<CurrencyBody>(EMPTY);
  const [countryIds, setCountryIds] = useState<Set<string>>(new Set());

  const save = useSaveLookup<CurrencyBody, CurrencyDto>('currencies');

  useEffect(() => {
    if (!existing) return;

    setForm({
      id: existing.id,
      code: existing.code,
      symbol: existing.symbol,
      nameAr: existing.nameAr,
      nameEn: existing.nameEn,
      isActive: existing.isActive,
      countryIds: existing.countryIds ?? [],
    });
    setCountryIds(new Set(existing.countryIds ?? []));
  }, [existing]);

  const canSubmit = isEdit
    ? adminSession.has(Permissions.CurrenciesUpdate)
    : adminSession.has(Permissions.CurrenciesCreate);

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
        <AdminPageHeader title={t('lookups.currencies')} subtitle={t('lookups.subtitle')} />
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
          ? t('lookups.editTitle', { entity: t('lookups.currency') })
          : t('lookups.createTitle', { entity: t('lookups.currency') })
      }
      subtitle={t('lookups.currencyFormHint')}
      listLabel={t('lookups.currencies')}
      onBack={goBack}
      onSubmit={submit}
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
            title={t('lookups.countries')}
            items={selectedCountryNames}
            emptyLabel={t('lookups.summaryEmpty')}
          />
        </>
      }
    >
      <AdminPanel title={t('lookups.sectionMoney')} subtitle={t('lookups.sectionMoneyHint')}>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t('lookups.code')} htmlFor="code" required hint="ISO 4217">
            <Input
              id="code"
              dir="ltr"
              maxLength={3}
              className="uppercase"
              value={form.code}
              onChange={(event) => setForm({ ...form, code: event.target.value.toUpperCase() })}
            />
          </Field>

          <Field label={t('lookups.symbol')} htmlFor="symbol" required>
            <Input
              id="symbol"
              value={form.symbol}
              onChange={(event) => setForm({ ...form, symbol: event.target.value })}
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

      <AdminPanel title={t('lookups.sectionAccepted')} subtitle={t('lookups.sectionAcceptedHint')}>
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
          testIdPrefix="currency-country"
        />
      </AdminPanel>
    </FormPageLayout>
  );
}
