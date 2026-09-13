import { AdminPageHeader, Alert, Button, Dialog, Field, Input, Spinner, buttonVariants } from '@dv/ui';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { History } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useSearchParams } from 'react-router-dom';
import { Permissions } from '@/features/auth/session';
import { usePermission } from '@/features/auth/useAdminSession';
import {
  STATUS_LABEL,
  STATUS_PILL,
  WalletRequestStatus,
  WalletRequestType,
  type AdminWalletRequest,
} from '@/features/wallet/api';
import { ProofLink } from '@/features/wallet/ProofLink';
import { adminKeys, apiClient } from '@/shared/api/client';
import { formatDateTime, formatNumber } from '@/shared/lib/format';
import { useApiErrorMessage } from '@/shared/lib/useApiError';
import { DataTable, type Column, type PagedResult, type SortParams } from '@/shared/ui/DataTable';
import { StatusPill } from '@/shared/ui/StatusPill';

interface Filters extends SortParams {
  page: number;
  pageSize: number;
  search?: string;
  status?: number;
  orderId?: string;
}

type Decision = { request: AdminWalletRequest; approve: boolean };

/** What the reviewer types before a decision is accepted. */
interface DecisionForm {
  confirmedAmount: string;
  confirmedReference: string;
  note: string;
}

/**
 * Deposit and payout requests awaiting an operator. Approving a deposit credits the wallet;
 * approving a payout confirms funds the applicant's request already put on hold, and rejecting
 * either one releases anything held — all of which the server does, so this page only decides.
 */
