import { Alert, Card, CardContent, LoadingState, buttonVariants } from '@dv/ui';
import { ArrowLeft } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom';
import { useWallet } from '@/entities/wallet/api';
import { BalancePicker } from '@/features/wallet/BalancePicker';
import { DepositForm } from '@/features/wallet/DepositForm';
import { useApiErrorMessage } from '@/shared/lib/useApiError';

/**
 * Adding funds, on its own page rather than in a popup: which balance, then how the money is being
 * sent, then what was sent.
 *
 * The currency and a suggested amount travel in the address — `?currency=<id>&amount=<n>` — so a
 * payment that came up short can send somebody straight here with the figure already filled in.
 */
export function AddFundsPage() {
  const { t } = useTranslation();
  const { lang = 'en' } = useParams<{ lang: string }>();
  const navigate = useNavigate();
  const toMessage = useApiErrorMessage();
  const [searchParams, setSearchParams] = useSearchParams();

  const wallet = useWallet(1);
  const balances = wallet.data?.balances ?? [];

  const requested = searchParams.get('currency');
  const selected =
    balances.find((balance) => balance.currencyId === requested) ??
    balances.find((balance) => balance.isMain) ??
    balances[0];

  const suggested = Number(searchParams.get('amount'));
  const initialAmount = Number.isFinite(suggested) && suggested > 0 ? suggested : undefined;

  const walletPath = `/${lang}/wallet`;

  function choose(currencyId: string) {
    // The suggested amount belonged to the balance it was worked out for.
    setSearchParams({ currency: currencyId }, { replace: true });
  }

  return (
    <div className="mx-auto max-w-3xl space-y-6 px-4 py-10 sm:px-6">
      <Link
        to={walletPath}
        className={buttonVariants({ variant: 'ghost', size: 'sm' })}
        data-testid="add-funds-back"
      >
        <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden="true" />
        {t('wallet.backToWallet')}
      </Link>

      <div>
        <h1 className="text-2xl font-semibold">{t('wallet.depositTitle')}</h1>
        <p className="mt-1 text-sm text-muted-foreground">{t('wallet.addFundsSubtitle')}</p>
      </div>

      {wallet.isPending ? (
        <LoadingState label={t('common.loading')} />
      ) : wallet.isError ? (
        <Alert variant="error">{toMessage(wallet.error)}</Alert>
      ) : !wallet.data?.features.depositRequestsEnabled ? (
        <Alert variant="info">{t('wallet.depositsUnavailable')}</Alert>
      ) : !selected ? (
        <Alert variant="info">{t('wallet.noPaymentMethods')}</Alert>
      ) : (
        <>
          {balances.length > 1 && (
            <BalancePicker
              balances={balances}
              selectedId={selected.currencyId}
              onSelect={choose}
              legend={t('wallet.chooseBalanceToFund')}
            />
          )}

          <Card>
            <CardContent className="p-6">
              <DepositForm
                currencyId={selected.currencyId}
                currency={selected.currencyCode}
                features={wallet.data.features}
                initialAmount={selected.currencyId === requested ? initialAmount : undefined}
                onCancel={() => navigate(walletPath)}
                onSubmitted={() => navigate(walletPath, { state: { submitted: 'deposit' } })}
              />
            </CardContent>
          </Card>
        </>
      )}
    </div>
  );
}
