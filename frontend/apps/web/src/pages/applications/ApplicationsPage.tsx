import {
  Alert,
  Button,
  buttonVariants,
  Card,
  CardContent,
  Dialog,
  Input,
  LoadingState,
  Select,
  cn,
} from '@dv/ui';
import {
  ArrowUpDown,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  ChevronUp,
  CreditCard,
  Eye,
  History,
  Info,
  Pencil,
  Plus,
  Search,
  Trash2,
  Undo2,
  Wallet,
  X,
} from 'lucide-react';
import { useEffect, useMemo, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { StatusBadge } from '@/entities/application/StatusBadge';
import {
  APPLICATIONS_PAGE_SIZES,
  ApplicationSort,
  useApplicationFilters,
  useDeleteApplication,
  useMyApplications,
  useRefundApplication,
  type ApplicationListItemDto,
  type ApplicationQuery,
  type ApplicationSortField,
} from '@/entities/application/listApi';
import { ApplicationStatus } from '@/entities/application/types';
import { statusLabelKey } from '@/entities/application/statusLabels';
import { useWallet } from '@/entities/wallet/api';
import { PayDialog } from '@/features/payment/PayDialog';
import { formatCurrency, formatDate } from '@/shared/lib/format';
import { useApiErrorMessage } from '@/shared/lib/useApiError';

/** Only an application that has been submitted and is awaiting payment can be paid — never a draft. */
const isPayable = (row: ApplicationListItemDto) => row.status === ApplicationStatus.PendingPayment;

const iconButton = 'size-8 p-0';

const INITIAL_QUERY: ApplicationQuery = {
  page: 1,
  pageSize: 25,
  sortBy: ApplicationSort.CreatedAt,
  sortDescending: true,
};

/** The four lookup dropdowns, so the filter bar is described once rather than repeated per select. */
const LOOKUP_FILTERS = [
  { key: 'transactionTypeId', options: 'transactionTypes', label: 'dashboard.transactionType' },
  { key: 'subTransactionTypeId', options: 'subTransactionTypes', label: 'dashboard.subTransactionType' },
  { key: 'verificationAuthorityId', options: 'verificationAuthorities', label: 'dashboard.authority' },
  { key: 'serviceTypeId', options: 'serviceTypes', label: 'dashboard.serviceType' },
] as const;

/** A blank cell, so an empty column reads as "nothing here" rather than as a rendering fault. */
function Empty() {
  return <span className="text-muted-foreground">—</span>;
}

/**
 * The payment column.
 *
 * A row still awaiting payment offers the action itself rather than the word "Unpaid". That is the
 * one payment state the applicant is expected to do something about, and a label they have to
 * mentally translate into "so click the small card icon at the end of the row" is a worse column
 * than a button that says what it does.
 *
 * Unpaid is not the same as payable: a draft has nothing to pay for yet and a refunded application
 * will not take another payment, so both keep the plain label rather than offering an action the
 * server would refuse.
 */
function PaymentCell({
  row,
  onPay,
}: {
  row: ApplicationListItemDto;
  onPay: (row: ApplicationListItemDto) => void;
}) {
  const { t } = useTranslation();

  if (row.isPaid) {
    return <span className="text-success">{t('dashboard.paid')}</span>;
  }

  if (!isPayable(row)) {
    return <span className="text-muted-foreground">{t('dashboard.unpaid')}</span>;
  }

  return (
    <Button
      size="sm"
      onClick={() => onPay(row)}
      /* Every payable row carries this button, so on its own the label reads as "pay now, pay
         now, pay now" to a screen reader. The application number says which one. */
      aria-label={t('dashboard.payNowFor', { reference: row.applicationNumber })}
      data-testid={`pay-${row.applicationNumber}`}
    >
      <CreditCard className="size-3.5" aria-hidden="true" />
      {t('dashboard.payNow')}
    </Button>
  );
}

interface SortableHeaderProps {
  field: ApplicationSortField;
  label: string;
  sortBy: ApplicationSortField;
  sortDescending: boolean;
  onSort: (field: ApplicationSortField) => void;
  align?: 'start' | 'end';
}

/**
 * A sortable column header: the label plus an arrow showing the direction while it is the active
 * key. Declared at module scope rather than inside the page — a component defined during render is
 * a new type every time, so React would remount these and the button would lose keyboard focus on
 * the very click that sorted the table.
 */
function SortableHeader({
  field,
  label,
  sortBy,
  sortDescending,
  onSort,
  align = 'start',
}: SortableHeaderProps) {
  const isActive = sortBy === field;
  const Icon = !isActive ? ArrowUpDown : sortDescending ? ChevronDown : ChevronUp;

  return (
    <th
      scope="col"
      className="p-0 font-medium"
      aria-sort={isActive ? (sortDescending ? 'descending' : 'ascending') : 'none'}
    >
      <button
        type="button"
        onClick={() => onSort(field)}
        data-testid={`sort-${field}`}
        className={cn(
          'flex w-full items-center gap-1.5 whitespace-nowrap p-3 transition-colors hover:text-primary',
          align === 'end' ? 'justify-end' : 'justify-start',
          isActive && 'text-primary',
        )}
      >
        {label}
        <Icon className="size-3.5 shrink-0 opacity-70" aria-hidden="true" />
      </button>
    </th>
  );
}

export function ApplicationsPage() {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage ?? 'en';
  const isArabic = locale.startsWith('ar');
  const { lang = 'en' } = useParams<{ lang: string }>();
  const navigate = useNavigate();
  const toMessage = useApiErrorMessage();

  const [query, setQuery] = useState<ApplicationQuery>(INITIAL_QUERY);
  const [searchInput, setSearchInput] = useState('');

  const applications = useMyApplications(query);
  const filters = useApplicationFilters();
  const wallet = useWallet();
  const remove = useDeleteApplication();
  const refund = useRefundApplication();

  const [selected, setSelected] = useState<Set<string>>(new Set());
  // The applications the pay dialog is settling: the whole selection for a bulk pay, or a single
  // row for the per-row pay button. Empty means the dialog is closed.
  const [payTargets, setPayTargets] = useState<ApplicationListItemDto[]>([]);
  const [paidCount, setPaidCount] = useState<number | null>(null);
  const [toDelete, setToDelete] = useState<ApplicationListItemDto | null>(null);
  const [toRefund, setToRefund] = useState<ApplicationListItemDto | null>(null);

  // Typing filters as you go, but one request per pause rather than one per keystroke.
  useEffect(() => {
    const handle = setTimeout(
      () => setQuery((current) => ({ ...current, search: searchInput.trim(), page: 1 })),
      300,
    );

    return () => clearTimeout(handle);
  }, [searchInput]);

  // Memoised so `payable` below has a stable dependency; `?? []` would otherwise produce a new
  // array on every render and defeat the memo entirely.
  const rows = useMemo(() => applications.data?.items ?? [], [applications.data]);
  const currency = wallet.data?.wallet.currencyCode ?? '';

  // Only an application awaiting payment can go into a payment batch.
  const payable = useMemo(() => rows.filter(isPayable), [rows]);
  const selectedRows = useMemo(
    () => payable.filter((row) => selected.has(row.id)),
    [payable, selected],
  );
  const selectedTotal = selectedRows.reduce((sum, row) => sum + row.totalCost, 0);

  const activeFilterCount = [
    query.status,
    query.transactionTypeId,
    query.subTransactionTypeId,
    query.verificationAuthorityId,
    query.serviceTypeId,
  ].filter((value) => value !== undefined && value !== '').length;

  /** Any change to what is being listed resets to page one — page 4 of a new result set is noise. */
  function update(patch: Partial<ApplicationQuery>) {
    setQuery((current) => ({ ...current, ...patch, page: 1 }));
    // A selection carries ids that may not survive the new filter, and the sticky bar would then
    // total rows the applicant can no longer see.
    setSelected(new Set());
  }

  function toggleSort(field: ApplicationSortField) {
    setQuery((current) => ({
      ...current,
      sortBy: field,
      // First click on a new column sorts descending; clicking the active one flips it.
      sortDescending: current.sortBy === field ? !current.sortDescending : true,
      page: 1,
    }));
  }

  function clearFilters() {
    setSearchInput('');
    setQuery({ ...INITIAL_QUERY, pageSize: query.pageSize });
    setSelected(new Set());
  }

  function toggle(id: string) {
    setSelected((previous) => {
      const next = new Set(previous);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  function toggleAll() {
    setSelected((previous) =>
      previous.size === payable.length ? new Set() : new Set(payable.map((row) => row.id)),
    );
  }

  /** The two props every sortable header needs, so each call site names only its own column. */
  const sortProps = {
    sortBy: query.sortBy,
    sortDescending: query.sortDescending,
    onSort: toggleSort,
  };

  if (applications.isPending) {
    return <LoadingState label={t('common.loading')} />;
  }

  const page = applications.data;
  const options = filters.data;

  return (
    <div className="mx-auto max-w-6xl space-y-6 px-4 py-10 pb-28 sm:px-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <h1 className="text-2xl font-semibold">{t('dashboard.title')}</h1>

        <div className="flex flex-wrap items-center gap-3">
          <Link
            to={`/${lang}/wallet`}
            className={buttonVariants({ variant: 'outline' })}
            data-testid="wallet-link"
          >
            <Wallet className="size-4" aria-hidden="true" />
            {currency
              ? formatCurrency(wallet.data?.wallet.balance ?? 0, currency, locale)
              : t('wallet.title')}
          </Link>

          <Link
            to={`/${lang}/applications/new`}
            className={buttonVariants()}
            data-testid="new-application"
          >
            <Plus className="size-4" aria-hidden="true" />
            {t('dashboard.newApplication')}
          </Link>
        </div>
      </div>

      {paidCount !== null && (
        <Alert variant="success" data-testid="payment-success">
          {t('payment.success', { count: paidCount })}
        </Alert>
      )}

      {(remove.isError || refund.isError) && (
        <Alert variant="error">{toMessage(remove.error ?? refund.error)}</Alert>
      )}

      {/* Search and filters. Every option comes from the server's counts, so nothing offered here
          can produce an empty grid. */}
      <div className="space-y-3 rounded-xl border border-border bg-muted/30 p-4">
        <div className="flex flex-wrap items-center gap-3">
          <div className="relative min-w-56 flex-1">
            <Search
              className="pointer-events-none absolute inset-y-0 start-3 my-auto size-4 text-muted-foreground"
              aria-hidden="true"
            />
            <Input
              type="search"
              className="ps-9"
              aria-label={t('dashboard.searchLabel')}
              placeholder={t('dashboard.searchPlaceholder')}
              value={searchInput}
              onChange={(event) => setSearchInput(event.target.value)}
              data-testid="applications-search"
            />
          </div>

          {(activeFilterCount > 0 || searchInput !== '') && (
            <Button variant="ghost" onClick={clearFilters} data-testid="clear-filters">
              <X className="size-4" aria-hidden="true" />
              {t('dashboard.clearFilters')}
            </Button>
          )}
        </div>

        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-5">
          <Select
            aria-label={t('dashboard.statusColumn')}
            value={query.status ?? ''}
            onChange={(event) =>
              update({
                status: event.target.value === '' ? undefined : (Number(event.target.value) as ApplicationStatus),
              })
            }
            data-testid="filter-status"
          >
            <option value="">{t('dashboard.allStatuses')}</option>
            {(options?.statuses ?? []).map((option) => (
              <option key={option.status} value={option.status}>
                {t(statusLabelKey(option.status))} ({option.count})
              </option>
            ))}
          </Select>

          {LOOKUP_FILTERS.map(({ key, options: optionsKey, label }) => (
            <Select
              key={key}
              aria-label={t(label)}
              value={query[key] ?? ''}
              onChange={(event) => update({ [key]: event.target.value || undefined })}
              data-testid={`filter-${key}`}
            >
              <option value="">{t(label)}</option>
              {(options?.[optionsKey] ?? []).map((option) => (
                <option key={option.id} value={option.id}>
                  {option.name} ({option.count})
                </option>
              ))}
            </Select>
          ))}
        </div>
      </div>

      {/* Nudge toward paying several at once, shown only while there is something to pay. */}
      {payable.length > 0 && (
        <div
          role="note"
          data-testid="bulk-pay-hint"
          className="flex items-start gap-3 rounded-xl border border-info-border bg-info-muted/70 p-4"
        >
          <span className="grid size-9 shrink-0 place-items-center rounded-full bg-info text-white shadow-sm">
            <Info className="size-5" aria-hidden="true" />
          </span>
          <div className="space-y-0.5">
            <p className="text-sm font-semibold text-foreground">{t('dashboard.bulkPayHintTitle')}</p>
            <p className="text-sm text-muted-foreground">{t('dashboard.bulkPayHint')}</p>
          </div>
        </div>
      )}

      {rows.length === 0 ? (
        <Card>
          <CardContent className="space-y-2 p-12 text-center">
            {/* A grid emptied by a filter is a different problem from having no applications. */}
            <p className="font-medium">
              {activeFilterCount > 0 || query.search
                ? t('dashboard.noMatches')
                : t('dashboard.empty')}
            </p>
            <p className="text-sm text-muted-foreground">
              {activeFilterCount > 0 || query.search
                ? t('dashboard.noMatchesHint')
                : t('dashboard.emptyHint')}
            </p>
          </CardContent>
        </Card>
      ) : (
        <div className="overflow-x-auto rounded-lg border border-border">
          {/* Eleven columns do not fit a laptop viewport, so the table scrolls sideways inside
              this box rather than widening the page. */}
          <table className="w-full min-w-[92rem] text-sm">
            <thead className="bg-muted">
              <tr>
                <th scope="col" className="w-10 p-3">
                  {payable.length > 0 && (
                    <input
                      type="checkbox"
                      className="size-4 rounded border-border"
                      aria-label={t('dashboard.selectAll')}
                      checked={selected.size === payable.length && payable.length > 0}
                      onChange={toggleAll}
                      data-testid="select-all"
                    />
                  )}
                </th>

                <SortableHeader
                  {...sortProps}
                  field={ApplicationSort.ApplicationNumber}
                  label={t('dashboard.applicationNumber')}
                />
                <SortableHeader
                  {...sortProps}
                  field={ApplicationSort.ApplicantName}
                  label={t('dashboard.applicantName')}
                />
                <SortableHeader
                  {...sortProps}
                  field={ApplicationSort.AddressedTo}
                  label={t('dashboard.addressedTo')}
                />
                <SortableHeader
                  {...sortProps}
                  field={ApplicationSort.TransactionType}
                  label={t('dashboard.transactionType')}
                />
                <SortableHeader
                  {...sortProps}
                  field={ApplicationSort.SubTransactionType}
                  label={t('dashboard.subTransactionType')}
                />
                <SortableHeader
                  {...sortProps}
                  field={ApplicationSort.Authority}
                  label={t('dashboard.authority')}
                />

                {/* Unsorted: a row can hold several services, so there is nothing single to
                    order on. Left as a plain header rather than a button that does nothing. */}
                <th scope="col" className="whitespace-nowrap p-3 text-start font-medium">
                  {t('dashboard.services')}
                </th>

                <SortableHeader
                  {...sortProps}
                  field={ApplicationSort.TotalCost}
                  label={t('dashboard.total')}
                  align="end"
                />
                <SortableHeader
                  {...sortProps}
                  field={ApplicationSort.IsPaid}
                  label={t('dashboard.payment')}
                />
                <SortableHeader
                  {...sortProps}
                  field={ApplicationSort.Status}
                  label={t('dashboard.statusColumn')}
                />
                <SortableHeader
                  {...sortProps}
                  field={ApplicationSort.CreatedAt}
                  label={t('dashboard.created')}
                />

                {/* Deliberately unlabelled: the icons carry their own names, and a header over
                    them added a column title with nothing to say. */}
                <th scope="col" className="p-3">
                  <span className="sr-only">{t('dashboard.actions')}</span>
                </th>
              </tr>
            </thead>

            <tbody>
              {rows.map((row) => (
                <tr key={row.id} className="border-t border-border" data-testid={`row-${row.applicationNumber}`}>
                  <td className="p-3">
                    {isPayable(row) && (
                      <input
                        type="checkbox"
                        className="size-4 rounded border-border"
                        aria-label={t('dashboard.selectRow')}
                        checked={selected.has(row.id)}
                        onChange={() => toggle(row.id)}
                        data-testid={`select-${row.applicationNumber}`}
                      />
                    )}
                  </td>

                  <td className="p-3 whitespace-nowrap">
                    <Link
                      to={`/${lang}/applications/${row.id}`}
                      className="font-mono text-xs text-primary hover:underline"
                      dir="ltr"
                    >
                      {row.applicationNumber}
                    </Link>
                  </td>

                  {/* The name in the reader's own script, falling back to the other one rather
                      than showing a blank cell. */}
                  <td className="max-w-[9rem] truncate p-3">
                    {(isArabic
                      ? row.applicantNameAr ?? row.applicantNameEn
                      : row.applicantNameEn ?? row.applicantNameAr) ?? <Empty />}
                  </td>

                  {/* `title` on each of these: a draft's authority name can be long, and the cell
                      truncates rather than letting one column push the table wider still. */}
                  <td className="max-w-[10rem] truncate p-3" title={row.addressedTo}>
                    {row.addressedTo}
                  </td>

                  <td
                    className="max-w-[9rem] truncate p-3"
                    title={row.transactionTypeName ?? undefined}
                  >
                    {row.transactionTypeName ?? <Empty />}
                  </td>

                  <td
                    className="max-w-[9rem] truncate p-3"
                    title={row.subTransactionTypeName ?? undefined}
                  >
                    {row.subTransactionTypeName ?? <Empty />}
                  </td>

                  <td
                    className="max-w-[10rem] truncate p-3"
                    title={row.verificationAuthorityName ?? undefined}
                  >
                    {row.verificationAuthorityName ?? <Empty />}
                  </td>

                  {/* Usually one service. More than one is named in the tooltip rather than
                      wrapped, so row heights stay uniform. */}
                  <td
                    className="max-w-[10rem] truncate p-3"
                    title={row.serviceTypeNames.join(', ') || undefined}
                  >
                    {row.serviceTypeNames.length > 0 ? (
                      <>
                        {row.serviceTypeNames[0]}
                        {row.serviceTypeNames.length > 1 && (
                          <span className="text-muted-foreground">
                            {' '}
                            +{row.serviceTypeNames.length - 1}
                          </span>
                        )}
                      </>
                    ) : (
                      <Empty />
                    )}
                  </td>

                  <td className="p-3 text-end whitespace-nowrap">
                    {formatCurrency(row.totalCost, row.currencyCode || currency, locale)}
                  </td>

                  <td className="p-3 whitespace-nowrap">
                    <PaymentCell row={row} onPay={(target) => setPayTargets([target])} />
                  </td>

                  <td className="p-3"><StatusBadge status={row.status} /></td>

                  <td className="p-3 whitespace-nowrap text-muted-foreground">
                    {formatDate(row.createdAtUtc, locale)}
                  </td>

                  {/* Actions render strictly from the server's capability flags — the UI never
                      decides for itself whether something may be edited, deleted or refunded. */}
                  <td className="p-3">
                    <div className="flex items-center justify-end gap-0.5">
                      <Link
                        to={`/${lang}/applications/${row.id}`}
                        className={cn(buttonVariants({ variant: 'ghost', size: 'sm' }), iconButton)}
                        aria-label={t('dashboard.view')}
                        title={t('dashboard.view')}
                        data-testid={`view-${row.applicationNumber}`}
                      >
                        <Eye className="size-4" aria-hidden="true" />
                      </Link>

                      {row.canEdit && (
                        <Link
                          to={`/${lang}/applications/${row.id}/edit`}
                          className={cn(buttonVariants({ variant: 'ghost', size: 'sm' }), iconButton)}
                          aria-label={t('dashboard.edit')}
                          title={t('dashboard.edit')}
                          data-testid={`edit-${row.applicationNumber}`}
                        >
                          <Pencil className="size-4" aria-hidden="true" />
                        </Link>
                      )}

                      {/* Always offered: history exists for every application, including one that
                          can no longer be edited — arguably especially for that one. */}
                      <Link
                        to={`/${lang}/applications/${row.id}/log`}
                        className={cn(buttonVariants({ variant: 'ghost', size: 'sm' }), iconButton)}
                        aria-label={t('dashboard.log')}
                        title={t('dashboard.log')}
                        data-testid={`log-${row.applicationNumber}`}
                      >
                        <History className="size-4" aria-hidden="true" />
                      </Link>

                      {row.canDelete && (
                        <Button
                          variant="ghost"
                          size="sm"
                          className={cn(iconButton, 'text-destructive')}
                          aria-label={t('dashboard.delete')}
                          title={t('dashboard.delete')}
                          onClick={() => setToDelete(row)}
                          data-testid={`delete-${row.applicationNumber}`}
                        >
                          <Trash2 className="size-4" aria-hidden="true" />
                        </Button>
                      )}

                      {row.canRefund && (
                        <Button
                          variant="ghost"
                          size="sm"
                          className={iconButton}
                          aria-label={t('dashboard.refund')}
                          title={t('dashboard.refund')}
                          onClick={() => setToRefund(row)}
                          data-testid={`refund-${row.applicationNumber}`}
                        >
                          <Undo2 className="size-4" aria-hidden="true" />
                        </Button>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}

      {page && page.totalCount > 0 && (
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex items-center gap-3">
            <p className="text-sm text-muted-foreground" data-testid="applications-count">
              {t('dashboard.showing', {
                from: (page.page - 1) * page.pageSize + 1,
                to: (page.page - 1) * page.pageSize + rows.length,
                total: page.totalCount,
              })}
            </p>

            <Select
              className="h-9 w-auto"
              aria-label={t('dashboard.rowsPerPage')}
              value={query.pageSize}
              onChange={(event) => update({ pageSize: Number(event.target.value) })}
              data-testid="page-size"
            >
              {APPLICATIONS_PAGE_SIZES.map((size) => (
                <option key={size} value={size}>
                  {size}
                </option>
              ))}
            </Select>
          </div>

          {page.totalPages > 1 && (
            <div className="flex items-center gap-2">
              <span className="text-sm text-muted-foreground">
                {t('wallet.pageOf', { page: page.page, total: page.totalPages })}
              </span>

              <Button
                variant="outline"
                size="sm"
                disabled={!page.hasPrevious || applications.isFetching}
                onClick={() => setQuery((c) => ({ ...c, page: Math.max(1, c.page - 1) }))}
                data-testid="applications-previous"
              >
                <ChevronLeft className="size-4 rtl:rotate-180" aria-hidden="true" />
                {t('common.previous')}
              </Button>

              <Button
                variant="outline"
                size="sm"
                disabled={!page.hasNext || applications.isFetching}
                onClick={() => setQuery((c) => ({ ...c, page: c.page + 1 }))}
                data-testid="applications-next"
              >
                {t('common.next')}
                <ChevronRight className="size-4 rtl:rotate-180" aria-hidden="true" />
              </Button>
            </div>
          )}
        </div>
      )}

      {/* Sticky bar: stays reachable while the table scrolls. */}
      {selectedRows.length > 0 && (
        <div className="fixed inset-x-0 bottom-0 z-10 border-t border-border bg-background/95 p-4 backdrop-blur">
          <div className="mx-auto flex max-w-6xl flex-wrap items-center justify-between gap-3">
            <p className="text-sm">
              <span data-testid="selected-count">
                {t('dashboard.selected', { count: selectedRows.length })}
              </span>
              <span className="ms-2 font-semibold" data-testid="selected-total">
                {formatCurrency(selectedTotal, currency, locale)}
              </span>
            </p>

            <div className="flex gap-2">
              <Button variant="outline" onClick={() => setSelected(new Set())}>
                {t('common.cancel')}
              </Button>
              <Button onClick={() => setPayTargets(selectedRows)} data-testid="pay-selected">
                {t('dashboard.paySelected')}
              </Button>
            </div>
          </div>
        </div>
      )}

      <PayDialog
        open={payTargets.length > 0}
        applications={payTargets}
        onClose={() => setPayTargets([])}
        onPaid={(count) => {
          setPayTargets([]);
          setSelected(new Set());
          setPaidCount(count);
        }}
        // Adding funds is the wallet's business, so this hands over to its Add funds page rather
        // than growing a second copy of the deposit flow here. The balance and the shortfall travel
        // in the URL, so both are filled in on arrival.
        onTopUp={(shortfall, currencyId) => {
          setPayTargets([]);
          navigate(`/${lang}/wallet/add-funds?currency=${currencyId}&amount=${encodeURIComponent(shortfall.toFixed(2))}`);
        }}
      />

      <Dialog
        open={toDelete !== null}
        onClose={() => setToDelete(null)}
        title={t('dashboard.deleteTitle')}
        footer={
          <>
            <Button variant="outline" onClick={() => setToDelete(null)}>
              {t('dashboard.keepIt')}
            </Button>
            <Button
              variant="destructive"
              data-testid="confirm-delete"
              onClick={() => {
                if (toDelete) remove.mutate(toDelete.id, { onSuccess: () => setToDelete(null) });
              }}
            >
              {t('dashboard.confirmDelete')}
            </Button>
          </>
        }
      >
        {t('dashboard.deleteBody', { reference: toDelete?.applicationNumber ?? '' })}
      </Dialog>

      <Dialog
        open={toRefund !== null}
        onClose={() => setToRefund(null)}
        title={t('dashboard.refundTitle')}
        footer={
          <>
            <Button variant="outline" onClick={() => setToRefund(null)}>
              {t('dashboard.keepIt')}
            </Button>
            <Button
              data-testid="confirm-refund"
              onClick={() => {
                if (toRefund) {
                  refund.mutate(
                    { applicationId: toRefund.id },
                    { onSuccess: () => setToRefund(null) },
                  );
                }
              }}
            >
              {t('dashboard.confirmRefund')}
            </Button>
          </>
        }
      >
        {t('dashboard.refundBody', {
          amount: formatCurrency(toRefund?.totalCost ?? 0, currency, locale),
        })}
      </Dialog>
    </div>
  );
}
