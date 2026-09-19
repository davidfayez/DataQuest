import { AdminPanel, Alert, Field, Input, LoadingState, Select } from '@dv/ui';
import { Eye, EyeOff, Plug } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useLocation, useNavigate, useParams } from 'react-router-dom';
import { adminSession, Permissions } from '@/features/auth/session';
import { useCountries, useCurrencies } from '@/features/lookups/api';
import { useUploadRequiredFileSample } from '@/features/lookups/requiredFileSamples';
import {
  usePaymentMethod,
  usePaymentMethodTypes,
  useSavePaymentMethod,
  type PaymentMethodAccountInput,
  type UpsertPaymentMethodBody,
} from '@/features/payments/api';
import {
  usePaymentGatewayIntegrations,
  usePaymentGateways,
  type PaymentGatewayIntegrationDto,
} from '@/features/payments/integrations';
import { useApiErrorMessage } from '@/shared/lib/useApiError';
import { FormPageLayout, StatusPanel, SummaryPanel } from '@/shared/ui/FormPageLayout';
import { RequiredDocumentsEditor } from '../lookups/tabs/RequiredDocumentsEditor';
import {
  stripDocumentExtras,
  toRequiredFileInput,
  uploadPendingReferenceFiles,
} from '../lookups/tabs/requiredDocuments';
import { IdChecklist } from '../lookups/tabs/shared';
import { AccountsEditor } from './AccountsEditor';
import {
  GatewaySettingsPanels,
  gatewayFieldErrors,
  settingsToSubmit,
} from './GatewaySettingsFields';
import { ModeBadge } from './IntegrationBadges';
import { NotificationEmailsEditor } from './NotificationEmailsEditor';

const LIST_PATH = '/payments/methods';

const EMPTY: UpsertPaymentMethodBody = {
  paymentMethodTypeId: '',
  nameAr: '',
  nameEn: '',
  descriptionAr: null,
  descriptionEn: null,
  publicNoteAr: null,
  publicNoteEn: null,
  privateNoteAr: null,
  privateNoteEn: null,
  externalUrl: null,
  sortOrder: 0,
  isActive: true,
  countryIds: [],
  currencyIds: [],
  accounts: [],
  notificationEmails: [],
  requiredFiles: [],
  gatewayIntegrationId: null,
  gatewaySettings: {},
  gatewaySecrets: {},
};

/** Carried to the edit page when the method saved but some of its reference files did not. */
interface FormLocationState {
  referenceFailures?: string[];
}

/**
 * Create or edit one payment method.
 *
 * The page is generated from the chosen type rather than showing every field it could ever need:
 * pick "External Payment Link" and the accounts editor disappears in favour of a URL, pick
 * "InstaPay" and each account grows a barcode uploader. That is the same set of flags the
 * applicant's deposit form reads, so what an admin configures here is exactly what an applicant is
 * asked for — which is why the right-hand column previews it in words.
 */
