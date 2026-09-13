import {
  AdminPanel,
  Alert,
  Button,
  Field,
  Input,
  SearchableSelect,
  Spinner,
} from '@dv/ui';
import { ArrowDown, ArrowUp, Plus, QrCode, Trash2, Upload } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  fetchAccountBarcode,
  useBanks,
  useDeleteAccountBarcode,
  useUploadAccountBarcode,
  type PaymentMethodAccountInput,
} from '@/features/payments/api';
import { useApiErrorMessage } from '@/shared/lib/useApiError';

interface Props {
  accounts: PaymentMethodAccountInput[];
  /** From the type: whether each account may carry a scannable image. */
  requiresBarcode: boolean;
  /** From the type: whether each row names the bank holding the account. */
  requiresBank: boolean;
  /** The method's countries, which decide the banks on offer. */
  countryIds: string[];
  /** Accounts that exist server-side; only these can take a barcode upload. */
  savedAccountIds: Set<string>;
  /** Which of those already have one. */
  barcodeAccountIds: Set<string>;
  onChange: (accounts: PaymentMethodAccountInput[]) => void;
}

/**
 * The receiving accounts an applicant may transfer to, and picks between when they say which one
 * they paid.
 *
 * Order matters — it is the order the applicant sees — so rows move rather than sort themselves,
 * and `sortOrder` is kept as the array index instead of being another number to type.
 */
