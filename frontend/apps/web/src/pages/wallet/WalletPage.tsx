import {
  Alert,
  Button,
  Card,
  CardContent,
  Input,
  LoadingState,
  Spinner,
  buttonVariants,
  cn,
} from '@dv/ui';
import {
  ArrowDownLeft,
  ArrowUpRight,
  ChevronLeft,
  ChevronRight,
  CreditCard,
  FlaskConical,
  Lock,
  Minus,
  Plus,
  Undo2,
} from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useParams, useSearchParams } from 'react-router-dom';
import {
  ApplicationSort,
  useMyApplications,
  type ApplicationListItemDto,
} from '@/entities/application/listApi';
import { ApplicationStatus } from '@/entities/application/types';
import {
  useSimulateDeposit,
  useWallet,
  WalletRequestType,
  WalletTransactionType,
} from '@/entities/wallet/api';
import { PayDialog } from '@/features/payment/PayDialog';
import { DepositDialog } from '@/features/wallet/DepositDialog';
import { WalletRequestDialog } from '@/features/wallet/WalletRequestDialog';
import { WalletRequestsSection } from '@/features/wallet/WalletRequestsSection';
import { formatCurrency, formatDateTime } from '@/shared/lib/format';
import { useApiErrorMessage } from '@/shared/lib/useApiError';

const TYPE_META = {
  [WalletTransactionType.TopUp]: { key: 'wallet.topUp', icon: Plus, tone: 'text-success' },
  [WalletTransactionType.Payment]: { key: 'wallet.paymentType', icon: ArrowUpRight, tone: 'text-destructive' },
  [WalletTransactionType.Refund]: { key: 'wallet.refundType', icon: ArrowDownLeft, tone: 'text-success' },
  [WalletTransactionType.Withdrawal]: { key: 'wallet.withdrawalType', icon: Lock, tone: 'text-destructive' },
  [WalletTransactionType.WithdrawalReversal]: { key: 'wallet.withdrawalReversalType', icon: Undo2, tone: 'text-success' },
} as const;

/** The ledger filter, in the order the chips are shown. */
const LEDGER_FILTERS: Array<{ key: string; type?: WalletTransactionType }> = [
  { key: 'wallet.filterAll' },
  { key: 'wallet.topUp', type: WalletTransactionType.TopUp },
  { key: 'wallet.paymentType', type: WalletTransactionType.Payment },
  { key: 'wallet.refundType', type: WalletTransactionType.Refund },
  { key: 'wallet.withdrawalType', type: WalletTransactionType.Withdrawal },
];

