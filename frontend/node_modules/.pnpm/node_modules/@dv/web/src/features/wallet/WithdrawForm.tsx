import { ApiError } from '@dv/api-client';
import { Alert, Button, Field, Input, Spinner } from '@dv/ui';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import {
  useCreateWalletRequest,
  WalletRequestType,
  type WalletFeaturesDto,
} from '@/entities/wallet/api';
import { formatCurrency } from '@/shared/lib/format';
import { useApiErrorMessage } from '@/shared/lib/useApiError';

interface Props {
  /** The balance being paid out from. */
  currencyId: string;
  currency: string;
  balance: number;
  features: WalletFeaturesDto;
  onCancel: () => void;
  onSubmitted: () => void;
}

/**
 * Asks for a payout from one balance — the Withdraw page's form. The amount is held the moment the
 * request is made, so it cannot exceed what that balance holds.
 */
export function WithdrawForm({
  currencyId,
  currency,
  balance,
  features,
  onCancel,
  onSubmitted,
}: Props) {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage ?? 'en';
  const toMessage = useApiErrorMessage();

  const create = useCreateWalletRequest();

  const [amount, setAmount] = useState('');
  const [note, setNote] = useState('');

  // Another currency is another balance: nothing typed for the last one carries over.
  useEffect(() => {
    setAmount('');
    setNote('');
    create.reset();
    // `create` is a stable mutation object; re-running on its identity would clear the error the
    // applicant is still reading.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [currencyId]);

  const parsed = Number(amount);
  const isNumeric = amount.trim() !== '' && Number.isFinite(parsed);

  const belowMinimum = isNumeric && parsed < features.minimumRequestAmount;
  const aboveMaximum = isNumeric && parsed > features.maximumRequestAmount;
  const exceedsBalance = isNumeric && parsed > balance;
  const isValid = isNumeric && parsed > 0 && !belowMinimum && !aboveMaximum && !exceedsBalance;

  // The server is the authority on affordability; this only pre-empts an obvious rejection.
  const insufficientFromServer =
    create.error instanceof ApiError && create.error.code === 'wallet.insufficient_funds';

  function submit() {
    create.mutate(
      { type: WalletRequestType.Withdrawal, amount: parsed, note, currencyId },
      { onSuccess: onSubmitted },
    );
  }

  return (
    <div className="space-y-5" data-testid="withdraw-form">
      <p className="text-sm text-muted-foreground">{t('wallet.withdrawIntro')}</p>

      <Alert variant="info">{t('wallet.withdrawHoldNotice')}</Alert>

      <div className="flex justify-between rounded-xl bg-muted px-4 py-3 text-sm">
        <span className="text-muted-foreground">{t('wallet.balance')}</span>
        <span className="font-semibold" data-testid="withdraw-balance">
          {formatCurrency(balance, currency, locale)}
        </span>
      </div>

      <Field label={t('wallet.amount')} htmlFor="wallet-request-amount">
        <div className="relative">
          <span
            className="pointer-events-none absolute inset-y-0 start-0 flex items-center ps-3 text-sm font-medium text-muted-foreground"
            aria-hidden="true"
          >
            {currency}
          </span>
          <Input
            id="wallet-request-amount"
            type="number"
            inputMode="decimal"
            min={features.minimumRequestAmount}
            max={balance}
            step="0.01"
            value={amount}
            onChange={(event) => setAmount(event.target.value)}
            data-testid="wallet-request-amount"
            className="ps-14 text-base font-semibold"
          />
        </div>
      </Field>

      <p className="-mt-3 text-xs text-muted-foreground">
        {t('wallet.amountLimits', {
          min: formatCurrency(features.minimumRequestAmount, currency, locale),
          max: formatCurrency(features.maximumRequestAmount, currency, locale),
        })}
      </p>

      <Field label={t('wallet.requestNote')} htmlFor="wallet-request-note">
        <Input
          id="wallet-request-note"
          value={note}
          maxLength={1000}
          placeholder={t('wallet.withdrawNotePlaceholder')}
          onChange={(event) => setNote(event.target.value)}
          data-testid="wallet-request-note"
        />
      </Field>

      {belowMinimum && (
        <Alert variant="error">
          {t('wallet.belowMinimum', {
            min: formatCurrency(features.minimumRequestAmount, currency, locale),
          })}
        </Alert>
      )}

      {aboveMaximum && (
        <Alert variant="error">
          {t('wallet.aboveMaximum', {
            max: formatCurrency(features.maximumRequestAmount, currency, locale),
          })}
        </Alert>
      )}

      {(exceedsBalance || insufficientFromServer) && (
        <Alert variant="error" title={t('payment.insufficientTitle')}>
          {t('wallet.exceedsBalance', { balance: formatCurrency(balance, currency, locale) })}
        </Alert>
      )}

      {create.isError && !insufficientFromServer ? (
        <Alert variant="error">{toMessage(create.error)}</Alert>
      ) : null}

      <div className="flex flex-wrap justify-end gap-3 border-t border-border pt-5">
        <Button variant="outline" onClick={onCancel}>
          {t('common.cancel')}
        </Button>
        <Button
          disabled={!isValid || create.isPending}
          data-testid="confirm-wallet-request"
          onClick={submit}
        >
          {create.isPending && <Spinner />}
          {t('wallet.submitRequest')}
        </Button>
      </div>
    </div>
  );
}
