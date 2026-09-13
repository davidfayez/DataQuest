import {
  Alert,
  Button,
  Card,
  CardContent,
  Field,
  Input,
  LoadingState,
  Spinner,
  buttonVariants,
} from '@dv/ui';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { ArrowLeft, ArrowRight } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router-dom';
import { Permissions } from '@/features/auth/session';
import { usePermission } from '@/features/auth/useAdminSession';
import { adminKeys, apiClient } from '@/shared/api/client';
import { formatDateTime, formatNumber } from '@/shared/lib/format';
import { useApiErrorMessage } from '@/shared/lib/useApiError';
import { ConfirmDialog } from '@/shared/ui/CrudDialog';
import { StatusPill } from '@/shared/ui/StatusPill';

interface OrderApplication {
  id: string;
  applicationNumber: string;
  addressedTo: string;
  status: number;
  statusName: string;
  isPaid: boolean;
  totalCost: number;
  canRefund: boolean;
  createdAtUtc: string;
}

interface OrderDetails {
  id: string;
  orderNumber: string;
  email: string;
  countryName: string | null;
  currencyCode: string | null;
  walletBalance: number;
  createdAtUtc: string;
  lastLoginAtUtc: string | null;
  applications: OrderApplication[];
}

interface WalletStatement {
  wallet: { balance: number; currencyCode: string };
  ledger: {
    items: Array<{
      id: string;
      typeName: string;
      signedAmount: number;
      balanceAfter: number;
      referenceApplicationNumbers: string[];
      note: string | null;
      createdAtUtc: string;
    }>;
  };
}

const STATUS_KEY: Record<string, string> = {
  Draft: 'draft',
  PendingPayment: 'pendingPayment',
  Pending: 'pending',
  InProgress: 'inProgress',
  MissedInfo: 'missedInfo',
  Success: 'success',
  Failed: 'failed',
  Refunded: 'refunded',
};