export function AccountsEditor({
  accounts,
  requiresBarcode,
  requiresBank,
  countryIds,
  savedAccountIds,
  barcodeAccountIds,
  onChange,
}: Props) {
  const { t } = useTranslation();

  // Only the banks of the countries this method serves — the same rule the server enforces. One
  // request per country keeps the query simple; there are rarely more than a handful.
  const banks = useBanks({
    page: 1,
    pageSize: 200,
    isActive: true,
    countryId: countryIds[0],
    enabled: requiresBank && countryIds.length > 0,
  });

  const bankOptions = banks.data?.items ?? [];

  function replace(index: number, patch: Partial<PaymentMethodAccountInput>) {
    onChange(accounts.map((account, i) => (i === index ? { ...account, ...patch } : account)));
  }

  function add() {
    onChange([
      ...accounts,
      {
        labelAr: '',
        labelEn: '',
        accountNumber: '',
        accountHolder: null,
        bankId: null,
        isActive: true,
        sortOrder: accounts.length,
      },
    ]);
  }

  function remove(index: number) {
    onChange(
      accounts
        .filter((_, i) => i !== index)
        .map((account, i) => ({ ...account, sortOrder: i })),
    );
  }

  function move(index: number, delta: number) {
    const target = index + delta;
    if (target < 0 || target >= accounts.length) return;

    const next = [...accounts];
    [next[index], next[target]] = [next[target], next[index]];
    onChange(next.map((account, i) => ({ ...account, sortOrder: i })));
  }

  return (
    <AdminPanel
      // A bank transfer collects IBANs, not phone numbers; the panel says which it wants.
      title={t(requiresBank ? 'payments.sectionBankAccounts' : 'payments.sectionAccounts')}
      subtitle={t(
        requiresBank ? 'payments.sectionBankAccountsHint' : 'payments.sectionAccountsHint',
      )}
      actions={
        <Button type="button" variant="outline" size="sm" onClick={add} data-testid="account-add">
          <Plus className="size-4" aria-hidden="true" />
          {t(requiresBank ? 'payments.addBankAccount' : 'payments.addAccount')}
        </Button>
      }
    >
      {/* A number without its code is the one way an InstaPay method can look finished and still
          be invisible to applicants, so it is called out here rather than left to the list's
          "Incomplete" badge. */}
      {requiresBarcode
        && accounts.some(
          (account) => account.isActive && !(account.id && barcodeAccountIds.has(account.id)),
        ) && <Alert variant="warning">{t('payments.barcodeMissingWarning')}</Alert>}

      {requiresBank && accounts.some((account) => account.isActive && !account.bankId) && (
        <Alert variant="warning">{t('payments.bankMissingWarning')}</Alert>
      )}

      {accounts.length === 0 ? (
        <Alert variant="warning">
          {t(requiresBank ? 'payments.noBankAccountsYet' : 'payments.noAccountsYet')}
        </Alert>
      ) : (
        <ul className="space-y-4">
          {accounts.map((account, index) => (
            <li
              key={account.id ?? `new-${index}`}
              className="rounded-xl border border-border p-4"
              data-testid={`account-row-${index}`}
            >
              <div className="mb-3 flex items-center justify-between gap-2">
                <span className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                  {t('payments.accountIndex', { index: index + 1 })}
                </span>

                <div className="flex items-center gap-1">
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    aria-label={t('payments.moveUp')}
                    disabled={index === 0}
                    onClick={() => move(index, -1)}
                  >
                    <ArrowUp className="size-4" aria-hidden="true" />
                  </Button>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    aria-label={t('payments.moveDown')}
                    disabled={index === accounts.length - 1}
                    onClick={() => move(index, 1)}
                  >
                    <ArrowDown className="size-4" aria-hidden="true" />
                  </Button>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    aria-label={t('common.delete')}
                    onClick={() => remove(index)}
                    data-testid={`account-remove-${index}`}
                  >
                    <Trash2 className="size-4 text-danger" aria-hidden="true" />
                  </Button>
                </div>
              </div>

              <div className="grid gap-4 sm:grid-cols-2">
                {requiresBank && (
                  <Field
                    label={t('payments.bank')}
                    htmlFor={`account-bank-${index}`}
                    required
                    hint={countryIds.length === 0 ? t('payments.bankAwaitsCountry') : undefined}
                  >
                    {/* Searchable rather than a plain select: a country's register runs to dozens
                        of banks, and scrolling for one by eye is the slow way to find it. */}
                    <SearchableSelect
                      id={`account-bank-${index}`}
                      options={bankOptions.map((bank) => ({
                        value: bank.id,
                        label: bank.name,
                        // Found by BIC too — it is often what an operator is reading off a form.
                        keywords: bank.swiftCode ?? undefined,
                      }))}
                      value={account.bankId ?? ''}
                      onChange={(bankId) => replace(index, { bankId: bankId || null })}
                      placeholder={t('payments.choosePlaceholder')}
                      searchPlaceholder={t('payments.searchBanks')}
                      emptyMessage={t('payments.noBanksMatch')}
                      clearable
                    />
                  </Field>
                )}

                <Field
                  label={requiresBank ? t('payments.iban') : t('payments.accountNumber')}
                  htmlFor={`account-number-${index}`}
                  required
                >
                  <Input
                    id={`account-number-${index}`}
                    dir="ltr"
                    className="font-mono"
                    value={account.accountNumber}
                    onChange={(event) => replace(index, { accountNumber: event.target.value })}
                    data-testid={`account-number-${index}`}
                  />
                </Field>

                <Field label={t('payments.accountHolder')} htmlFor={`account-holder-${index}`}>
                  <Input
                    id={`account-holder-${index}`}
                    value={account.accountHolder ?? ''}
                    onChange={(event) =>
                      replace(index, { accountHolder: event.target.value || null })
                    }
                  />
                </Field>

                <Field label={t('payments.accountLabelAr')} htmlFor={`account-label-ar-${index}`}>
                  <Input
                    id={`account-label-ar-${index}`}
                    dir="rtl"
                    value={account.labelAr}
                    onChange={(event) => replace(index, { labelAr: event.target.value })}
                  />
                </Field>

                <Field label={t('payments.accountLabelEn')} htmlFor={`account-label-en-${index}`}>
                  <Input
                    id={`account-label-en-${index}`}
                    dir="ltr"
                    value={account.labelEn}
                    onChange={(event) => replace(index, { labelEn: event.target.value })}
                  />
                </Field>
              </div>

              <label className="mt-3 flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  className="size-4 rounded border-border"
                  checked={account.isActive}
                  onChange={(event) => replace(index, { isActive: event.target.checked })}
                />
                {t('payments.accountActive')}
              </label>

              {requiresBarcode && (
                <BarcodeField
                  accountId={account.id ?? null}
                  isSaved={Boolean(account.id && savedAccountIds.has(account.id))}
                  hasBarcode={Boolean(account.id && barcodeAccountIds.has(account.id))}
                />
              )}
            </li>
          ))}
        </ul>
      )}
    </AdminPanel>
  );
}

