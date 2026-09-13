import { AdminPageHeader, Button, Field, Input, Select } from '@dv/ui';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link } from 'react-router-dom';
import { useAuthorities } from '@/features/lookups/api';
import { adminKeys, apiClient } from '@/shared/api/client';
import { formatDateTime, formatNumber } from '@/shared/lib/format';
import { useLanguage } from '@/shared/lib/useLanguage';
import { DataTable, type PagedResult, type SortParams } from '@/shared/ui/DataTable';
import { RowActions } from '@/shared/ui/RowActions';
import { StatusPill } from '@/shared/ui/StatusPill';

interface QueueItem {
  id: string;
  applicationNumber: string;
  addressedTo: string;
  status: number;
  statusName: string;
  isPaid: boolean;
  totalCost: number;
  currencyCode: string;
  orderNumber: string;
  orderEmail: string;
  countryName: string;
  authorityName: string;
  createdAtUtc: string;
  paidAtUtc: string | null;
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

const STATUS_OPTIONS = [
  { value: 2, key: 'pending' },
  { value: 3, key: 'inProgress' },
  { value: 4, key: 'missedInfo' },
  { value: 5, key: 'success' },
  { value: 6, key: 'failed' },
  { value: 1, key: 'pendingPayment' },
  { value: 0, key: 'draft' },
  { value: 7, key: 'refunded' },
];

interface Filters extends SortParams {
  page: number;
  pageSize: number;
  search?: string;
  status?: number;
  verificationAuthorityId?: string;
  createdFromUtc?: string;
  createdToUtc?: string;
}

export function ApplicationsQueuePage() {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage ?? 'en';
  const lang = useLanguage();
  const [filters, setFilters] = useState<Filters>({ page: 1, pageSize: 25 });

  const authorities = useAuthorities({ page: 1, isActive: true });

  const queue = useQuery({
    queryKey: adminKeys.applications(filters, lang),
    queryFn: () =>
      apiClient.get<PagedResult<QueueItem>>('admin/applications', {
        query: {
          page: filters.page,
          pageSize: filters.pageSize,
          sortBy: filters.sortBy,
          sortDescending: filters.sortDescending,
          search: filters.search || undefined,
          status: filters.status,
          verificationAuthorityId: filters.verificationAuthorityId,
          createdFromUtc: filters.createdFromUtc || undefined,
          createdToUtc: filters.createdToUtc || undefined,
        },
      }),
  });

  const hasFilters =
    filters.status !== undefined ||
    Boolean(filters.verificationAuthorityId) ||
    Boolean(filters.createdFromUtc) ||
    Boolean(filters.createdToUtc);

  return (
    <div className="animate-fade-in space-y-6">
      <AdminPageHeader title={t('applications.title')} subtitle={t('applications.subtitle')} />

      <DataTable
        data={queue.data}
        isPending={queue.isPending}
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
        filters={
          <>
            <Field label={t('applications.filterStatus')} htmlFor="filter-status" className="w-44">
              <Select
                id="filter-status"
                value={filters.status ?? ''}
                onChange={(event) =>
                  setFilters((f) => ({
                    ...f,
                    status: event.target.value === '' ? undefined : Number(event.target.value),
                    page: 1,
                  }))
                }
                data-testid="filter-status"
              >
                <option value="">{t('common.all')}</option>
                {STATUS_OPTIONS.map((option) => (
                  <option key={option.value} value={option.value}>
                    {t(`status.${option.key}`)}
                  </option>
                ))}
              </Select>
            </Field>

            <Field label={t('applications.filterAuthority')} htmlFor="filter-authority" className="w-52">
              <Select
                id="filter-authority"
                value={filters.verificationAuthorityId ?? ''}
                onChange={(event) =>
                  setFilters((f) => ({
                    ...f,
                    verificationAuthorityId: event.target.value || undefined,
                    page: 1,
                  }))
                }
              >
                <option value="">{t('common.all')}</option>
                {authorities.data?.items.map((authority) => (
                  <option key={authority.id} value={authority.id}>
                    {authority.name}
                  </option>
                ))}
              </Select>
            </Field>

            <Field label={t('applications.filterFrom')} htmlFor="filter-from" className="w-40">
              <Input
                id="filter-from"
                type="date"
                value={filters.createdFromUtc ?? ''}
                onChange={(event) =>
                  setFilters((f) => ({ ...f, createdFromUtc: event.target.value, page: 1 }))
                }
              />
            </Field>

            <Field label={t('applications.filterTo')} htmlFor="filter-to" className="w-40">
              <Input
                id="filter-to"
                type="date"
                value={filters.createdToUtc ?? ''}
                onChange={(event) =>
                  setFilters((f) => ({ ...f, createdToUtc: event.target.value, page: 1 }))
                }
              />
            </Field>

            {hasFilters && (
              <Button
                variant="ghost"
                size="sm"
                onClick={() =>
                  setFilters({ page: 1, pageSize: filters.pageSize, search: filters.search })
                }
              >
                {t('applications.clearFilters')}
              </Button>
            )}
          </>
        }
        columns={[
          {
            key: 'applicationNumber',
            header: t('applications.reference'),
            getValue: (row) => row.applicationNumber,
            render: (row) => (
              <Link
                to={`/applications/${row.id}`}
                className="font-mono text-xs text-primary hover:underline"
                dir="ltr"
              >
                {row.applicationNumber}
              </Link>
            ),
          },
          {
            key: 'orderNumber',
            header: t('applications.orderNumber'),
            getValue: (row) => `${row.orderNumber} ${row.orderEmail}`,
            render: (row) => (
              <div>
                <p className="font-mono text-xs" dir="ltr">
                  {row.orderNumber}
                </p>
                <p className="text-xs text-muted-foreground" dir="ltr">
                  {row.orderEmail}
                </p>
              </div>
            ),
          },
          {
            key: 'statusName',
            header: t('table.status'),
            getValue: (row) => row.statusName,
            render: (row) => (
              <StatusPill
                statusName={row.statusName}
                label={t(`status.${STATUS_KEY[row.statusName] ?? 'draft'}`)}
              />
            ),
          },
          {
            key: 'authorityName',
            header: t('applications.authority'),
            getValue: (row) => row.authorityName,
            render: (row) => row.authorityName,
          },
          {
            key: 'totalCost',
            header: t('applications.total'),
            align: 'end',
            getValue: (row) => row.totalCost,
            render: (row) => `${formatNumber(row.totalCost, locale)} ${row.currencyCode}`,
          },
          {
            key: 'createdAtUtc',
            header: t('applications.created'),
            getValue: (row) => row.createdAtUtc,
            render: (row) => formatDateTime(row.createdAtUtc, locale),
          },
          {
            key: 'actions',
            header: '',
            align: 'end',
            sortable: false,
            filterable: false,
            render: (row) => (
              <RowActions
                viewTo={`/applications/${row.id}`}
                viewTestId={`view-application-${row.applicationNumber}`}
              />
            ),
          },
        ]}
      />
    </div>
  );
}