export function WalletRequestsPage() {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage ?? 'en';
  const toMessage = useApiErrorMessage();
  const queryClient = useQueryClient();

  const [searchParams] = useSearchParams();
  const orderId = searchParams.get('orderId') ?? undefined;

  const canCredit = usePermission(Permissions.OrdersCredit);
  const canWithdraw = usePermission(Permissions.OrdersWithdraw);

  const [filters, setFilters] = useState<Filters>({
    page: 1,
    pageSize: 25,
    status: WalletRequestStatus.Pending,
    orderId,
  });
  const [decision, setDecision] = useState<Decision | null>(null);
  const [form, setForm] = useState<DecisionForm>({
    confirmedAmount: '',
    confirmedReference: '',
    note: '',
  });

  /**
   * Opens the decision with the applicant's claim already in the fields. Prefilled rather than
   * blank because confirming what was claimed is the common case; the reviewer edits only when
   * the statement disagrees, which is exactly when they should be looking closely.
   */
  function openDecision(request: AdminWalletRequest, approve: boolean) {
    setDecision({ request, approve });
    setForm({
      confirmedAmount: String(request.amount),
      confirmedReference: request.referenceNumber ?? '',
      note: '',
    });
  }

  const requests = useQuery({
    queryKey: adminKeys.walletRequests(filters),
    queryFn: () =>
      apiClient.get<PagedResult<AdminWalletRequest>>('admin/wallet-requests', {
        query: {
          page: filters.page,
          pageSize: filters.pageSize,
          sortBy: filters.sortBy,
          sortDescending: filters.sortDescending,
          search: filters.search || undefined,
          status: filters.status,
          orderId: filters.orderId,
        },
      }),
  });

  const decide = useMutation({
    mutationFn: ({ request, approve }: Decision) =>
      apiClient.post<AdminWalletRequest>(
        `admin/wallet-requests/${request.id}/${approve ? 'approve' : 'reject'}`,
        {
          confirmedAmount: form.confirmedAmount.trim() === ''
            ? null
            : Number(form.confirmedAmount),
          confirmedReference: form.confirmedReference.trim() || null,
          note: form.note.trim() || null,
        },
      ),
    onSuccess: () => {
      setDecision(null);
      void queryClient.invalidateQueries({ queryKey: ['admin', 'wallet-requests'] });
      // The decision moved money, so any order or wallet view already loaded is now stale.
      void queryClient.invalidateQueries({ queryKey: ['admin', 'orders'] });
    },
  });

  /** Crediting and paying out are separate grants, so each row is checked against its own. */
  function mayDecide(request: AdminWalletRequest): boolean {
    return request.type === WalletRequestType.Deposit ? canCredit : canWithdraw;
  }

  const columns: Column<AdminWalletRequest>[] = [
    {
      key: 'orderNumber',
      header: t('orders.orderNumber'),
      getValue: (row) => row.orderNumber,
      render: (row) => (
        <Link
          to={`/orders/${row.orderId}`}
          className="font-mono text-xs font-semibold tracking-wider text-primary hover:underline"
          dir="ltr"
        >
          {row.orderNumber}
        </Link>
      ),
    },
    {
      key: 'type',
      header: t('walletRequests.type'),
      getValue: (row) => row.typeName,
      render: (row) =>
        t(
          row.type === WalletRequestType.Deposit
            ? 'walletRequests.deposit'
            : 'walletRequests.withdrawal',
        ),
    },
    {
      key: 'amount',
      header: t('walletRequests.amount'),
      align: 'end',
      getValue: (row) => row.amount,
      render: (row) => `${formatNumber(row.amount, locale)} ${row.currencyCode}`,
    },
    {
      key: 'status',
      header: t('table.status'),
      getValue: (row) => row.statusName,
      render: (row) => (
        <StatusPill
          statusName={STATUS_PILL[row.status] ?? 'Draft'}
          label={t(STATUS_LABEL[row.status] ?? 'walletRequests.pending')}
        />
      ),
    },
    {
      key: 'paymentMethod',
      header: t('walletRequests.paidBy'),
      getValue: (row) => row.paymentMethodName ?? '',
      render: (row) =>
        row.paymentMethodName ? (
          <span className="block max-w-[14rem]">
            <span className="block truncate">{row.paymentMethodName}</span>
            {row.paymentAccountNumber && (
              <span className="block truncate font-mono text-xs text-muted-foreground" dir="ltr">
                {row.paymentAccountNumber}
              </span>
            )}
          </span>
        ) : (
          '—'
        ),
    },
    {
      key: 'referenceNumber',
      header: t('walletRequests.reference'),
      getValue: (row) => row.referenceNumber ?? '',
      render: (row) =>
        row.referenceNumber ? (
          <span className="font-mono text-sm" dir="ltr">
            {row.referenceNumber}
          </span>
        ) : (
          '—'
        ),
    },
    {
      key: 'files',
      header: t('walletRequests.proof'),
      sortable: false,
      filterable: false,
      getValue: (row) => String(row.files.length),
      render: (row) =>
        row.files.length === 0 ? (
          <span className="text-subtle">—</span>
        ) : (
          <div className="flex flex-col gap-0.5">
            {row.files.map((file) => (
              <ProofLink key={file.id} requestId={row.id} file={file} />
            ))}
          </div>
        ),
    },
    {
      key: 'applicantNote',
      header: t('walletRequests.applicantNote'),
      getValue: (row) => row.applicantNote ?? '',
      render: (row) => (
        <span className="block max-w-xs truncate" title={row.applicantNote ?? undefined}>
          {row.applicantNote || '—'}
        </span>
      ),
    },
    {
      key: 'createdAtUtc',
      header: t('walletRequests.requested'),
      getValue: (row) => row.createdAtUtc,
      render: (row) => formatDateTime(row.createdAtUtc, locale),
    },
    {
      key: 'reviewedByName',
      header: t('walletRequests.decidedBy'),
      getValue: (row) => row.reviewedByName ?? '',
      render: (row) =>
        row.reviewedByName ? (
          <span>
            {row.reviewedByName}
            {row.reviewedAtUtc && (
              <span className="block text-xs text-muted-foreground">
                {formatDateTime(row.reviewedAtUtc, locale)}
              </span>
            )}
          </span>
        ) : (
          '—'
        ),
    },
    {
      key: 'actions',
      header: '',
      align: 'end',
      sortable: false,
      filterable: false,
      render: (row) =>
        row.status === WalletRequestStatus.Pending && mayDecide(row) ? (
          <div className="flex items-center justify-end gap-1">
            <Link
              to={`/wallet-requests/${row.id}`}
              className={buttonVariants({ variant: 'ghost', size: 'sm' })}
              aria-label={t('walletRequests.historyFor', { orderNumber: row.orderNumber })}
              data-testid={`history-${row.id}`}
            >
              <History className="size-4" aria-hidden="true" />
            </Link>
            <Button
              variant="ghost"
              size="sm"
              onClick={() => openDecision(row, true)}
              data-testid={`approve-${row.id}`}
            >
              {t('walletRequests.approve')}
            </Button>
            <Button
              variant="ghost"
              size="sm"
              className="text-red-600"
              onClick={() => openDecision(row, false)}
              data-testid={`reject-${row.id}`}
            >
              {t('walletRequests.reject')}
            </Button>
          </div>
        ) : (
          // A decided request has no buttons left, but its trail is exactly what someone comes
          // back for later.
          <div className="flex items-center justify-end">
            <Link
              to={`/wallet-requests/${row.id}`}
              className={buttonVariants({ variant: 'ghost', size: 'sm' })}
              aria-label={t('walletRequests.historyFor', { orderNumber: row.orderNumber })}
              data-testid={`history-${row.id}`}
            >
              <History className="size-4" aria-hidden="true" />
            </Link>
          </div>
        ),
    },
  ];

  const statusFilters: Array<{ label: string; value?: number }> = [
    { label: t('walletRequests.pending'), value: WalletRequestStatus.Pending },
    { label: t('walletRequests.approved'), value: WalletRequestStatus.Approved },
    { label: t('walletRequests.rejected'), value: WalletRequestStatus.Rejected },
    { label: t('common.all') },
  ];

  return (
    <div className="animate-fade-in space-y-6">
      <AdminPageHeader
        title={t('walletRequests.title')}
        subtitle={
          requests.data
            ? `${formatNumber(requests.data.totalCount, locale)} · ${t('walletRequests.subtitle')}`
            : t('walletRequests.subtitle')
        }
      />

      {!canCredit && !canWithdraw && (
        <Alert variant="info">{t('walletRequests.readOnly')}</Alert>
      )}

      {decide.isError && <Alert variant="error">{toMessage(decide.error)}</Alert>}

      {filters.orderId && (
        <Alert variant="info">
          {t('walletRequests.filteredByOrder')}{' '}
          <button
            type="button"
            className="underline"
            onClick={() => setFilters((f) => ({ ...f, orderId: undefined, page: 1 }))}
          >
            {t('applications.clearFilters')}
          </button>
        </Alert>
      )}

      <DataTable
        data={requests.data}
        isPending={requests.isPending}
        rowKey={(row) => row.id}
        onSearch={(search) => setFilters((f) => ({ ...f, search, page: 1 }))}
        onPageChange={(page) => setFilters((f) => ({ ...f, page }))}
        onPageSizeChange={(pageSize) => setFilters((f) => ({ ...f, pageSize, page: 1 }))}
        // Back to the first page: the row that sorts first belongs on page one.
        onSortChange={(sort) =>
          setFilters((f) => ({
            ...f,
            sortBy: sort?.key,
            sortDescending: sort?.descending,
            page: 1,
          }))
        }
        emptyMessage={t('walletRequests.empty')}
        filters={
          <div className="flex flex-wrap gap-1.5">
            {statusFilters.map((option) => (
              <Button
                key={option.label}
                variant={filters.status === option.value ? 'primary' : 'outline'}
                size="sm"
                onClick={() => setFilters((f) => ({ ...f, status: option.value, page: 1 }))}
                data-testid={`status-filter-${option.value ?? 'all'}`}
              >
                {option.label}
              </Button>
            ))}
          </div>
        }
        columns={columns}
      />

      <Dialog
        open={decision !== null}
        onClose={() => setDecision(null)}
        title={t(
          decision?.approve ? 'walletRequests.approveTitle' : 'walletRequests.rejectTitle',
        )}
        footer={
          <>
            <Button variant="outline" onClick={() => setDecision(null)}>
              {t('common.cancel')}
            </Button>
            <Button
              variant={decision?.approve ? 'primary' : 'destructive'}
              // Approving moves money on two figures, so it stays blocked until both are there —
              // the server's 400 should never be the first thing a reviewer sees.
              disabled={
                decide.isPending
                || (decision?.approve === true
                  && (form.confirmedReference.trim() === ''
                    || !(Number(form.confirmedAmount) > 0)))
              }
              onClick={() => decision && decide.mutate(decision)}
              data-testid="confirm-decision"
            >
              {decide.isPending && <Spinner />}
              {t(decision?.approve ? 'walletRequests.approve' : 'walletRequests.reject')}
            </Button>
          </>
        }
      >
        {decision && (
          <div className="space-y-4">
            <p className="text-sm">
              {t(
                decision.approve
                  ? decision.request.type === WalletRequestType.Deposit
                    ? 'walletRequests.approveDepositBody'
                    : 'walletRequests.approveWithdrawalBody'
                  : 'walletRequests.rejectBody',
                {
                  amount: `${formatNumber(decision.request.amount, locale)} ${decision.request.currencyCode}`,
                  orderNumber: decision.request.orderNumber,
                },
              )}
            </p>

            {decision.request.applicantNote && (
              <div className="rounded-md border border-border bg-muted/50 p-3 text-sm">
                <p className="text-xs text-muted-foreground">
                  {t('walletRequests.applicantNote')}
                </p>
                <p className="mt-0.5">{decision.request.applicantNote}</p>
              </div>
            )}

            {/* Prefilled with the claim; the reviewer overwrites it when the statement disagrees,
                and it is the confirmed figure the wallet is credited with. */}
            <div className="grid gap-4 sm:grid-cols-2">
              <Field
                label={t('walletRequests.confirmedAmount')}
                htmlFor="confirmed-amount"
                required={decision.approve}
                hint={t('walletRequests.confirmedAmountHint')}
              >
                <Input
                  id="confirmed-amount"
                  type="number"
                  min={0}
                  step="0.01"
                  value={form.confirmedAmount}
                  onChange={(event) =>
                    setForm({ ...form, confirmedAmount: event.target.value })
                  }
                  data-testid="confirmed-amount"
                />
              </Field>

              <Field
                label={t('walletRequests.confirmedReference')}
                htmlFor="confirmed-reference"
                required={decision.approve}
                hint={t('walletRequests.confirmedReferenceHint')}
              >
                <Input
                  id="confirmed-reference"
                  dir="ltr"
                  className="font-mono"
                  maxLength={100}
                  value={form.confirmedReference}
                  onChange={(event) =>
                    setForm({ ...form, confirmedReference: event.target.value })
                  }
                  data-testid="confirmed-reference"
                />
              </Field>
            </div>

            {decision.approve
              && Number(form.confirmedAmount) !== decision.request.amount
              && form.confirmedAmount.trim() !== '' && (
              <Alert variant="warning" data-testid="amount-differs">
                {t('walletRequests.amountDiffers', {
                  claimed: `${formatNumber(decision.request.amount, locale)} ${decision.request.currencyCode}`,
                  confirmed: `${formatNumber(Number(form.confirmedAmount) || 0, locale)} ${decision.request.currencyCode}`,
                })}
              </Alert>
            )}

            <Field label={t('walletRequests.reviewerNote')} htmlFor="reviewer-note">
              <Input
                id="reviewer-note"
                value={form.note}
                maxLength={1000}
                onChange={(event) => setForm({ ...form, note: event.target.value })}
                placeholder={t('walletRequests.reviewerNotePlaceholder')}
                data-testid="reviewer-note"
              />
            </Field>
          </div>
        )}
      </Dialog>

    </div>
  );
}