export function PaymentMethodFormPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const { id } = useParams<{ id: string }>();
  const toMessage = useApiErrorMessage();

  const isEdit = Boolean(id);

  const existing = usePaymentMethod(id);
  const types = usePaymentMethodTypes({ page: 1, pageSize: 100, isActive: true });
  const countries = useCountries({ page: 1, pageSize: 300, isActive: true });
  const currencies = useCurrencies({ page: 1, pageSize: 200, isActive: true });
  const save = useSavePaymentMethod();
  const uploadSample = useUploadRequiredFileSample('paymentMethod');
  const location = useLocation();

  const [form, setForm] = useState<UpsertPaymentMethodBody>(EMPTY);
  const [isUploadingSamples, setIsUploadingSamples] = useState(false);
  const referenceFailures = (location.state as FormLocationState | null)?.referenceFailures ?? [];

  useEffect(() => {
    const row = existing.data;
    if (!row) return;

    setForm({
      id: row.id,
      paymentMethodTypeId: row.paymentMethodTypeId,
      nameAr: row.nameAr,
      nameEn: row.nameEn,
      descriptionAr: row.descriptionAr,
      descriptionEn: row.descriptionEn,
      publicNoteAr: row.publicNoteAr,
      publicNoteEn: row.publicNoteEn,
      privateNoteAr: row.privateNoteAr,
      privateNoteEn: row.privateNoteEn,
      externalUrl: row.externalUrl,
      sortOrder: row.sortOrder,
      isActive: row.isActive,
      countryIds: row.countryIds,
      currencyIds: row.currencyIds,
      accounts: row.accounts.map((account) => ({
        id: account.id,
        labelAr: account.labelAr,
        labelEn: account.labelEn,
        accountNumber: account.accountNumber,
        accountHolder: account.accountHolder,
        bankId: account.bankId,
        isActive: account.isActive,
        sortOrder: account.sortOrder,
      })),
      notificationEmails: row.notificationEmails.map((recipient) => ({
        id: recipient.id,
        email: recipient.email,
        displayName: recipient.displayName,
        notifyOnSubmitted: recipient.notifyOnSubmitted,
        notifyOnApproved: recipient.notifyOnApproved,
        notifyOnRejected: recipient.notifyOnRejected,
      })),
      requiredFiles: row.requiredFiles.map(toRequiredFileInput),
      gatewayIntegrationId: row.gatewayIntegrationId,
      gatewaySettings: { ...row.gatewaySettings },
      // Nothing submitted: every stored secret is kept unless this edit changes it.
      gatewaySecrets: {},
    });
  }, [existing.data]);

  const typeList = types.data?.items ?? [];
  const selectedType = typeList.find((type) => type.id === form.paymentMethodTypeId);
  // What the type asks for, not what kind it is: a transfer type may want a link as well as
  // receiving numbers, and the editor has to offer both.
  const wantsLink = selectedType?.requiresExternalUrl ?? false;

  // The gateway this method pays through, and what it asks for. Offered on every method: a manual
  // transfer can be reconciled against a gateway too, and the type's own flags no longer decide it.
  const integrations = usePaymentGatewayIntegrations({ page: 1, pageSize: 200, isActive: true });
  const gateways = usePaymentGateways();
  const chosenIntegration = integrations.data?.items.find(
    (item) => item.id === form.gatewayIntegrationId,
  );
  // Falls back to what the method was saved with, so a retired integration still shows its fields.
  const gatewayCode = chosenIntegration?.gatewayCode
    ?? (existing.data?.gatewayIntegrationId === form.gatewayIntegrationId
      ? existing.data?.gatewayCode ?? undefined
      : undefined);
  const gateway = gateways.data?.find((item) => item.code === gatewayCode);
  const gatewayIsLive =
    (chosenIntegration?.modeName ?? existing.data?.gatewayModeName) === 'Live';

  // Secrets already stored on this method, but only while it stays on the same gateway.
  const storedGatewaySecrets = useMemo(
    () =>
      existing.data && existing.data.gatewayIntegrationId === form.gatewayIntegrationId
        ? new Set(existing.data.configuredGatewaySecrets)
        : new Set<string>(),
    [existing.data, form.gatewayIntegrationId],
  );

  const [showGatewayErrors, setShowGatewayErrors] = useState(false);
  const gatewayErrors = useMemo(
    () =>
      gatewayFieldErrors(
        gateway,
        form.gatewaySettings ?? {},
        form.gatewaySecrets ?? {},
        storedGatewaySecrets,
        gatewayIsLive,
        t,
      ),
    [gateway, form.gatewaySettings, form.gatewaySecrets, storedGatewaySecrets, gatewayIsLive, t],
  );

  const selectedCountries = useMemo(() => new Set(form.countryIds), [form.countryIds]);

  /**
   * Currencies are offered only where one of the chosen countries actually uses them — the same
   * rule the server enforces. Choosing no country yet offers nothing, which is what makes the
   * dependency legible instead of surprising.
   */
  const availableCurrencies = useMemo(
    () =>
      (currencies.data?.items ?? []).filter((currency) =>
        currency.countryIds.some((countryId) => selectedCountries.has(countryId)),
      ),
    [currencies.data, selectedCountries],
  );

  // Narrowing the countries can strand a currency none of them uses; the server would refuse the
  // save, so the selection is pruned as the countries change rather than at submit time.
  useEffect(() => {
    const allowed = new Set(availableCurrencies.map((currency) => currency.id));
    const kept = form.currencyIds.filter((currencyId) => allowed.has(currencyId));

    if (kept.length !== form.currencyIds.length) {
      setForm((previous) => ({ ...previous, currencyIds: kept }));
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [availableCurrencies]);

  const canSubmit = isEdit
    ? adminSession.has(Permissions.PaymentMethodsUpdate)
    : adminSession.has(Permissions.PaymentMethodsCreate);

  function patch(changes: Partial<UpsertPaymentMethodBody>) {
    setForm((previous) => ({ ...previous, ...changes }));
  }

  function toggleId(key: 'countryIds' | 'currencyIds', value: string) {
    setForm((previous) => {
      const next = previous[key].includes(value)
        ? previous[key].filter((item) => item !== value)
        : [...previous[key], value];
      return { ...previous, [key]: next };
    });
  }

  function goBack() {
    navigate(LIST_PATH);
  }

  async function submit() {
    // The gateway would refuse these, and it says so beside each field rather than after a save.
    if (gateway && Object.keys(gatewayErrors).length > 0) {
      setShowGatewayErrors(true);
      return;
    }

    const documents = form.requiredFiles ?? [];
    let saved;
    try {
      saved = await save.mutateAsync({
        ...form,
        // An external link has nothing to transfer to, and a transfer has nowhere to send anyone.
        accounts: selectedType?.requiresAccountNumber ? form.accounts : [],
        externalUrl: wantsLink ? form.externalUrl : null,
        requiredFiles: stripDocumentExtras(documents),
        // The generic credentials panel this page used to carry is gone; its stored values are
        // left untouched rather than cleared.
        integration: undefined,
        gatewayIntegrationId: form.gatewayIntegrationId || null,
        gatewaySettings: settingsToSubmit(gateway, form.gatewaySettings ?? {}),
        gatewaySecrets: form.gatewaySecrets ?? {},
      });
    } catch {
      // Shown by the page's own error alert.
      return;
    }

    // Reference files chosen on documents that were new until this save go up now that the
    // documents have ids to belong to.
    setIsUploadingSamples(true);
    const { failures } = await uploadPendingReferenceFiles(
      documents,
      saved.requiredFiles,
      (input) => uploadSample.mutateAsync(input),
      toMessage,
    );
    setIsUploadingSamples(false);

    if (failures.length === 0) {
      navigate(LIST_PATH, { state: { notice: t('lookups.saved') } });
      return;
    }

    // The method exists now either way: reopen it, saying which files still need adding.
    navigate(`${LIST_PATH}/${saved.id}/edit`, {
      replace: true,
      state: { referenceFailures: failures } satisfies FormLocationState,
    });
    if (isEdit) void existing.refetch();
  }

  if (isEdit && existing.isPending) {
    return <LoadingState label={t('common.loading')} />;
  }

  if (isEdit && existing.isError) {
    return (
      <div className="animate-fade-in space-y-6">
        <Alert variant="error">{toMessage(existing.error)}</Alert>
      </div>
    );
  }

  const selectedCountryLabels = (countries.data?.items ?? [])
    .filter((country) => selectedCountries.has(country.id))
    .map((country) => country.name);

  const selectedCurrencyLabels = availableCurrencies
    .filter((currency) => form.currencyIds.includes(currency.id))
    .map((currency) => `${currency.name} (${currency.code})`);

  return (
    <FormPageLayout
      title={isEdit ? t('payments.editMethod') : t('payments.newMethod')}
      subtitle={t('payments.methodFormHint')}
      listLabel={t('payments.title')}
      onBack={goBack}
      onSubmit={() => void submit()}
      isPending={save.isPending || isUploadingSamples}
      canSubmit={canSubmit}
      error={save.isError ? save.error : null}
      errorMessage={toMessage}
      aside={
        <>
          <StatusPanel isActive={form.isActive} onChange={(isActive) => patch({ isActive })} />

          <AdminPanel title={t('payments.displayPanel')}>
            <Field
              label={t('payments.sortOrder')}
              htmlFor="method-sort"
              hint={t('payments.sortOrderHint')}
            >
              <Input
                id="method-sort"
                type="number"
                min={0}
                className="w-28"
                value={form.sortOrder}
                onChange={(event) => patch({ sortOrder: Number(event.target.value) || 0 })}
              />
            </Field>
          </AdminPanel>

          <ApplicantPreview type={selectedType} />

          <SummaryPanel
            title={t('lookups.countries')}
            items={selectedCountryLabels}
            emptyLabel={t('lookups.summaryEmpty')}
          />

          <SummaryPanel
            title={t('lookups.currencies')}
            items={selectedCurrencyLabels}
            emptyLabel={t('lookups.summaryEmpty')}
          />
        </>
      }
    >
      {referenceFailures.length > 0 && (
        <Alert variant="warning" data-testid="reference-upload-failures">
          {t('payments.referenceDocsUploadFailed', { files: referenceFailures.join(', ') })}
        </Alert>
      )}

      {/* AdminPanel's body applies no spacing of its own, so panels holding more than one block
          space their own children. */}
      <AdminPanel title={t('payments.sectionChannel')} subtitle={t('payments.sectionChannelHint')}>
        <div className="space-y-5">
          <Field label={t('payments.type')} htmlFor="method-type" required>
            <Select
              id="method-type"
              value={form.paymentMethodTypeId}
              onChange={(event) => patch({ paymentMethodTypeId: event.target.value })}
              data-testid="method-type"
            >
              <option value="">{t('payments.choosePlaceholder')}</option>
              {typeList.map((type) => (
                <option key={type.id} value={type.id}>
                  {type.name} — {t(`payments.kind.${type.kindName}`)}
                </option>
              ))}
            </Select>
          </Field>

          {selectedType ? (
            // The chosen type's kind is what decides the rest of this page, so it is named here
            // rather than left for the admin to infer from which panels appeared.
            <Alert variant="info">{t(`payments.kindNotice.${selectedType.kindName}`)}</Alert>
          ) : (
            <p className="text-sm text-muted-foreground">{t('payments.chooseTypeFirst')}</p>
          )}
        </div>
      </AdminPanel>

      {/* One column per language rather than one row per field. The name and the description of a
          language belong together — they are written in one sitting, by whoever speaks it — and
          the previous layout interleaved them, so filling in the Arabic side meant reading down
          past the English name to reach the Arabic description. */}
      <AdminPanel title={t('lookups.sectionNames')} subtitle={t('payments.sectionNamesHint')}>
        <div className="grid gap-8 sm:grid-cols-2 sm:gap-x-8 sm:gap-y-0">
          <LanguageFields
            heading="العربية"
            dir="rtl"
            idPrefix="method-ar"
            name={form.nameAr}
            description={form.descriptionAr ?? ''}
            onNameChange={(value) => patch({ nameAr: value })}
            onDescriptionChange={(value) => patch({ descriptionAr: value || null })}
          />

          {/* The rule only appears once the columns sit side by side; stacked, the gap does the
              separating and a line across the middle would read as an end. */}
          <div className="sm:border-s sm:border-ink-100 sm:ps-8">
            <LanguageFields
              heading="English"
              dir="ltr"
              idPrefix="method-en"
              name={form.nameEn}
              description={form.descriptionEn ?? ''}
              onNameChange={(value) => patch({ nameEn: value })}
              onDescriptionChange={(value) => patch({ descriptionEn: value || null })}
            />
          </div>
        </div>
      </AdminPanel>

      {wantsLink && (
        <AdminPanel title={t('payments.sectionLink')} subtitle={t('payments.sectionLinkHint')}>
          <Field label={t('payments.externalUrl')} htmlFor="method-url" required hint="https://">
            <Input
              id="method-url"
              dir="ltr"
              type="url"
              placeholder="https://pay.example.com/checkout"
              value={form.externalUrl ?? ''}
              onChange={(event) => patch({ externalUrl: event.target.value || null })}
              data-testid="method-url"
            />
          </Field>
        </AdminPanel>
      )}

      {/* A method the applicant pays online picks the gateway it goes through; a manual transfer has
          nothing to connect to, and the server clears the choice on such a type. */}
      <GatewayIntegrationPicker
        items={integrations.data?.items ?? []}
        isPending={integrations.isPending}
        value={form.gatewayIntegrationId ?? null}
        current={
          existing.data?.gatewayIntegrationId
            ? { id: existing.data.gatewayIntegrationId, name: existing.data.gatewayIntegrationName ?? '' }
            : null
        }
        onChange={(gatewayIntegrationId) =>
          // A different gateway asks for different things; its predecessor's values would be
          // dropped by the server anyway.
          patch({ gatewayIntegrationId, gatewaySettings: {}, gatewaySecrets: {} })
        }
      />

      {gateway && (
        <GatewaySettingsPanels
          gateway={gateway}
          settings={form.gatewaySettings ?? {}}
          secrets={form.gatewaySecrets ?? {}}
          storedSecrets={storedGatewaySecrets}
          live={gatewayIsLive}
          showErrors={showGatewayErrors}
          errors={gatewayErrors}
          canStoreSecrets={existing.data?.canStoreGatewaySecrets ?? true}
          canReveal={isEdit && adminSession.has(Permissions.PaymentMethodsUpdate)}
          methodId={id}
          onSettingChange={(key, value) =>
            patch({ gatewaySettings: { ...(form.gatewaySettings ?? {}), [key]: value } })
          }
          onSecretChange={(key, value) => {
            const secrets = { ...(form.gatewaySecrets ?? {}) };
            if (value === undefined) delete secrets[key];
            else secrets[key] = value;
            patch({ gatewaySecrets: secrets });
          }}
        />
      )}

      <AdminPanel title={t('payments.sectionCoverage')} subtitle={t('payments.sectionCoverageHint')}>
        <IdChecklist
          legend={t('lookups.countries')}
          hint={t('payments.countriesHint')}
          selectedLabel={t('payments.countriesSelected', { count: form.countryIds.length })}
          searchPlaceholder={t('lookups.countriesSearch')}
          emptyLabel={t('lookups.noCountries')}
          isPending={countries.isPending}
          selectedIds={selectedCountries}
          options={(countries.data?.items ?? []).map((country) => ({
            id: country.id,
            label: country.name,
            secondary: `(${country.code})`,
            searchText: `${country.code} ${country.name} ${country.nameEn} ${country.nameAr}`,
          }))}
          onToggle={(countryId) => toggleId('countryIds', countryId)}
          testIdPrefix="method-country"
        />

        <div className="mt-6 border-t border-border pt-6">
          {form.countryIds.length === 0 ? (
            // The dependency stated rather than shown as an empty list, which would read as a bug.
            <Alert variant="info">{t('payments.currenciesAwaitCountry')}</Alert>
          ) : (
            <IdChecklist
              legend={t('lookups.currencies')}
              hint={t('payments.currenciesHint')}
              selectedLabel={t('payments.currenciesSelected', { count: form.currencyIds.length })}
              searchPlaceholder={t('lookups.currenciesSearch')}
              emptyLabel={t('payments.noCurrenciesForCountries')}
              isPending={currencies.isPending}
              selectedIds={new Set(form.currencyIds)}
              options={availableCurrencies.map((currency) => ({
                id: currency.id,
                label: currency.name,
                secondary: `(${currency.code})`,
                searchText: `${currency.code} ${currency.name}`,
              }))}
              onToggle={(currencyId) => toggleId('currencyIds', currencyId)}
              testIdPrefix="method-currency"
            />
          )}
        </div>
      </AdminPanel>

      {selectedType?.requiresAccountNumber && (
        <AccountsEditor
          accounts={form.accounts}
          requiresBarcode={selectedType.requiresBarcode}
          requiresBank={selectedType.requiresBank}
          countryIds={form.countryIds}
          // A barcode hangs off a saved row, so an unsaved account cannot carry one yet.
          savedAccountIds={new Set((existing.data?.accounts ?? []).map((account) => account.id))}
          barcodeAccountIds={
            new Set(
              (existing.data?.accounts ?? [])
                .filter((account) => account.hasBarcode)
                .map((account) => account.id),
            )
          }
          onChange={(accounts: PaymentMethodAccountInput[]) => patch({ accounts })}
        />
      )}

      <AdminPanel
        title={t('payments.sectionDocuments')}
        subtitle={t('payments.sectionDocumentsHint')}
      >
        <RequiredDocumentsEditor
          documents={form.requiredFiles ?? []}
          onChange={(requiredFiles) => patch({ requiredFiles })}
          scope="paymentMethod"
          allowEmpty
          showHeading={false}
        />
      </AdminPanel>

      <NotificationEmailsEditor
        recipients={form.notificationEmails}
        onChange={(notificationEmails) => patch({ notificationEmails })}
      />

      <AdminPanel title={t('payments.sectionNotes')} subtitle={t('payments.sectionNotesHint')}>
        <div className="space-y-4 rounded-xl bg-success-muted/40 p-4 ring-1 ring-success/20">
          <div className="flex items-center gap-2 text-sm font-semibold text-ink-900">
            <Eye className="size-4" aria-hidden="true" />
            {t('payments.publicNotes')}
          </div>
          <p className="-mt-2 text-xs text-muted-foreground">{t('payments.publicNotesHint')}</p>

          <div className="grid gap-4 sm:grid-cols-2">
            <Field label={t('payments.publicNoteAr')} htmlFor="method-public-ar">
              <NoteArea
                id="method-public-ar"
                dir="rtl"
                rows={3}
                maxLength={2000}
                value={form.publicNoteAr ?? ''}
                onChange={(event) => patch({ publicNoteAr: event.target.value || null })}
              />
            </Field>

            <Field label={t('payments.publicNoteEn')} htmlFor="method-public-en">
              <NoteArea
                id="method-public-en"
                dir="ltr"
                rows={3}
                maxLength={2000}
                value={form.publicNoteEn ?? ''}
                onChange={(event) => patch({ publicNoteEn: event.target.value || null })}
              />
            </Field>
          </div>
        </div>

        <div className="mt-4 space-y-4 rounded-xl bg-ink-50 p-4 ring-1 ring-ink-200">
          <div className="flex items-center gap-2 text-sm font-semibold text-ink-900">
            <EyeOff className="size-4" aria-hidden="true" />
            {t('payments.privateNotes')}
          </div>
          <p className="-mt-2 text-xs text-muted-foreground">{t('payments.privateNotesHint')}</p>

          <div className="grid gap-4 sm:grid-cols-2">
            <Field label={t('payments.privateNoteAr')} htmlFor="method-private-ar">
              <NoteArea
                id="method-private-ar"
                dir="rtl"
                rows={3}
                maxLength={2000}
                value={form.privateNoteAr ?? ''}
                onChange={(event) => patch({ privateNoteAr: event.target.value || null })}
              />
            </Field>

            <Field label={t('payments.privateNoteEn')} htmlFor="method-private-en">
              <NoteArea
                id="method-private-en"
                dir="ltr"
                rows={3}
                maxLength={2000}
                value={form.privateNoteEn ?? ''}
                onChange={(event) => patch({ privateNoteEn: event.target.value || null })}
              />
            </Field>
          </div>
        </div>
      </AdminPanel>
    </FormPageLayout>
  );
}

/**
 * What the applicant will actually be asked for, in words.
 *
 * The type's flags are the contract between this page and the applicant's deposit form, but that
 * form is somewhere an admin never sees. Spelling the consequences out here is what stops a
 * method being published with, say, no reference required and the reviewer left unable to
 * reconcile anything.
 */
function ApplicantPreview({
  type,
}: {
  type:
    | {
        kindName: string;
        needsApproval: boolean;
        requiresAccountNumber: boolean;
        requiresBarcode: boolean;
        requiresBank: boolean;
        requiresExternalUrl: boolean;
        requiresProofDocument: boolean;
        requiresReferenceNumber: boolean;
      }
    | undefined;
}) {
  const { t } = useTranslation();

  if (!type) return null;

  const steps = [
    type.requiresExternalUrl && t('payments.preview.openLink'),
    t('payments.preview.amount'),
    type.requiresBank
      ? t('payments.preview.bankAccount')
      : type.requiresAccountNumber && t('payments.preview.account'),
    type.requiresBarcode && t('payments.preview.scan'),
    type.requiresReferenceNumber && t('payments.preview.reference'),
    type.requiresProofDocument && t('payments.preview.proof'),
    type.needsApproval ? t('payments.preview.approval') : t('payments.preview.noApproval'),
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

/**
 * A multi-line field matching the panel's inputs. The UI package has no textarea, and the six
 * bilingual notes on this page would otherwise repeat the same class string six times.
 */
function NoteArea({
  id,
  dir,
  rows,
  maxLength,
  value,
  onChange,
}: {
  id: string;
  dir: 'rtl' | 'ltr';
  rows: number;
  maxLength: number;
  value: string;
  onChange: (event: { target: { value: string } }) => void;
}) {
  return (
    <textarea
      id={id}
      dir={dir}
      rows={rows}
      maxLength={maxLength}
      value={value}
      onChange={onChange}
      className="flex w-full rounded-xl border-0 bg-white px-3.5 py-2.5 text-sm text-ink-900 shadow-soft ring-1 ring-ink-200 transition-shadow placeholder:text-ink-300 focus:outline-none focus:ring-2 focus:ring-brand-500"
    />
  );
}

/**
 * What applicants read in one language: its name and its description, together.
 *
 * The heading is the language's own name rather than a translated label — a language is called the
 * same thing whichever interface language you are working in, and it lets each field say plainly
 * "Name" and "Description" instead of repeating "(Arabic)" on every line.
 */
function LanguageFields({
  heading,
  dir,
  idPrefix,
  name,
  description,
  onNameChange,
  onDescriptionChange,
}: {
  heading: string;
  dir: 'rtl' | 'ltr';
  idPrefix: string;
  name: string;
  description: string;
  onNameChange: (value: string) => void;
  onDescriptionChange: (value: string) => void;
}) {
  const { t } = useTranslation();

  return (
    <section className="space-y-4">
      <h3 className="text-xs font-semibold uppercase tracking-[0.12em] text-ink-400">{heading}</h3>

      <Field label={t('payments.name')} htmlFor={`${idPrefix}-name`} required>
        <Input
          id={`${idPrefix}-name`}
          dir={dir}
          value={name}
          onChange={(event) => onNameChange(event.target.value)}
        />
      </Field>

      <Field label={t('payments.description')} htmlFor={`${idPrefix}-desc`}>
        <NoteArea
          id={`${idPrefix}-desc`}
          dir={dir}
          rows={4}
          maxLength={2000}
          value={description}
          onChange={(event) => onDescriptionChange(event.target.value)}
        />
      </Field>
    </section>
  );
}

/**
 * Which configured gateway this method pays through. The integrations themselves — credentials,
 * mode, the gateway's own settings — live on their own page.
 */
function GatewayIntegrationPicker({
  items,
  isPending,
  value,
  current,
  onChange,
}: {
  items: PaymentGatewayIntegrationDto[];
  isPending: boolean;
  value: string | null;
  /** The integration saved on the method, offered even if it has since been retired. */
  current: { id: string; name: string } | null;
  onChange: (id: string | null) => void;
}) {
  const { t } = useTranslation();
  const selected = items.find((item) => item.id === value);
  const retired = current && current.id === value && !selected ? current : null;

  return (
    <AdminPanel title={t('integrations.pickerTitle')} subtitle={t('integrations.pickerHint')}>
      <div className="space-y-3">
        <Field label={t('integrations.title')} htmlFor="method-gateway-integration">
          <Select
            id="method-gateway-integration"
            value={value ?? ''}
            onChange={(event) => onChange(event.target.value || null)}
            data-testid="method-gateway-integration"
          >
            <option value="">{t('integrations.pickerNone')}</option>
            {retired && (
              <option value={retired.id}>
                {retired.name} ({t('common.inactive')})
              </option>
            )}
            {items.map((item) => (
              <option key={item.id} value={item.id}>
                {item.name} — {item.gatewayName}
              </option>
            ))}
          </Select>
        </Field>

        {selected && (
          <p className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
            <Plug className="size-4" aria-hidden="true" />
            {selected.gatewayName}
            <ModeBadge mode={selected.mode} />
          </p>
        )}

        {retired && <Alert variant="warning">{t('integrations.pickerRetired')}</Alert>}

        {!isPending && items.length === 0 && (
          <Alert variant="info">{t('integrations.pickerEmpty')}</Alert>
        )}

        <Link
          to="/payments/integrations"
          className="inline-flex text-sm font-medium text-primary hover:underline"
        >
          {t('integrations.manage')}
        </Link>
      </div>
    </AdminPanel>
  );
}
