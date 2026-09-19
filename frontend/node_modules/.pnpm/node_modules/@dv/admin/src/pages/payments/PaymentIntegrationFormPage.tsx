import { AdminPanel, Alert, Field, Input, LoadingState, SearchableSelect, Select, cn } from '@dv/ui';
import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router-dom';
import { adminSession, Permissions } from '@/features/auth/session';
import { PaymentIntegrationMode } from '@/features/payments/api';
import {
  EMPTY_INTEGRATION_BODY,
  usePaymentGatewayIntegration,
  usePaymentGateways,
  useSavePaymentGatewayIntegration,
  type UpsertPaymentGatewayIntegrationBody,
} from '@/features/payments/integrations';
import { useApiErrorMessage } from '@/shared/lib/useApiError';
import { FormPageLayout, StatusPanel } from '@/shared/ui/FormPageLayout';
import { GatewaySummary, TextArea } from './GatewaySettingsFields';

const LIST_PATH = '/payments/integrations';

/**
 * Create or edit one gateway integration.
 *
 * An integration names a gateway and says whether it is a test or a live account — nothing more.
 * The credentials and settings are filled in on each payment method that uses it, because two
 * methods on the same gateway are usually two different merchant accounts.
 */
export function PaymentIntegrationFormPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { id } = useParams<{ id: string }>();
  const toMessage = useApiErrorMessage();

  const isEdit = Boolean(id);
  const existing = usePaymentGatewayIntegration(id);
  const gateways = usePaymentGateways();
  const save = useSavePaymentGatewayIntegration();

  const [form, setForm] = useState<UpsertPaymentGatewayIntegrationBody>(EMPTY_INTEGRATION_BODY);
  const [showErrors, setShowErrors] = useState(false);

  useEffect(() => {
    const row = existing.data;
    if (!row) return;

    setForm({
      id: row.id,
      nameAr: row.nameAr,
      nameEn: row.nameEn,
      descriptionAr: row.descriptionAr ?? '',
      descriptionEn: row.descriptionEn ?? '',
      gatewayCode: row.gatewayCode,
      mode: row.mode,
      sortOrder: row.sortOrder,
      isActive: row.isActive,
    });
  }, [existing.data]);

  const catalogue = useMemo(() => gateways.data ?? [], [gateways.data]);
  const gateway = catalogue.find((item) => item.code === form.gatewayCode);
  const live = form.mode === PaymentIntegrationMode.Live;
  const inUse = (existing.data?.methodCount ?? 0) > 0;

  const canSubmit = isEdit
    ? adminSession.has(Permissions.PaymentMethodsUpdate)
    : adminSession.has(Permissions.PaymentMethodsCreate);

  function patch(changes: Partial<UpsertPaymentGatewayIntegrationBody>) {
    setForm((previous) => ({ ...previous, ...changes }));
  }

  const isComplete =
    Boolean(gateway) &&
    form.nameAr.trim() !== '' &&
    form.nameEn.trim() !== '' &&
    form.descriptionAr.trim() !== '' &&
    form.descriptionEn.trim() !== '';

  function submit() {
    if (!isComplete) {
      setShowErrors(true);
      return;
    }

    save.mutate(form, {
      onSuccess: () => navigate(LIST_PATH, { state: { notice: t('lookups.saved') } }),
    });
  }

  if (isEdit && existing.isPending) return <LoadingState label={t('common.loading')} />;

  if (isEdit && existing.isError) {
    return (
      <div className="animate-fade-in space-y-6">
        <Alert variant="error">{toMessage(existing.error)}</Alert>
      </div>
    );
  }

  const gatewayOptions = catalogue.map((item) => ({
    value: item.code,
    label: `${item.name} — ${t(`integrations.category.${item.categoryName}`)}`,
    triggerLabel: item.name,
    keywords: `${item.code} ${item.region} ${t(`integrations.category.${item.categoryName}`)}`,
  }));

  return (
    <FormPageLayout
      title={isEdit ? t('integrations.edit') : t('integrations.new')}
      subtitle={t('integrations.formHint')}
      listLabel={t('integrations.title')}
      onBack={() => navigate(LIST_PATH)}
      onSubmit={submit}
      isPending={save.isPending}
      canSubmit={canSubmit}
      error={save.isError ? save.error : null}
      errorMessage={toMessage}
      aside={
        <>
          <StatusPanel isActive={form.isActive} onChange={(isActive) => patch({ isActive })} />

          <AdminPanel title={t('payments.displayPanel')}>
            <Field label={t('payments.sortOrder')} htmlFor="integration-sort" hint={t('payments.sortOrderHint')}>
              <Input
                id="integration-sort"
                type="number"
                min={0}
                className="w-28"
                value={form.sortOrder}
                onChange={(event) => patch({ sortOrder: Number(event.target.value) || 0 })}
              />
            </Field>
          </AdminPanel>

          {gateway && <GatewaySummary gateway={gateway} />}

          {isEdit && inUse && (
            <Alert variant="info">
              {t('integrations.usedBy', { count: existing.data!.methodCount })}
            </Alert>
          )}
        </>
      }
    >
      <AdminPanel title={t('lookups.sectionNames')} subtitle={t('integrations.namesHint')}>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t('lookups.nameAr')} htmlFor="integration-name-ar" required>
            <Input
              id="integration-name-ar"
              dir="rtl"
              maxLength={200}
              value={form.nameAr}
              invalid={showErrors && form.nameAr.trim() === ''}
              onChange={(event) => patch({ nameAr: event.target.value })}
              data-testid="integration-name-ar"
            />
          </Field>
          <Field label={t('lookups.nameEn')} htmlFor="integration-name-en" required>
            <Input
              id="integration-name-en"
              dir="ltr"
              maxLength={200}
              value={form.nameEn}
              invalid={showErrors && form.nameEn.trim() === ''}
              onChange={(event) => patch({ nameEn: event.target.value })}
              data-testid="integration-name-en"
            />
          </Field>
          <Field label={t('lookups.descriptionAr')} htmlFor="integration-description-ar" required>
            <TextArea
              id="integration-description-ar"
              dir="rtl"
              value={form.descriptionAr}
              onChange={(event) => patch({ descriptionAr: event.target.value })}
              data-testid="integration-description-ar"
            />
          </Field>
          <Field label={t('lookups.descriptionEn')} htmlFor="integration-description-en" required>
            <TextArea
              id="integration-description-en"
              dir="ltr"
              value={form.descriptionEn}
              onChange={(event) => patch({ descriptionEn: event.target.value })}
              data-testid="integration-description-en"
            />
          </Field>
        </div>
        {showErrors && !isComplete && (
          <p className="mt-3 text-xs text-destructive">{t('integrations.namesRequired')}</p>
        )}
      </AdminPanel>

      {/* allowOverflow: the gateway list is a dropdown, and the panel's clip would slice it off at
          the card's edge. */}
      <AdminPanel
        title={t('integrations.gatewaySection')}
        subtitle={t('integrations.gatewaySectionHint')}
        allowOverflow
      >
        <div className="grid gap-4 sm:grid-cols-[minmax(0,1fr)_14rem]">
          <Field label={t('integrations.gateway')} htmlFor="integration-gateway" required>
            <SearchableSelect
              id="integration-gateway"
              options={gatewayOptions}
              value={form.gatewayCode}
              onChange={(gatewayCode) => patch({ gatewayCode })}
              placeholder={gateways.isPending ? t('common.loading') : t('integrations.chooseGateway')}
              searchPlaceholder={t('integrations.searchGateway')}
              emptyMessage={t('integrations.noGateway')}
              disabled={inUse}
              invalid={showErrors && !gateway}
              aria-label={t('integrations.gateway')}
            />
          </Field>

          <Field label={t('payments.integrationMode')} htmlFor="integration-mode" required>
            <Select
              id="integration-mode"
              value={String(form.mode)}
              onChange={(event) => patch({ mode: Number(event.target.value) })}
              className={cn(live && 'ring-red-300 text-red-700')}
              data-testid="integration-mode"
            >
              <option value={PaymentIntegrationMode.Sandbox}>{t('payments.modeSandbox')}</option>
              <option value={PaymentIntegrationMode.Live}>{t('payments.modeLive')}</option>
            </Select>
          </Field>
        </div>

        <p className="mt-2 text-xs text-muted-foreground">
          {live ? t('payments.modeLiveHint') : t('payments.modeSandboxHint')}
        </p>

        {/* Changing it would leave those methods holding settings for a gateway nobody asked for,
            so the server refuses it too. */}
        {inUse && <Alert variant="info" className="mt-4">{t('integrations.gatewayLocked')}</Alert>}
      </AdminPanel>

      <Alert variant="info">{t('integrations.settingsOnMethod')}</Alert>
    </FormPageLayout>
  );
}