/**
 * The barcode uploader for one account.
 *
 * Uploads immediately rather than on save: the file belongs to a row that has to exist first, and
 * carrying bytes through the parent form would mean re-uploading them on every unrelated edit.
 * A row the admin has only just added therefore says so instead of offering a control that would
 * 404.
 */
function BarcodeField({
  accountId,
  isSaved,
  hasBarcode,
}: {
  accountId: string | null;
  isSaved: boolean;
  hasBarcode: boolean;
}) {
  const { t } = useTranslation();
  const toMessage = useApiErrorMessage();
  const inputRef = useRef<HTMLInputElement>(null);

  const upload = useUploadAccountBarcode();
  const remove = useDeleteAccountBarcode();

  // Tracks this session's changes so the panel reflects an upload without a page reload.
  const [present, setPresent] = useState(hasBarcode);
  // Bumped on every upload so the preview refetches instead of showing the replaced image.
  const [version, setVersion] = useState(0);

  if (!isSaved || !accountId) {
    return (
      <p className="mt-3 rounded-lg bg-muted px-3 py-2 text-xs text-muted-foreground">
        {t('payments.barcodeAfterSave')}
      </p>
    );
  }

  return (
    <div className="mt-3 space-y-2 rounded-lg bg-muted/60 p-3">
      <div className="flex flex-wrap items-center gap-2">
        <QrCode className="size-4 text-muted-foreground" aria-hidden="true" />
        <span className="text-sm font-medium">{t('payments.barcode')}</span>
        <span className="text-xs text-muted-foreground">
          {present ? t('payments.barcodeAttached') : t('payments.barcodeMissing')}
        </span>
      </div>

      <input
        ref={inputRef}
        type="file"
        accept="image/png,image/jpeg"
        className="hidden"
        data-testid={`barcode-input-${accountId}`}
        onChange={(event) => {
          const file = event.target.files?.[0];
          event.target.value = '';
          if (!file) return;

          upload.mutate(
            { accountId, file },
            {
              onSuccess: () => {
                setPresent(true);
                setVersion((previous) => previous + 1);
              },
            },
          );
        }}
      />

      {present && <BarcodePreview accountId={accountId} version={version} />}

      <div className="flex flex-wrap gap-2">
        <Button
          type="button"
          variant="outline"
          size="sm"
          disabled={upload.isPending}
          onClick={() => inputRef.current?.click()}
        >
          {upload.isPending ? <Spinner /> : <Upload className="size-4" aria-hidden="true" />}
          {present ? t('payments.replaceBarcode') : t('payments.uploadBarcode')}
        </Button>

        {present && (
          <Button
            type="button"
            variant="ghost"
            size="sm"
            disabled={remove.isPending}
            onClick={() => remove.mutate(accountId, { onSuccess: () => setPresent(false) })}
          >
            <Trash2 className="size-4 text-danger" aria-hidden="true" />
            {t('payments.removeBarcode')}
          </Button>
        )}
      </div>

      <p className="text-xs text-muted-foreground">{t('payments.barcodeHint')}</p>

      {upload.isError && <Alert variant="error">{toMessage(upload.error)}</Alert>}
      {remove.isError && <Alert variant="error">{toMessage(remove.error)}</Alert>}
    </div>
  );
}

/**
 * Shows the barcode that is actually stored, so an admin can confirm they uploaded the right one
 * rather than trusting a filename. Fetched with the session's credentials — a plain `<img src>`
 * would arrive unauthenticated — and the object URL is revoked on unmount.
 */
function BarcodePreview({ accountId, version }: { accountId: string; version: number }) {
  const { t } = useTranslation();
  const [url, setUrl] = useState<string | null>(null);

  useEffect(() => {
    let objectUrl: string | null = null;
    let cancelled = false;

    fetchAccountBarcode(accountId)
      .then((blob) => {
        if (cancelled) return;
        objectUrl = URL.createObjectURL(blob);
        setUrl(objectUrl);
      })
      .catch(() => {
        if (!cancelled) setUrl(null);
      });

    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [accountId, version]);

  if (!url) return null;

  return (
    <img
      src={url}
      alt={t('payments.barcode')}
      className="size-24 rounded-lg border border-border bg-white object-contain p-1"
      data-testid={`barcode-preview-${accountId}`}
    />
  );
}
