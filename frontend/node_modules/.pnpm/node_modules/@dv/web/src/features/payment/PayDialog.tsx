import { ApiError } from '@dv/api-client';
import { Alert, Button, Dialog, Spinner } from '@dv/ui';
import { Plus } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import type { ApplicationListItemDto } from '@/entities/application/listApi';
import { usePayApplications } from '@/entities/application/listApi';
import { useWallet } from '@/entities/wallet/api';
import { formatCurrency } from '@/shared/lib/format';
import { useApiErrorMessage } from '@/shared/lib/useApiError';

interface Props {
  open: boolean;
  applications: ApplicationListItemDto[];
  onClose: () => void;
  onPaid: (count: number) => void;
  /**
   * Offers to top up when the balance falls short, carrying the shortfall across so the amount is
   * already filled in. Omitted where the host has nowhere to send them.
   */
  onTopUp?: (shortfall: number) => void;
}

/**
 * Confirms a multi-application payment. Shows the balance against the amount due before charging,
 * and surfaces the server's insufficient-funds figures rather than a generic failure.
 *
 * Applications are settled from the wallet, so a short balance is not a failure to report but a
 * step to take: the dialog offers to add the difference rather than leaving somebody at a dead end
 * to work out for themselves that topping up is what happens next.
 */
export function PayDialog({ open, applications, onClose, onPaid, onTopUp }: Props) {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage ?? 'en';
  const toMessage = useApiErrorMessage();

  const wallet = useWallet();
  const pay = usePayApplications();

  const currency = wallet.data?.wallet.currencyCode ?? applications[0]?.currencyCode ?? '';
  const balance = wallet.data?.wallet.balance ?? 0;
  const total = applications.reduce((sum, application) => sum + application.totalCost, 0);
  const remaining = balance - total;

  // The server is the authority on affordability; this only pre-empts an obvious rejection.
  const insufficient = remaining < 0;
  const shortfall = insufficient ? Math.abs(remaining) : 0;

  const insufficientFromServer =
    pay.error instanceof ApiError && pay.error.code === 'wallet.insufficient_funds'
      ? {
          required: Number(pay.error.problem.required ?? total),
          available: Number(pay.error.problem.available ?? balance),
        }
      : null;

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title={t('payment.title')}
      footer={
        <>
          <Button variant="outline" onClick={onClose}>
            {t('common.cancel')}
          </Button>

          {/* The way forward when the balance is short, offered where the blocked action is. */}
          {insufficient && onTopUp && (
            <Button variant="secondary" onClick={() => onTopUp(shortfall)} data-testid="pay-top-up">
              <Plus className="size-4" aria-hidden="true" />
              {t('payment.addTheDifference')}
            </Button>
          )}

          <Button
            disabled={pay.isPending || insufficient || applications.length === 0}
            data-testid="confirm-payment"
            onClick={() =>
              pay.mutate(
                applications.map((application) => application.id),
                {
                  onSuccess: (result) => onPaid(result.applications.length),
                },
              )
            }
          >
            {pay.isPending && <Spinner />}
            {t('payment.confirm')}
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <p className="text-muted-foreground">{t('payment.subtitle')}</p>

        <ul className="divide-y divide-border rounded-lg border border-border">
          {applications.map((application) => (
            <li key={application.id} className="flex justify-between gap-3 p-3">
              <span className="font-mono text-xs" dir="ltr">
                {application.applicationNumber}
              </span>
              <span>{formatCurrency(application.totalCost, currency, locale)}</span>
            </li>
          ))}
        </ul>

        <dl className="space-y-1.5">
          <div className="flex justify-between">
            <dt className="text-muted-foreground">{t('payment.walletBalance')}</dt>
            <dd data-testid="dialog-balance">{formatCurrency(balance, currency, locale)}</dd>
          </div>
          <div className="flex justify-between font-medium">
            <dt>{t('payment.amountDue')}</dt>
            <dd data-testid="dialog-total">{formatCurrency(total, currency, locale)}</dd>
          </div>
          <div className="flex justify-between border-t border-border pt-1.5">
            <dt className="text-muted-foreground">{t('payment.remaining')}</dt>
            <dd className={remaining < 0 ? 'text-destructive' : undefined}>
              {formatCurrency(remaining, currency, locale)}
            </dd>
          </div>
        </dl>

        {(insufficient || insufficientFromServer) && (
          <Alert variant="error" title={t('payment.insufficientTitle')}>
            {t('payment.insufficientBody', {
              required: formatCurrency(insufficientFromServer?.required ?? total, currency, locale),
              available: formatCurrency(
                insufficientFromServer?.available ?? balance,
                currency,
                locale,
              ),
            })}

            {/* Named in money rather than left as a subtraction, and honest that the top-up is
                reviewed before it can be spent — otherwise somebody adds funds and comes straight
                back expecting to pay. */}
            {shortfall > 0 && onTopUp && (
              <span className="mt-2 block">
                {t('payment.shortfallHint', {
                  amount: formatCurrency(shortfall, currency, locale),
                })}
              </span>
            )}
          </Alert>
        )}

        {pay.isError && !insufficientFromServer ? (
          <Alert variant="error">{toMessage(pay.error)}</Alert>
        ) : null}
      </div>
    </Dialog>
  );
}
