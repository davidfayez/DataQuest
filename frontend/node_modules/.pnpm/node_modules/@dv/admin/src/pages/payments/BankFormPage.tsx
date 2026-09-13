import { AdminPanel, Alert, Field, Input, LoadingState, Select } from '@dv/ui';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router-dom';
import { adminSession, Permissions } from '@/features/auth/session';
import { useCountries } from '@/features/lookups/api';
import { useBanks, useSaveBank, type UpsertBankBody } from '@/features/payments/api';
import { useApiErrorMessage } from '@/shared/lib/useApiError';
import { FormPageLayout, StatusPanel } from '@/shared/ui/FormPageLayout';

const LIST_PATH = '/banks';

const EMPTY: UpsertBankBody = {
  countryId: '',
  nameAr: '',
  nameEn: '',
  swiftCode: null,
  sortOrder: 0,
  isActive: true,
};

/** Create or edit one bank in the catalogue. */
export function BankFormPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { id } = useParams<{ id: string }>();
  const toMessage = useApiErrorMessage();

  const isEdit = Boolean(id);

  // Banks have no by-id endpoint; the row is found in the list, as the other lookup forms do.
  const list = useBanks({ page: 1, pageSize: 200 });
  const existing = isEdit ? list.data?.items.find((row) => row.id === id) : undefined;

  const countries = useCountries({ page: 1, pageSize: 300, isActive: true });
  const save = useSaveBank();

  const [form, setForm] = useState<UpsertBankBody>(EMPTY);

  useEffect(() => {
    if (!existing) return;

    setForm({
      id: existing.id,
      countryId: existing.countryId,
      nameAr: existing.nameAr,
      nameEn: existing.nameEn,
      swiftCode: existing.swiftCode,
      sortOrder: existing.sortOrder,
      isActive: existing.isActive,
    });
  }, [existing]);

  const canSubmit = isEdit
    ? adminSession.has(Permissions.PaymentMethodsUpdate)
    : adminSession.has(Permissions.PaymentMethodsCreate);

  function patch(changes: Partial<UpsertBankBody>) {
    setForm((previous) => ({ ...previous, ...changes }));
  }

  function goBack() {
    navigate(LIST_PATH);
  }

  function submit() {
    save.mutate(form, {
      onSuccess: () => navigate(LIST_PATH, { state: { notice: t('lookups.saved') } }),
    });
  }

  if (isEdit && !existing && list.isPending) {
    return <LoadingState label={t('common.loading')} />;
  }

  if (isEdit && !existing && !list.isPending) {
    return (
      <div className="animate-fade-in space-y-6">
        <Alert variant="error">{t('errors.notFound')}</Alert>
      </div>
    );
  }

  return (
    <FormPageLayout
      title={isEdit ? t('banks.editBank') : t('banks.newBank')}
      subtitle={t('banks.formHint')}
      listLabel={t('banks.title')}
      onBack={goBack}
      onSubmit={submit}
      isPending={save.isPending}
      canSubmit={canSubmit}
      error={save.isError ? save.error : null}
      errorMessage={toMessage}
      aside={
        <>
          <StatusPanel isActive={form.isActive} onChange={(isActive) => patch({ isActive })} />

          <AdminPanel title={t('payments.displayPanel')}>
            <Field
              label={t('payments.sortOrder')}
              htmlFor="bank-sort"
              hint={t('banks.sortOrderHint')}
            >
              <Input
                id="bank-sort"
                type="number"
                min={0}
                className="w-28"
                value={form.sortOrder}
                onChange={(event) => patch({ sortOrder: Number(event.target.value) || 0 })}
              />
            </Field>
          </AdminPanel>
        </>
      }
    >
      <AdminPanel title={t('lookups.sectionNames')} subtitle={t('banks.namesHint')}>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t('lookups.nameAr')} htmlFor="bank-name-ar" required>
            <Input
              id="bank-name-ar"
              dir="rtl"
              value={form.nameAr}
              onChange={(event) => patch({ nameAr: event.target.value })}
              data-testid="bank-name-ar"
            />
          </Field>

          <Field label={t('lookups.nameEn')} htmlFor="bank-name-en" required>
            <Input
              id="bank-name-en"
              dir="ltr"
              value={form.nameEn}
              onChange={(event) => patch({ nameEn: event.target.value })}
              data-testid="bank-name-en"
            />
          </Field>
        </div>
      </AdminPanel>

      <AdminPanel title={t('banks.sectionWhere')} subtitle={t('banks.sectionWhereHint')}>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t('lookups.country')} htmlFor="bank-country" required>
            <Select
              id="bank-country"
              value={form.countryId}
              onChange={(event) => patch({ countryId: event.target.value })}
              data-testid="bank-country"
            >
              <option value="">{t('payments.choosePlaceholder')}</option>
              {(countries.data?.items ?? []).map((country) => (
                <option key={country.id} value={country.id}>
                  {country.name}
                </option>
              ))}
            </Select>
          </Field>

          <Field label={t('banks.swift')} htmlFor="bank-swift" hint={t('banks.swiftHint')}>
            <Input
              id="bank-swift"
              dir="ltr"
              maxLength={11}
              className="font-mono uppercase"
              value={form.swiftCode ?? ''}
              onChange={(event) => patch({ swiftCode: event.target.value.toUpperCase() || null })}
              data-testid="bank-swift"
            />
          </Field>
        </div>
      </AdminPanel>
    </FormPageLayout>
  );
}
