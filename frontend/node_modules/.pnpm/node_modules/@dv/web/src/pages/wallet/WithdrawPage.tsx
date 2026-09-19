import { Alert, Card, CardContent, LoadingState, buttonVariants } from '@dv/ui';
import { ArrowLeft } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { useWallet } from '@/entities/wallet/api';
import { BalancePicker } from '@/features/wallet/BalancePicker';
import { WithdrawForm } from '@/features/wallet/WithdrawForm';
import { useApiErrorMessage } from '@/shared/lib/useApiError';

/**
 * Asking for money back, on its own page: which balance, then how much. The currency travels in
 * the address — `?currency=<id>` — so the wallet can send somebody here for a particular balance.
 */
export function WithdrawPage() {
  const { t } = useTranslation();
  const { lang = 'en' } = useParams<{ lang: string }>();
  const navigate = useNavigate();
  const toMessage = useApiErrorMessage();
  const [searchParams, setSearchParams] = useSearchParams();

  const wallet = useWallet(1);
  const balances = wallet.data?.balances ?? [];
  const withMoney = balances.filter((balance) => balance.balance > 0);

  const requested = searchParams.get('currency');
  const selected =
    withMoney.find((balance) => balance.currencyId === requested) ??
    withMoney.find((balance) => balance.isMain) ??
    withMoney[0];

  const walletPath = `/${lang}/wallet`;

  return (
    <div className="mx-auto max-w-3xl space-y-6 px-4 py-10 sm:px-6">
      <Link
        to={walletPath}
        className={buttonVariants({ variant: 'ghost', size: 'sm' })}
        data-testid="withdraw-back"
      >
        <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden="true" />
        {t('wallet.backToWallet')}
      </Link>

      <h1 className="text-2xl font-semibold">{t('wallet.withdrawTitle')}</h1>

      {wallet.isPending ? (
        <LoadingState label={t('common.loading')} />
      ) : wallet.isError ? (
        <Alert variant="error">{toMessage(wallet.error)}</Alert>
      ) : !wallet.data?.features.withdrawalRequestsEnabled ? (
        <Alert variant="info">{t('wallet.withdrawalsUnavailable')}</Alert>
      ) : !selected ? (
        // Nothing to pay out from any balance.
        <Alert variant="info">{t('wallet.nothingToWithdraw')}</Alert>
      ) : (
        <>
          {balances.length > 1 && (
            <BalancePicker
              balances={balances}
              selectedId={selected.currencyId}
              onSelect={(currencyId) =>
                setSearchParams({ currency: currencyId }, { replace: true })
              }
              isDisabled={(balance) => balance.balance <= 0}
              legend={t('wallet.chooseBalanceToWithdraw')}
            />
          )}

          <Card>
            <CardContent className="p-6">
              <WithdrawForm
                currencyId={selected.currencyId}
                currency={selected.currencyCode}
                balance={selected.balance}
                features={wallet.data.features}
                onCancel={() => navigate(walletPath)}
                onSubmitted={() => navigate(walletPath, { state: { submitted: 'withdrawal' } })}
              />
            </CardContent>
          </Card>
        </>
      )}
    </div>
  );
}
