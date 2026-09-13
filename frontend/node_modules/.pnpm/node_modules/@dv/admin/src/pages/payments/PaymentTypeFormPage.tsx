import { AdminPanel, Alert, Field, Input, LoadingState, Select, cn } from '@dv/ui';
import { useEffect, useState } from 'react';
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
  kind: PaymentMethodKind.Transfer,
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

/** The two ways money can reach us, with the notice each one carries. */
const KINDS = [
  {
    value: PaymentMethodKind.Transfer,
    label: 'payments.kind.Transfer',
    notice: 'payments.kindNotice.Transfer',
  },
  {
    value: PaymentMethodKind.PayPal,
    label: 'payments.kind.PayPal',
    notice: 'payments.kindNotice.PayPal',
  },
] as const;

/**
 * Create or edit one payment type.
 *
 * The kind answers one question only — does the applicant send money to us, or pay through PayPal.
 * Everything a particular provider needs is switched on below it, because those needs combine: one
 * transfer provider wants numbers and QR codes, another wants numbers each naming a bank, a third
 * wants a link as well. They used to be consequences of the kind, which meant a provider that did
 * not fit one of four shapes could not be expressed at all.
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

  const [form, setForm] = useState<UpsertPaymentMethodTypeBody>(EMPTY);
  const save = useSavePaymentMethodType();

  useEffect(() => {
    if (!existing) return;

    setForm({
      id: existing.id,
      nameAr: existing.nameAr,
      nameEn: existing.nameEn,
      kind: existing.kind,
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

          <TypePreview form={form} />
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

      {/* AdminPanel's body applies no spacing of its own, so panels holding more than one block
          space their own children. */}
      <AdminPanel title={t('payments.kindLabel')} subtitle={t('payments.kindHint')}>
        <div className="space-y-5">
          <Field label={t('payments.kindLabel')} htmlFor="type-kind" required>
            <Select
              id="type-kind"
              value={String(form.kind)}
              onChange={(event) => patch({ kind: Number(event.target.value) as PaymentMethodKind })}
              data-testid="payment-type-kind"
            >
              {KINDS.map((option) => (
                <option key={option.value} value={String(option.value)}>
                  {t(option.label)}
                </option>
              ))}
            </Select>
          </Field>

          <Alert variant="info">
            {t(KINDS.find((option) => option.value === form.kind)?.notice ?? '')}
          </Alert>

{/* Switches now, not statements. A transfer type ticks whatever its provider needs, and
              the method editor then shows exactly those panels. */}
          {form.kind === PaymentMethodKind.Transfer ? (
            <div className="space-y-1">
              <FlagToggle
                id="requires-accounts"
                checked={form.requiresAccountNumber}
                label={t('payments.flag.accounts')}
                hint={t('payments.flag.accountsHint')}
                onChange={(requiresAccountNumber) =>
                  patch({
                    requiresAccountNumber,
                    // A QR code and a bank name both hang off a receiving row; without rows there
                    // is nothing for them to belong to.
                    ...(requiresAccountNumber ? {} : { requiresBarcode: false, requiresBank: false }),
                  })
                }
              />

              <FlagToggle
                id="requires-barcode"
                checked={form.requiresBarcode}
                disabled={!form.requiresAccountNumber}
                label={t('payments.flag.barcode')}
                hint={t('payments.flag.barcodeHint')}
                onChange={(requiresBarcode) => patch({ requiresBarcode })}
              />

              <FlagToggle
                id="requires-bank"
                checked={form.requiresBank}
                disabled={!form.requiresAccountNumber}
                label={t('payments.flag.bank')}
                hint={t('payments.flag.bankHint')}
                onChange={(requiresBank) => patch({ requiresBank })}
              />

              <FlagToggle
                id="requires-link"
                checked={form.requiresExternalUrl}
                label={t('payments.flag.link')}
                hint={t('payments.flag.linkHint')}
                onChange={(requiresExternalUrl) => patch({ requiresExternalUrl })}
              />

              {/* Saving would be refused by the server; saying so here costs a round trip less. */}
              {!form.requiresAccountNumber && !form.requiresExternalUrl && (
                <Alert variant="warning">{t('payments.noWayToPay')}</Alert>
              )}
            </div>
          ) : (
            <Alert variant="info">{t('payments.payPalConfigured')}</Alert>
          )}
        </div>
      </AdminPanel>

      <AdminPanel title={t('payments.requires')} subtitle={t('payments.requiresHint')}>
        <div className="space-y-1">
          <FlagToggle
            id="requires-proof"
            checked={form.requiresProofDocument}
            label={t('payments.flag.proof')}
            hint={t('payments.flag.proofHint')}
            onChange={(requiresProofDocument) => patch({ requiresProofDocument })}
          />

          <FlagToggle
            id="requires-reference"
            checked={form.requiresReferenceNumber}
            label={t('payments.flag.reference')}
            hint={t('payments.flag.referenceHint')}
            onChange={(requiresReferenceNumber) => patch({ requiresReferenceNumber })}
          />
        </div>
      </AdminPanel>
    </FormPageLayout>
  );
}


/**
 * One requirement switch. Each states what turning it on does to the applicant's form, because
 * that consequence is the whole reason the switch exists and is invisible from this screen.
 */
function FlagToggle({
  id,
  checked,
  label,
  hint,
  disabled = false,
  onChange,
}: {
  id: string;
  checked: boolean;
  label: string;
  hint: string;
  /** For a switch that only means something once another one is on. */
  disabled?: boolean;
  onChange: (checked: boolean) => void;
}) {
  return (
    <label
      htmlFor={id}
      className={cn(
        'flex items-start gap-3 rounded-xl p-3 transition-colors',
        disabled ? 'cursor-not-allowed opacity-50' : 'cursor-pointer hover:bg-ink-50',
      )}
    >
      <input
        id={id}
        type="checkbox"
        className="mt-0.5 size-4 rounded border-ink-300"
        checked={checked}
        disabled={disabled}
        onChange={(event) => onChange(event.target.checked)}
        data-testid={id}
      />
      <span>
        <span className="block text-sm font-semibold text-ink-900">{label}</span>
        <span className="mt-0.5 block text-xs leading-relaxed text-ink-500">{hint}</span>
      </span>
    </label>
  );
}

/** The deposit form these settings add up to, so their combined effect is visible while editing. */
function TypePreview({ form }: { form: UpsertPaymentMethodTypeBody }) {
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
    isTransferKind(form.kind)
      ? t('payments.preview.approval')
      : t('payments.preview.noApproval'),
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
