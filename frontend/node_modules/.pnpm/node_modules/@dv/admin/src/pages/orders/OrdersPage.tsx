import { AdminPageHeader, Button } from '@dv/ui';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useCountries, useCurrencies } from '@/features/lookups/api';
import { adminKeys, apiClient } from '@/shared/api/client';
import { formatDateTime, formatNumber } from '@/shared/lib/format';
import { DataTable, type Column, type PagedResult, type SortParams } from '@/shared/ui/DataTable';
import { MultiSelect } from '@/shared/ui/MultiSelect';
import { RowActions } from '@/shared/ui/RowActions';

export interface AdminOrderListItem {
  id: string;
  orderNumber: string;
  email: string;
  languageCode: string;
  countryName: string | null;
  currencyCode: string | null;
  walletBalance: number;
  applicationCount: number;
  createdAtUtc: string;
  lastLoginAtUtc: string | null;
}

interface OrderFilters extends SortParams {
  page: number;
  pageSize: number;
  search?: string;
  countryIds: string[];
  currencyIds: string[];
}

export function OrdersPage() {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage ?? 'en';
  const [filters, setFilters] = useState<OrderFilters>({
    page: 1,
    pageSize: 25,
    countryIds: [],
    currencyIds: [],
  });

  const countries = useCountries({ page: 1, pageSize: 200, isActive: true });
  const currencies = useCurrencies({ page: 1, pageSize: 200, isActive: true });

  const orders = useQuery({
    queryKey: adminKeys.orders(filters),
    queryFn: () =>
      apiClient.get<PagedResult<AdminOrderListItem>>('admin/orders', {
        query: {
          page: filters.page,
          pageSize: filters.pageSize,
          sortBy: filters.sortBy,
          sortDescending: filters.sortDescending,
          search: filters.search || undefined,
          countryIds: filters.countryIds.length ? filters.countryIds : undefined,
          currencyIds: filters.currencyIds.length ? filters.currencyIds : undefined,
        },
      }),
  });

  const hasFilters = filters.countryIds.length > 0 || filters.currencyIds.length > 0;

  const columns: Column<AdminOrderListItem>[] = [
    {
      key: 'orderNumber',
      header: t('orders.orderNumber'),
      getValue: (row) => row.orderNumber,
      render: (row) => (
        <span className="font-mono text-xs font-semibold tracking-wider" dir="ltr">
          {row.orderNumber}
        </span>
      ),
    },
    {
      key: 'email',
      header: t('orders.email'),
      getValue: (row) => row.email,
      render: (row) => (
        <span className="text-sm" dir="ltr">
          {row.email}
        </span>
      ),
    },
    {
      key: 'countryName',
      header: t('lookups.country'),
      getValue: (row) => row.countryName ?? '',
      render: (row) => row.countryName ?? '—',
    },
    {
      key: 'currencyCode',
      header: t('lookups.currencies'),
      getValue: (row) => row.currencyCode ?? '',
      render: (row) =>
        row.currencyCode ? (
          <span className="font-mono text-xs" dir="ltr">
            {row.currencyCode}
          </span>
        ) : (
          '—'
        ),
    },
    {
      key: 'walletBalance',
      header: t('orders.walletBalance'),
      align: 'end',
      getValue: (row) => row.walletBalance,
      render: (row) =>
        row.currencyCode
          ? `${formatNumber(row.walletBalance, locale)} ${row.currencyCode}`
          : formatNumber(row.walletBalance, locale),
    },
    {
      key: 'applicationCount',
      header: t('orders.applications'),
      align: 'center',
      getValue: (row) => row.applicationCount,
      render: (row) => formatNumber(row.applicationCount, locale),
    },
    {
      key: 'createdAtUtc',
      header: t('orders.created'),
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
        <RowActions viewTo={`/orders/${row.id}`} viewTestId={`view-order-${row.orderNumber}`} />
      ),
    },
  ];

  return (
    <div className="animate-fade-in space-y-6">
      <AdminPageHeader
        title={t('orders.title')}
        subtitle={
          orders.data
            ? `${formatNumber(orders.data.totalCount, locale)} · ${t('orders.subtitle')}`
            : t('orders.subtitle')
        }
      />

      <DataTable
        data={orders.data}
        isPending={orders.isPending}
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
            <MultiSelect
              label={t('orders.country')}
              options={(countries.data?.items ?? []).map((country) => ({
                value: country.id,
                label: country.name,
              }))}
              value={filters.countryIds}
              onChange={(countryIds) => setFilters((f) => ({ ...f, countryIds, page: 1 }))}
              placeholder={t('common.all')}
            />

            <MultiSelect
              label={t('orders.currency')}
              options={(currencies.data?.items ?? []).map((currency) => ({
                value: currency.id,
                label: `${currency.name} (${currency.code})`,
              }))}
              value={filters.currencyIds}
              onChange={(currencyIds) => setFilters((f) => ({ ...f, currencyIds, page: 1 }))}
              placeholder={t('common.all')}
            />

            {hasFilters && (
              <Button
                variant="ghost"
                size="sm"
                className="mb-0.5"
                onClick={() =>
                  setFilters((f) => ({ ...f, countryIds: [], currencyIds: [], page: 1 }))
                }
              >
                {t('applications.clearFilters')}
              </Button>
            )}
          </>
        }
        columns={columns}
      />
    </div>
  );
}