export function OrderDetailPage() {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage ?? 'en';
  const { id = '' } = useParams<{ id: string }>();
  const toMessage = useApiErrorMessage();
  const queryClient = useQueryClient();

  const canCredit = usePermission(Permissions.OrdersCredit);
  const canRefund = usePermission(Permissions.OrdersRefund);

  const [amount, setAmount] = useState('');
  const [note, setNote] = useState('');
  const [toRefund, setToRefund] = useState<OrderApplication | null>(null);

  const order = useQuery({
    queryKey: adminKeys.order(id),
    queryFn: () => apiClient.get<OrderDetails>(`admin/orders/${id}`),
  });

  const wallet = useQuery({
    queryKey: adminKeys.orderWallet(id),
    queryFn: () => apiClient.get<WalletStatement>(`admin/orders/${id}/wallet`),
  });

  function invalidate() {
    void queryClient.invalidateQueries({ queryKey: adminKeys.order(id) });
    void queryClient.invalidateQueries({ queryKey: adminKeys.orderWallet(id) });
  }

  const credit = useMutation({
    mutationFn: () =>
      apiClient.post(`admin/orders/${id}/wallet/credit`, {
        amount: Number(amount),
        note: note || null,
      }),
    onSuccess: () => {
      setAmount('');
      setNote('');
      invalidate();
    },
  });

  const refund = useMutation({
    mutationFn: (applicationId: string) =>
      apiClient.post(`admin/orders/${id}/applications/${applicationId}/refund`, { note: null }),
    onSuccess: () => {
      setToRefund(null);
      invalidate();
    },
  });

  if (order.isPending) return <LoadingState label={t('common.loading')} />;

  const details = order.data!;
  const currency = details.currencyCode ?? '';

  return (
    <div className="space-y-6">
      <Link
        to="/orders"
        className="inline-flex items-center gap-1.5 text-sm text-muted-foreground hover:text-foreground"
      >
        <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden="true" />
        {t('orders.title')}
      </Link>

      <div>
        <h1 className="font-mono text-xl font-semibold" dir="ltr">
          {details.orderNumber}
        </h1>
        <p className="text-sm text-muted-foreground" dir="ltr">
          {details.email}
        </p>
      </div>

      <div className="grid gap-4 lg:grid-cols-3">
        <Card>
          <CardContent className="p-5">
            <p className="text-sm text-muted-foreground">{t('orders.walletBalance')}</p>
            <p className="mt-1 text-2xl font-semibold" data-testid="order-wallet-balance">
              {formatNumber(details.walletBalance, locale)} {currency}
            </p>

            {/* Deposits and payouts are decided in the queue, filtered to this order. */}
            <Link
              to={`/wallet-requests?orderId=${id}`}
              className="mt-3 inline-flex items-center gap-1.5 text-sm text-primary hover:underline"
              data-testid="order-wallet-requests"
            >
              {t('walletRequests.title')}
              <ArrowRight className="size-3.5 rtl:rotate-180" aria-hidden="true" />
            </Link>
          </CardContent>
        </Card>

        {canCredit && (
          <Card className="lg:col-span-2">
            <CardContent className="space-y-3 p-5">
              <h2 className="font-medium">{t('orders.creditWallet')}</h2>

              {credit.isError ? <Alert variant="error">{toMessage(credit.error)}</Alert> : null}
              {credit.isSuccess ? <Alert variant="success">{t('orders.credited')}</Alert> : null}

              <div className="flex flex-wrap items-end gap-3">
                <Field label={t('orders.creditAmount')} htmlFor="amount" className="w-40">
                  <Input
                    id="amount"
                    type="number"
                    min={0.01}
                    step="0.01"
                    value={amount}
                    onChange={(event) => setAmount(event.target.value)}
                    data-testid="credit-amount"
                  />
                </Field>

                <Field label={t('orders.creditNote')} htmlFor="note" className="min-w-48 flex-1">
                  <Input
                    id="note"
                    value={note}
                    onChange={(event) => setNote(event.target.value)}
                  />
                </Field>

                <Button
                  disabled={credit.isPending || !amount || Number(amount) <= 0}
                  onClick={() => credit.mutate()}
                  data-testid="credit-submit"
                >
                  {credit.isPending && <Spinner />}
                  {t('orders.creditSubmit')}
                </Button>
              </div>
            </CardContent>
          </Card>
        )}
      </div>

      <section className="space-y-3">
        <h2 className="font-medium">
          {t('orders.applicationsOnOrder')}{' '}
          <span className="text-muted-foreground">({details.applications.length})</span>
        </h2>

        <div className="overflow-x-auto rounded-md border border-border bg-background shadow-card">
          <table className="w-full min-w-[46rem] text-sm">
            <thead className="border-b border-border bg-muted">
              <tr>
                {[
                  t('applications.reference'),
                  t('applications.addressedTo'),
                  t('table.status'),
                  t('orders.payment'),
                  t('orders.created'),
                ].map((header) => (
                  <th
                    key={header}
                    scope="col"
                    className="px-3 py-2.5 text-start text-2xs font-semibold uppercase tracking-[0.06em] text-muted-foreground"
                  >
                    {header}
                  </th>
                ))}
                <th
                  scope="col"
                  className="px-3 py-2.5 text-end text-2xs font-semibold uppercase tracking-[0.06em] text-muted-foreground"
                >
                  {t('applications.total')}
                </th>
                <th className="px-3 py-2.5" />
              </tr>
            </thead>
            <tbody>
              {details.applications.map((application) => (
                <tr
                  key={application.id}
                  className="border-t border-border transition-colors hover:bg-muted/60"
                >
                  <td className="px-3 py-2.5">
                    <Link
                      to={`/applications/${application.id}`}
                      className="reference text-primary hover:underline"
                      dir="ltr"
                    >
                      {application.applicationNumber}
                    </Link>
                  </td>
                  <td className="max-w-xs truncate px-3 py-2.5">{application.addressedTo}</td>
                  <td className="px-3 py-2.5">
                    <StatusPill
                      statusName={application.statusName}
                      label={t(`status.${STATUS_KEY[application.statusName] ?? 'draft'}`)}
                    />
                  </td>
                  <td className="px-3 py-2.5">
                    <span className={application.isPaid ? 'text-success' : 'text-muted-foreground'}>
                      {application.isPaid ? t('orders.paid') : t('orders.unpaid')}
                    </span>
                  </td>
                  <td className="px-3 py-2.5 whitespace-nowrap text-muted-foreground">
                    {formatDateTime(application.createdAtUtc, locale)}
                  </td>
                  <td className="px-3 py-2.5 text-end whitespace-nowrap">
                    {formatNumber(application.totalCost, locale)} {currency}
                  </td>
                  <td className="px-3 py-2.5 text-end">
                    <div className="flex items-center justify-end gap-1">
                      {/* Refund is offered only where the domain allows it and the admin may. */}
                      {canRefund && application.canRefund && (
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => setToRefund(application)}
                          data-testid={`refund-${application.applicationNumber}`}
                        >
                          {t('orders.refund')}
                        </Button>
                      )}
                      {/* An explicit action so the way into an application's details is obvious. */}
                      <Link
                        to={`/applications/${application.id}`}
                        className={buttonVariants({ variant: 'ghost', size: 'sm' })}
                        data-testid={`open-${application.applicationNumber}`}
                      >
                        {t('orders.view')}
                        <ArrowRight className="size-3.5 rtl:rotate-180" aria-hidden="true" />
                      </Link>
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <section className="space-y-3">
        <h2 className="font-medium">{t('orders.ledger')}</h2>

        <div className="overflow-x-auto rounded-lg border border-border">
          <table className="w-full min-w-[42rem] text-sm">
            <thead className="bg-muted">
              <tr>
                <th scope="col" className="p-3 text-start font-medium">{t('audit.action')}</th>
                <th scope="col" className="p-3 text-start font-medium">{t('applications.reference')}</th>
                <th scope="col" className="p-3 text-start font-medium">{t('audit.date')}</th>
                <th scope="col" className="p-3 text-end font-medium">{t('orders.creditAmount')}</th>
                <th scope="col" className="p-3 text-end font-medium">{t('orders.walletBalance')}</th>
              </tr>
            </thead>
            <tbody>
              {(wallet.data?.ledger.items ?? []).map((entry) => (
                <tr key={entry.id} className="border-t border-border">
                  <td className="p-3">{entry.typeName}</td>
                  <td className="p-3 font-mono text-xs" dir="ltr">
                    {entry.referenceApplicationNumbers.join(', ') || (entry.note ?? '—')}
                  </td>
                  <td className="p-3 whitespace-nowrap text-muted-foreground">
                    {formatDateTime(entry.createdAtUtc, locale)}
                  </td>
                  <td
                    className={`p-3 text-end whitespace-nowrap ${
                      entry.signedAmount < 0 ? 'text-destructive' : 'text-success'
                    }`}
                  >
                    {formatNumber(entry.signedAmount, locale)}
                  </td>
                  <td className="p-3 text-end whitespace-nowrap">
                    {formatNumber(entry.balanceAfter, locale)}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <ConfirmDialog
        open={toRefund !== null}
        title={t('orders.refundTitle')}
        body={t('orders.refundBody', {
          amount: `${formatNumber(toRefund?.totalCost ?? 0, locale)} ${currency}`,
        })}
        confirmLabel={t('orders.refund')}
        onClose={() => setToRefund(null)}
        onConfirm={() => toRefund && refund.mutate(toRefund.id)}
        isPending={refund.isPending}
      />
    </div>
  );
}