export function WalletPage() {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage ?? 'en';
  const { lang = 'en' } = useParams<{ lang: string }>();
  const toMessage = useApiErrorMessage();

  const [page, setPage] = useState(1);
  const [filter, setFilter] = useState<WalletTransactionType | undefined>(undefined);
  const [requesting, setRequesting] = useState<WalletRequestType | null>(null);
  const [depositing, setDepositing] = useState(false);
  // Carried from a payment that came up short, so the deposit opens with the figure already in it.
  const [topUpAmount, setTopUpAmount] = useState<number | undefined>(undefined);

  // Arriving from a payment that came up short on another page: open the deposit with the figure
  // already in it, then drop the parameter so a refresh does not reopen the dialog.
  const [searchParams, setSearchParams] = useSearchParams();

  useEffect(() => {
    const requested = Number(searchParams.get('topUp'));
    if (!Number.isFinite(requested) || requested <= 0) return;

    setTopUpAmount(requested);
    setDepositing(true);
    setSearchParams({}, { replace: true });
  }, [searchParams, setSearchParams]);
  const [submitted, setSubmitted] = useState<WalletRequestType | null>(null);
  const [payTargets, setPayTargets] = useState<ApplicationListItemDto[]>([]);
  const [paidCount, setPaidCount] = useState<number | null>(null);
  const [testAmount, setTestAmount] = useState('100');

  const wallet = useWallet(page, filter);
  const simulate = useSimulateDeposit();

  // Unpaid applications are the reason a balance matters, so the wallet offers to settle them
  // rather than sending the applicant back to the dashboard to find them. Filtered server-side:
  // an order with a long history would otherwise page past them.
  const applications = useMyApplications({
    page: 1,
    pageSize: 100,
    status: ApplicationStatus.PendingPayment,
    sortBy: ApplicationSort.CreatedAt,
    sortDescending: true,
  });

  const unpaid = useMemo(() => applications.data?.items ?? [], [applications.data]);
  const unpaidTotal = unpaid.reduce((sum, row) => sum + row.totalCost, 0);

  if (wallet.isPending) {
    return <LoadingState label={t('common.loading')} />;
  }

  const statement = wallet.data;
  const currency = statement?.wallet.currencyCode ?? '';
  const balance = statement?.wallet.balance ?? 0;
  const features = statement?.features;
  const entries = statement?.ledger.items ?? [];
  const ledger = statement?.ledger;

  function changeFilter(type: WalletTransactionType | undefined) {
    setFilter(type);
    // A filter changes what page 1 even means, so paging restarts with it.
    setPage(1);
  }

  return (
    <div className="mx-auto max-w-4xl space-y-6 px-4 py-10 sm:px-6">
      <h1 className="text-2xl font-semibold">{t('wallet.title')}</h1>

      {submitted !== null && (
        <Alert variant="success" data-testid="wallet-request-success">
          {t(
            submitted === WalletRequestType.Deposit
              ? 'wallet.depositSubmitted'
              : 'wallet.withdrawSubmitted',
          )}
        </Alert>
      )}

      {paidCount !== null && (
        <Alert variant="success" data-testid="payment-success">
          {t('payment.success', { count: paidCount })}
        </Alert>
      )}

      <Card>
        <CardContent className="flex flex-wrap items-end justify-between gap-4 p-6">
          <div>
            <p className="text-sm text-muted-foreground">{t('wallet.balance')}</p>
            <p className="mt-1 text-3xl font-semibold" data-testid="wallet-balance">
              {formatCurrency(balance, currency, locale)}
            </p>
          </div>

          {/* Both controls come straight from the server's feature flags — an environment that
              does not offer payouts never renders the button. */}
          <div className="flex flex-wrap gap-2">
            {features?.depositRequestsEnabled && (
              <Button
                onClick={() => setDepositing(true)}
                data-testid="wallet-deposit"
              >
                <Plus className="size-4" aria-hidden="true" />
                {t('wallet.deposit')}
              </Button>
            )}

            {features?.withdrawalRequestsEnabled && (
              <Button
                variant="outline"
                disabled={balance <= 0}
                onClick={() => setRequesting(WalletRequestType.Withdrawal)}
                data-testid="wallet-withdraw"
              >
                <Minus className="size-4" aria-hidden="true" />
                {t('wallet.withdraw')}
              </Button>
            )}
          </div>
        </CardContent>
      </Card>

      {statement && statement.pendingRequests.length > 0 && (
        <Alert variant="info" data-testid="wallet-pending-notice">
          {t('wallet.pendingNotice')}
        </Alert>
      )}

      {/* Settle what is owed without leaving the page. */}
      {unpaid.length > 0 && (
        <Card>
          <CardContent className="flex flex-wrap items-center justify-between gap-4 p-6">
            <div>
              {/* `n` rather than `count`: i18next reserves `count` for plural resolution, and
                  these strings are phrased to work without per-language plural forms. */}
              <p className="font-medium">{t('wallet.unpaidTitle', { n: unpaid.length })}</p>
              <p className="mt-0.5 text-sm text-muted-foreground">
                {t('wallet.unpaidTotal', {
                  amount: formatCurrency(unpaidTotal, currency, locale),
                })}
              </p>
            </div>

            <div className="flex flex-wrap gap-2">
              <Link
                to={`/${lang}/applications`}
                className={buttonVariants({ variant: 'outline' })}
                data-testid="wallet-applications-link"
              >
                {t('wallet.viewApplications')}
              </Link>

              <Button onClick={() => setPayTargets(unpaid)} data-testid="wallet-pay-all">
                <CreditCard className="size-4" aria-hidden="true" />
                {t('wallet.payAll')}
              </Button>
            </div>
          </CardContent>
        </Card>
      )}

      {/* Development and demo environments only; the server refuses this endpoint elsewhere. */}
      {features?.simulatedDepositsEnabled && (
        <Card className="border-warning-border bg-warning-muted/40">
          <CardContent className="space-y-3 p-6">
            <div className="flex items-center gap-2">
              <FlaskConical className="size-4 text-warning" aria-hidden="true" />
              <h2 className="font-medium">{t('wallet.testFundsTitle')}</h2>
            </div>

            <p className="text-sm text-muted-foreground">{t('wallet.testFundsBody')}</p>

            {simulate.isError && <Alert variant="error">{toMessage(simulate.error)}</Alert>}

            <div className="flex flex-wrap items-end gap-3">
              <Input
                type="number"
                min={1}
                step="0.01"
                className="w-40"
                aria-label={t('wallet.amount')}
                value={testAmount}
                onChange={(event) => setTestAmount(event.target.value)}
                data-testid="simulate-amount"
              />

              <Button
                variant="secondary"
                disabled={simulate.isPending || !(Number(testAmount) > 0)}
                onClick={() => simulate.mutate(Number(testAmount))}
                data-testid="simulate-submit"
              >
                {simulate.isPending && <Spinner />}
                {t('wallet.addTestFunds')}
              </Button>
            </div>
          </CardContent>
        </Card>
      )}

      <WalletRequestsSection currency={currency} />

      <section className="space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <h2 className="font-medium">{t('wallet.ledgerTitle')}</h2>

          <div className="flex flex-wrap gap-1.5">
            {LEDGER_FILTERS.map((option) => (
              <button
                key={option.key}
                type="button"
                aria-pressed={filter === option.type}
                onClick={() => changeFilter(option.type)}
                data-testid={`ledger-filter-${option.type ?? 'all'}`}
                className={cn(
                  'inline-flex items-center gap-1.5 rounded-full border px-3 py-1 text-xs font-medium transition-colors',
                  filter === option.type
                    ? 'border-primary bg-primary text-white'
                    : 'border-border text-muted-foreground hover:bg-muted',
                )}
              >
                {filter === option.type && wallet.isFetching && <Spinner />}
                {t(option.key)}
              </button>
            ))}
          </div>
        </div>

        {entries.length === 0 ? (
          <Card>
            <CardContent className="p-10 text-center text-sm text-muted-foreground">
              {t(filter === undefined ? 'wallet.empty' : 'wallet.emptyForFilter')}
            </CardContent>
          </Card>
        ) : (
          // Changing the filter refetches, and the rows on screen are the previous filter's until
          // it lands. Dimming them says so — without it a click on a filter that turns out to
          // match the same rows looks like nothing happened at all.
          <div
            aria-busy={wallet.isFetching}
            className={cn(
              'overflow-x-auto rounded-lg border border-border transition-opacity',
              wallet.isFetching && 'opacity-50',
            )}
          >
            <table className="w-full min-w-[42rem] text-sm">
              <thead className="bg-muted">
                <tr>
                  <th scope="col" className="p-3 text-start font-medium">{t('wallet.type')}</th>
                  <th scope="col" className="p-3 text-start font-medium">{t('wallet.reference')}</th>
                  <th scope="col" className="p-3 text-start font-medium">{t('wallet.date')}</th>
                  <th scope="col" className="p-3 text-end font-medium">{t('wallet.amount')}</th>
                  <th scope="col" className="p-3 text-end font-medium">{t('wallet.balanceAfter')}</th>
                </tr>
              </thead>
              <tbody>
                {entries.map((entry) => {
                  const meta = TYPE_META[entry.type] ?? TYPE_META[WalletTransactionType.TopUp];
                  const Icon = meta.icon;

                  return (
                    <tr key={entry.id} className="border-t border-border">
                      <td className="p-3">
                        <span className="flex items-center gap-2">
                          <Icon className={`size-4 ${meta.tone}`} aria-hidden="true" />
                          {t(meta.key)}
                        </span>
                      </td>

                      <td className="p-3">
                        {entry.referenceApplicationNumbers.length > 0 ? (
                          <span className="font-mono text-xs" dir="ltr">
                            {entry.referenceApplicationNumbers.join(', ')}
                          </span>
                        ) : (
                          <span className="text-muted-foreground">{entry.note ?? '—'}</span>
                        )}
                      </td>

                      <td className="p-3 whitespace-nowrap text-muted-foreground">
                        {formatDateTime(entry.createdAtUtc, locale)}
                      </td>

                      {/* signedAmount comes from the server already carrying its direction. */}
                      <td
                        className={`p-3 text-end font-medium whitespace-nowrap ${
                          entry.signedAmount < 0 ? 'text-destructive' : 'text-success'
                        }`}
                      >
                        {formatCurrency(entry.signedAmount, currency, locale)}
                      </td>

                      <td className="p-3 text-end whitespace-nowrap">
                        {formatCurrency(entry.balanceAfter, currency, locale)}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}

        {/* Without this the ledger stopped at its first page, whatever its length. */}
        {ledger && ledger.totalPages > 1 && (
          <div className="flex items-center justify-between gap-3">
            <p className="text-sm text-muted-foreground" data-testid="ledger-page-label">
              {t('wallet.pageOf', { page: ledger.page, total: ledger.totalPages })}
            </p>

            <div className="flex gap-2">
              <Button
                variant="outline"
                size="sm"
                disabled={!ledger.hasPrevious || wallet.isFetching}
                onClick={() => setPage((current) => Math.max(1, current - 1))}
                data-testid="ledger-previous"
              >
                <ChevronLeft className="size-4 rtl:rotate-180" aria-hidden="true" />
                {t('common.previous')}
              </Button>

              <Button
                variant="outline"
                size="sm"
                disabled={!ledger.hasNext || wallet.isFetching}
                onClick={() => setPage((current) => current + 1)}
                data-testid="ledger-next"
              >
                {t('common.next')}
                <ChevronRight className="size-4 rtl:rotate-180" aria-hidden="true" />
              </Button>
            </div>
          </div>
        )}
      </section>

      {features && (
        <DepositDialog
          open={depositing}
          features={features}
          currency={currency}
          initialAmount={topUpAmount}
          onClose={() => {
            setDepositing(false);
            setTopUpAmount(undefined);
          }}
          onSubmitted={() => {
            setDepositing(false);
            setTopUpAmount(undefined);
            setSubmitted(WalletRequestType.Deposit);
          }}
        />
      )}

      {features && (
        <WalletRequestDialog
          type={requesting}
          features={features}
          balance={balance}
          currency={currency}
          onClose={() => setRequesting(null)}
          onSubmitted={(type) => {
            setRequesting(null);
            setSubmitted(type);
          }}
        />
      )}

      <PayDialog
        open={payTargets.length > 0}
        applications={payTargets}
        onClose={() => setPayTargets([])}
        onPaid={(count) => {
          setPayTargets([]);
          setPaidCount(count);
        }}
        // One dialog closes as the other opens: they are both modals, and a payment that cannot go
        // through has nothing left to show behind the top-up.
        onTopUp={(shortfall) => {
          setPayTargets([]);
          setTopUpAmount(shortfall);
          setDepositing(true);
        }}
      />
    </div>
  );
}
