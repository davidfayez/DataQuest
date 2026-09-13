import {
  AdminPageHeader,
  AdminPanel,
  Alert,
  Button,
  Field,
  LoadingState,
  MultiSelect,
  SearchableSelect,
} from '@dv/ui';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useLocation, useNavigate, useParams } from 'react-router-dom';
import { adminSession, Permissions } from '@/features/auth/session';
import {
  useAuthorities,
  useCountries,
  useSaveLookup,
  useSubTransactionTypes,
  useTransactionTypes,
  type AuthorityDto,
} from '@/features/lookups/api';
import { useApiErrorMessage } from '@/shared/lib/useApiError';
import { FormPageLayout, StatusPanel, SummaryPanel } from '@/shared/ui/FormPageLayout';
import { LocalizedDescriptionFields, LocalizedNameFields, LookupCodeField, isValidLookupCode } from './tabs/shared';

interface AuthorityBody {
  id?: string;
  code: string;
  countryId: string;
  nameAr: string;
  nameEn: string;
  descriptionAr: string;
  descriptionEn: string;
  isActive: boolean;
  subTransactionTypeIds: string[];
}

const EMPTY: AuthorityBody = {
  code: '',
  countryId: '',
  nameAr: '',
  nameEn: '',
  descriptionAr: '',
  descriptionEn: '',
  isActive: true,
  subTransactionTypeIds: [],
};

const LIST_PATH = '/lookups/authorities';

/**
 * Create or edit a verification authority on its own page rather than in a dialog.
 *
 * The row is handed over in router state when arriving from the list, which is the common path.
 * Opening the URL directly (a bookmark, a refresh) has no state, so the list is fetched and the
 * record found in it — there is no by-id endpoint for lookups.
 */
export function AuthorityFormPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { id } = useParams<{ id: string }>();
  const location = useLocation();
  const toMessage = useApiErrorMessage();

  const isEdit = Boolean(id);
  const handedOver = (location.state as { authority?: AuthorityDto } | null)?.authority;

  // Only fetched when the record was not handed over, so the common path costs nothing extra.
  const list = useAuthorities({ page: 1, pageSize: 200 });
  const existing =
    handedOver ?? (isEdit ? list.data?.items.find((row) => row.id === id) : undefined);

  const countries = useCountries({ page: 1, pageSize: 200, isActive: true });
  const [form, setForm] = useState<AuthorityBody>(EMPTY);

  // Only sub-types from the chosen country may be mapped — the server rejects anything else.
  const transactionTypes = useTransactionTypes({ page: 1, countryId: form.countryId || undefined });
  const subTypes = useSubTransactionTypes({ page: 1, pageSize: 200 });

  const save = useSaveLookup<AuthorityBody, AuthorityDto>('authorities');

  useEffect(() => {
    // A row saved before descriptions existed arrives with nulls; the form works in strings.
    if (existing) {
      setForm({
        ...existing,
        id: existing.id,
        code: existing.code ?? '',
        descriptionAr: existing.descriptionAr ?? '',
        descriptionEn: existing.descriptionEn ?? '',
      });
    }
  }, [existing]);

  const canSubmit = isEdit
    ? adminSession.has(Permissions.AuthoritiesUpdate)
    : adminSession.has(Permissions.AuthoritiesCreate);

  // Both descriptions are required. Kept apart from canSubmit, which is about permission: an
  // empty field should hold Save back, not tell the admin they only have view access.
  const descriptionsComplete =
    form.descriptionAr.trim() !== '' && form.descriptionEn.trim() !== '';

  const countryTransactionIds = new Set((transactionTypes.data?.items ?? []).map((type) => type.id));
  const transactionTypeName = (typeId: string) =>
    transactionTypes.data?.items.find((type) => type.id === typeId)?.name ?? '—';

  const subTypeOptions = !form.countryId
    ? []
    : (subTypes.data?.items ?? [])
        .filter((subType) => countryTransactionIds.has(subType.transactionTypeId))
        .map((subType) => ({
          value: subType.id,
          label: `${transactionTypeName(subType.transactionTypeId)} - ${subType.name}`,
          keywords: `${subType.nameEn} ${subType.nameAr}`,
        }))
        .sort((a, b) => a.label.localeCompare(b.label));

  function goBack() {
    navigate(LIST_PATH);
  }

  function submit() {
    save.mutate(form, {
      // The notice belongs to the list, so it travels back in router state.
      onSuccess: () => navigate(LIST_PATH, { state: { notice: t('lookups.saved') } }),
    });
  }

  // Editing something that is not in the first page of the list, and was not handed over.
  if (isEdit && !existing && !list.isPending) {
    return (
      <div className="animate-fade-in space-y-6">
        <AdminPageHeader title={t('lookups.authorities')} subtitle={t('lookups.subtitle')} />
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

  // The chips in the summary panel read back the same labels the picker offered.
  const selectedSubTypeLabels = form.subTransactionTypeIds
    .map((selectedId) => subTypeOptions.find((option) => option.value === selectedId)?.label)
    .filter((label): label is string => Boolean(label));

  // The heading names one record, so it uses the singular entity — the list button stays plural.
  return (
    <FormPageLayout
      title={
        isEdit
          ? t('lookups.editTitle', { entity: t('lookups.authority') })
          : t('lookups.createTitle', { entity: t('lookups.authority') })
      }
      subtitle={t('lookups.authorityFormHint')}
      listLabel={t('lookups.authorities')}
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
            title={t('lookups.subTypes')}
            items={selectedSubTypeLabels}
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

      {/* allowOverflow: both panels hold a dropdown, which the panel's clip would slice off. */}
      <AdminPanel
        title={t('lookups.sectionCoverage')}
        subtitle={t('lookups.sectionCoverageHint')}
        allowOverflow
      >
        <Field label={t('lookups.country')} htmlFor="countryId" required className="max-w-md">
          <SearchableSelect
            id="countryId"
            value={form.countryId}
            onChange={(countryId) =>
              // Changing country invalidates the mapping, which is scoped to that country.
              setForm({ ...form, countryId, subTransactionTypeIds: [] })
            }
            options={(countries.data?.items ?? []).map((country) => ({
              value: country.id,
              label: `${country.name} (${country.code})`,
            }))}
            placeholder={t('lookups.selectCountry')}
            searchPlaceholder={t('lookups.countriesSearch')}
            emptyMessage={t('lookups.noCountries')}
          />
        </Field>
      </AdminPanel>

      <AdminPanel
        title={t('lookups.sectionHandles')}
        subtitle={t('lookups.sectionHandlesHint')}
        allowOverflow
      >
        <Field
          label={t('lookups.subTypes')}
          htmlFor="subTransactionTypeIds"
          hint={form.countryId ? undefined : t('lookups.selectCountryForSubTypes')}
        >
          <MultiSelect
            id="subTransactionTypeIds"
            values={form.subTransactionTypeIds}
            onChange={(subTransactionTypeIds) => setForm({ ...form, subTransactionTypeIds })}
            options={subTypeOptions}
            placeholder={t('lookups.selectSubTypes')}
            searchPlaceholder={t('lookups.subTypesSearch')}
            emptyMessage={
              form.countryId
                ? t('lookups.noSubTypesForCountry')
                : t('lookups.selectCountryForSubTypes')
            }
            summaryLabel={(count) => t('lookups.subTypesSelected', { count })}
          />
        </Field>
      </AdminPanel>
    </FormPageLayout>
  );
}
