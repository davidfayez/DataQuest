import { ApiError } from '@dv/api-client';
import { Alert, Button, Dialog, Spinner, cn } from '@dv/ui';
import { Check, Plus } from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import type { ApplicationListItemDto, PaymentQuoteOptionDto } from '@/entities/application/listApi';
import { usePayApplications, usePaymentQuote } from '@/entities/application/listApi';
import { formatCurrency } from '@/shared/lib/format';
import { useApiErrorMessage } from '@/shared/lib/useApiError';

interface Props {
  open: boolean;
  applications: ApplicationListItemDto[];
  onClose: () => void;
  onPaid: (count: number) => void;
  /**
   * Offers to top up the chosen balance when it falls short, carrying the shortfall and currency
   * across so the Add funds page opens ready. Omitted where the host has nowhere to send them.
   */
  onTopUp?: (shortfall: number, currencyId: string) => void;
}

/**
 * Confirms a payment, from whichever of the order's balances the applicant picks.
 *
 * Each balance is offered with what the applications would cost in its currency — priced from the
 * services' own prices in it, never converted. A balance whose currency a service is not sold in
 * cannot be chosen; one that is short offers to add the difference rather than leaving somebody at
 * a dead end.
 */
export function PayDialog({ open, applications, onClose, onPaid, onTopUp }: Props) {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage ?? 'en';
  const toMessage = useApiErrorMessage();

  const ids = useMemo(() => applications.map((application) => application.id), [applications]);
  const quote = usePaymentQuote(ids, open);
  const pay = usePayApplications();

  const options = useMemo(() => quote.data?.options ?? [], [quote.data]);
  const [currencyId, setCurrencyId] = useState<string>('');

  // Opening starts from the balance the applications are already priced in, else the main one,
  // and never from last time's error.
  useEffect(() => {
    if (!open) return;
    pay.reset();
    setCurrencyId('');
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  useEffect(() => {
    if (currencyId || options.length === 0) return;
    const start =
      options.find((option) => option.isCurrent && option.total !== null) ??
      options.find((option) => option.canPay) ??
      options.find((option) => option.isMain) ??
      options[0];
    if (start) setCurrencyId(start.currencyId);
  }, [options, currencyId]);

  const chosen = options.find((option) => option.currencyId === currencyId);
  const currency = chosen?.currencyCode ?? applications[0]?.currencyCode ?? '';
  const total = chosen?.total ?? 0;
  const balance = chosen?.balance ?? 0;
  const remaining = balance - total;
  const notSold = chosen !== undefined && chosen.total === null;

  // The server is the authority on affordability; this only pre-empts an obvious rejection.
  const insufficient = chosen !== undefined && !notSold && remaining < 0;
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
          {insufficient && onTopUp && chosen && (
            <Button
              variant="secondary"
              onClick={() => onTopUp(shortfall, chosen.currencyId)}
              data-testid="pay-top-up"
            >
              <Plus className="size-4" aria-hidden="true" />
              {t('payment.addTheDifference')}
            </Button>
          )}

          <Button
            disabled={
              pay.isPending ||
              quote.isPending ||
              !chosen ||
              notSold ||
              insufficient ||
              applications.length === 0
            }
            data-testid="confirm-payment"
            onClick={() =>
              pay.mutate(
                { applicationIds: ids, currencyId },
                { onSuccess: (result) => onPaid(result.applications.length) },
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

        {quote.isPending ? (
          <p className="text-sm text-muted-foreground">{t('common.loading')}</p>
        ) : quote.isError ? (
          <Alert variant="error">{toMessage(quote.error)}</Alert>
        ) : (
          options.length > 1 && (
            <fieldset className="space-y-2">
              <legend className="mb-1 text-sm font-medium">{t('payment.payFrom')}</legend>
              {options.map((option) => (
                <QuoteOption
                  key={option.currencyId}
                  option={option}
                  checked={option.currencyId === currencyId}
                  onPick={() => {
                    pay.reset();
                    setCurrencyId(option.currencyId);
                  }}
                  locale={locale}
                />
              ))}
            </fieldset>
          )
        )}

        <ul className="divide-y divide-border rounded-lg border border-border">
          {applications.map((application) => (
            <li key={application.id} className="flex justify-between gap-3 p-3">
              <span className="font-mono text-xs" dir="ltr">
                {application.applicationNumber}
              </span>
              {/* Its price as it stands; paying in another currency prices it there instead. */}
              <span className="text-muted-foreground">
                {formatCurrency(application.totalCost, application.currencyCode, locale)}
              </span>
            </li>
          ))}
        </ul>

        {chosen && !notSold && (
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
        )}

        {notSold && <Alert variant="warning">{t('payment.notSoldInCurrency', { currency })}</Alert>}

        {chosen && !chosen.isCurrent && !notSold && (
          <Alert variant="info">{t('payment.repricedNotice', { currency })}</Alert>
        )}

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

/** One balance to pay from, with what the selection costs in it. */
function QuoteOption({
  option,
  checked,
  onPick,
  locale,
}: {
  option: PaymentQuoteOptionDto;
  checked: boolean;
  onPick: () => void;
  locale: string;
}) {
  const { t } = useTranslation();
  const unavailable = option.total === null;

  return (
    <label
      className={cn(
        'flex items-center gap-3 rounded-xl border p-3 transition',
        'focus-within:ring-2 focus-within:ring-primary focus-within:ring-offset-1',
        unavailable
          ? 'cursor-not-allowed opacity-50'
          : checked
            ? 'cursor-pointer border-primary bg-primary/5'
            : 'cursor-pointer border-border hover:bg-muted',
      )}
    >
      <input
        type="radio"
        name="pay-from"
        className="sr-only"
        checked={checked}
        disabled={unavailable}
        onChange={onPick}
        data-testid={`pay-from-${option.currencyCode}`}
      />

      <span className="font-mono text-sm font-semibold" dir="ltr">
        {option.currencyCode}
      </span>

      <span className="min-w-0 flex-1 text-sm">
        <span className="block">
          {unavailable
            ? t('payment.notSoldShort')
            : t('payment.totalIn', {
                amount: formatCurrency(option.total ?? 0, option.currencyCode, locale),
              })}
        </span>
        <span className="block text-xs text-muted-foreground">
          {t('payment.balanceIs', {
            amount: formatCurrency(option.balance, option.currencyCode, locale),
          })}
          {!unavailable && !option.canPay && ` · ${t('payment.notEnough')}`}
        </span>
      </span>

      <span
        className={cn(
          'flex size-5 shrink-0 items-center justify-center rounded-full border',
          checked ? 'border-primary bg-primary text-white' : 'border-border',
        )}
        aria-hidden="true"
      >
        {checked && <Check className="size-3.5" strokeWidth={3} />}
      </span>
    </label>
  );
}
