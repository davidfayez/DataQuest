import { AdminPanel, Alert, Field, Input, LoadingState } from '@dv/ui';
import { useEffect, useState, type TextareaHTMLAttributes } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router-dom';
import { adminSession, Permissions } from '@/features/auth/session';
import {
  isTransferKind,
  PaymentMethodKind,
  usePaymentMethodTypes,
  useSavePaymentMethodType,
  type UpsertPaymentMethodTypeBody,
} from '@/features/payments/api';
import { useApiErrorMessage } from '@/shared/lib/useApiError';
import { FormPageLayout, StatusPanel } from '@/shared/ui/FormPageLayout';

const LIST_PATH = '/payments/types';

const EMPTY: UpsertPaymentMethodTypeBody = {
  nameAr: '',
  nameEn: '',
  descriptionAr: '',
  descriptionEn: '',
  // Receiving numbers on by default: it is what nearly every transfer provider needs, and a type
  // with nothing switched on cannot be saved.
  requiresAccountNumber: true,
  requiresBarcode: false,
  requiresBank: false,
  requiresExternalUrl: false,
  requiresProofDocument: true,
  requiresReferenceNumber: true,
  sortOrder: 0,
  isActive: true,
};

/**
 * Create or edit one payment type.
 *
 * There is no kind to choose: a type created here is a transfer — the applicant sends money to us
 * and a reviewer confirms it — and what its provider needs is switched on below. An existing type
 * keeps the kind it has, which is how the seeded PayPal type still shows as PayPal.
 */
export function PaymentTypeFormPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { id } = useParams<{ id: string }>();
  const toMessage = useApiErrorMessage();

  const isEdit = Boolean(id);

  // Types have no by-id endpoint, and there are only ever a handful, so the row is found in the
  // list — the same approach the other lookup form pages take.
  const list = usePaymentMethodTypes({ page: 1, pageSize: 200 });
  const existing = isEdit ? list.data?.items.find((row) => row.id === id) : undefined;

  // Read-only now: shown through what the page offers, never chosen on it.
  const kind = existing?.kind ?? PaymentMethodKind.Transfer;
  const isTransfer = isTransferKind(kind);

  const [form, setForm] = useState<UpsertPaymentMethodTypeBody>(EMPTY);
  const save = useSavePaymentMethodType();

  useEffect(() => {
    if (!existing) return;

    setForm({
      id: existing.id,
      nameAr: existing.nameAr,
      nameEn: existing.nameEn,
      descriptionAr: existing.descriptionAr ?? '',
      descriptionEn: existing.descriptionEn ?? '',
      requiresAccountNumber: existing.requiresAccountNumber,
      requiresBarcode: existing.requiresBarcode,
      requiresBank: existing.requiresBank,
      requiresExternalUrl: existing.requiresExternalUrl,
      requiresProofDocument: existing.requiresProofDocument,
      requiresReferenceNumber: existing.requiresReferenceNumber,
      sortOrder: existing.sortOrder,
      isActive: existing.isActive,
    });
  }, [existing]);

  const canSubmit = isEdit
    ? adminSession.has(Permissions.PaymentMethodsUpdate)
    : adminSession.has(Permissions.PaymentMethodsCreate);

  function patch(changes: Partial<UpsertPaymentMethodTypeBody>) {
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
      title={isEdit ? t('payments.editType') : t('payments.newType')}
      subtitle={t('payments.typeFormHint')}
      listLabel={t('payments.typesTitle')}
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
              htmlFor="type-sort"
              hint={t('payments.sortOrderHint')}
            >
              <Input
                id="type-sort"
                type="number"
                min={0}
                className="w-28"
                value={form.sortOrder}
                onChange={(event) => patch({ sortOrder: Number(event.target.value) || 0 })}
              />
            </Field>
          </AdminPanel>

          <TypePreview form={form} needsApproval={isTransfer} />
        </>
      }
    >
      <AdminPanel title={t('lookups.sectionNames')} subtitle={t('payments.typeNamesHint')}>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t('lookups.nameAr')} htmlFor="type-name-ar" required>
            <Input
              id="type-name-ar"
              dir="rtl"
              value={form.nameAr}
              onChange={(event) => patch({ nameAr: event.target.value })}
              data-testid="type-name-ar"
            />
          </Field>

          <Field label={t('lookups.nameEn')} htmlFor="type-name-en" required>
            <Input
              id="type-name-en"
              dir="ltr"
              value={form.nameEn}
              onChange={(event) => patch({ nameEn: event.target.value })}
              data-testid="type-name-en"
            />
          </Field>
        </div>
      </AdminPanel>

      <AdminPanel title={t('payments.description')} subtitle={t('payments.typeDescriptionHint')}>
        <div className="grid gap-4 sm:grid-cols-2">
          <Field label={t('lookups.descriptionAr')} htmlFor="type-description-ar" required>
            <DescriptionArea
              id="type-description-ar"
              dir="rtl"
              value={form.descriptionAr}
              onChange={(event) => patch({ descriptionAr: event.target.value })}
              data-testid="type-description-ar"
            />
          </Field>

          <Field label={t('lookups.descriptionEn')} htmlFor="type-description-en" required>
            <DescriptionArea
              id="type-description-en"
              dir="ltr"
              value={form.descriptionEn}
              onChange={(event) => patch({ descriptionEn: event.target.value })}
              data-testid="type-description-en"
            />
          </Field>
        </div>
      </AdminPanel>

    </FormPageLayout>
  );
}

/** A multi-line box styled like the package's inputs; the shared UI has no textarea of its own. */
function DescriptionArea(props: TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return (
    <textarea
      rows={4}
      maxLength={2000}
      {...props}
      className="flex w-full rounded-xl border-0 bg-white px-3.5 py-2.5 text-sm text-ink-900 shadow-soft ring-1 ring-ink-200 transition-shadow placeholder:text-ink-300 focus:outline-none focus:ring-2 focus:ring-brand-500"
    />
  );
}

/** The deposit form these settings add up to, so their combined effect is visible while editing. */
function TypePreview({
  form,
  needsApproval,
}: {
  form: UpsertPaymentMethodTypeBody;
  needsApproval: boolean;
}) {
  const { t } = useTranslation();

  const steps = [
    form.requiresExternalUrl && t('payments.preview.openLink'),
    t('payments.preview.amount'),
    form.requiresBank
      ? t('payments.preview.bankAccount')
      : form.requiresAccountNumber && t('payments.preview.account'),
    form.requiresBarcode && t('payments.preview.scan'),
    form.requiresReferenceNumber && t('payments.preview.reference'),
    form.requiresProofDocument && t('payments.preview.proof'),
    needsApproval ? t('payments.preview.approval') : t('payments.preview.noApproval'),
  ].filter(Boolean) as string[];

  return (
    <AdminPanel title={t('payments.previewTitle')} subtitle={t('payments.previewHint')}>
      <ol className="space-y-2">
        {steps.map((step, index) => (
          <li key={step} className="flex gap-2.5 text-sm">
            <span className="mt-0.5 flex size-5 shrink-0 items-center justify-center rounded-full bg-ink-100 text-xs font-semibold text-ink-600">
              {index + 1}
            </span>
            <span className="leading-relaxed text-ink-700">{step}</span>
          </li>
        ))}
      </ol>
    </AdminPanel>
  );
}
