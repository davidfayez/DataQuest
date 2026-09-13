import { ApiError } from '@dv/api-client';
import { Alert, Button, Dialog, Field, Input, Spinner } from '@dv/ui';
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
  /** Null closes the dialog; a type opens it in that direction. */
  type: WalletRequestType | null;
  features: WalletFeaturesDto;
  balance: number;
  currency: string;
  onClose: () => void;
  onSubmitted: (type: WalletRequestType) => void;
}

/**
 * Raises a top-up or payout request. The two directions share a form because they differ only in
 * their explanatory copy and in whether the balance caps the amount — a withdrawal cannot exceed
 * what is in the wallet, since the server holds the funds the moment the request is made.
 */
export function WalletRequestDialog({
  type,
  features,
  balance,
  currency,
  onClose,
  onSubmitted,
}: Props) {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage ?? 'en';
  const toMessage = useApiErrorMessage();

  const create = useCreateWalletRequest();

  const [amount, setAmount] = useState('');
  const [note, setNote] = useState('');

  const isWithdrawal = type === WalletRequestType.Withdrawal;

  // Reopening the dialog must not show the previous attempt's figures or error.
  useEffect(() => {
    if (type !== null) {
      setAmount('');
      setNote('');
      create.reset();
    }
    // `create` is a stable mutation object; re-running on its identity would clear the error the
    // user is still reading.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [type]);

  const parsed = Number(amount);
  const isNumeric = amount.trim() !== '' && Number.isFinite(parsed);

  const belowMinimum = isNumeric && parsed < features.minimumRequestAmount;
  const aboveMaximum = isNumeric && parsed > features.maximumRequestAmount;
  const exceedsBalance = isWithdrawal && isNumeric && parsed > balance;
  const isValid = isNumeric && parsed > 0 && !belowMinimum && !aboveMaximum && !exceedsBalance;

  // The server is the authority on affordability; this only pre-empts an obvious rejection.
  const insufficientFromServer =
    create.error instanceof ApiError && create.error.code === 'wallet.insufficient_funds';

  return (
    <Dialog
      open={type !== null}
      onClose={onClose}
      title={t(isWithdrawal ? 'wallet.withdrawTitle' : 'wallet.depositTitle')}
      footer={
        <>
          <Button variant="outline" onClick={onClose}>
            {t('common.cancel')}
          </Button>
          <Button
            disabled={!isValid || create.isPending}
            data-testid="confirm-wallet-request"
            onClick={() => {
              if (type === null) return;

              create.mutate(
                { type, amount: parsed, note },
                { onSuccess: () => onSubmitted(type) },
              );
            }}
          >
            {create.isPending && <Spinner />}
            {t('wallet.submitRequest')}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <p className="text-sm text-muted-foreground">
          {t(isWithdrawal ? 'wallet.withdrawIntro' : 'wallet.depositIntro')}
        </p>

        {isWithdrawal && (
          <Alert variant="info">{t('wallet.withdrawHoldNotice')}</Alert>
        )}

        <Field label={t('wallet.amount')} htmlFor="wallet-request-amount">
          <Input
            id="wallet-request-amount"
            type="number"
            inputMode="decimal"
            min={features.minimumRequestAmount}
            max={isWithdrawal ? balance : features.maximumRequestAmount}
            step="0.01"
            value={amount}
            onChange={(event) => setAmount(event.target.value)}
            data-testid="wallet-request-amount"
          />
        </Field>

        <p className="text-xs text-muted-foreground">
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
            placeholder={t(
              isWithdrawal ? 'wallet.withdrawNotePlaceholder' : 'wallet.depositNotePlaceholder',
            )}
            onChange={(event) => setNote(event.target.value)}
            data-testid="wallet-request-note"
          />
        </Field>

        {isWithdrawal && (
          <div className="flex justify-between border-t border-border pt-3 text-sm">
            <span className="text-muted-foreground">{t('wallet.balance')}</span>
            <span className="font-medium">{formatCurrency(balance, currency, locale)}</span>
          </div>
        )}

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
            {t('wallet.exceedsBalance', {
              balance: formatCurrency(balance, currency, locale),
            })}
          </Alert>
        )}

        {create.isError && !insufficientFromServer ? (
          <Alert variant="error">{toMessage(create.error)}</Alert>
        ) : null}
      </div>
    </Dialog>
  );
}
