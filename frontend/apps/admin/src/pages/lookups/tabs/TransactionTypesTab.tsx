import { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { useLocation, useNavigate } from 'react-router-dom';
import { useCountries, useTransactionTypes, type TransactionTypeDto } from '@/features/lookups/api';
import { ConfirmDialog } from '@/shared/ui/CrudDialog';
import { DataTable } from '@/shared/ui/DataTable';
import { NewButton } from '../LookupsPage';
import { useLookupTab } from '../useLookupTab';
import { Notice, ParentFilter } from './shared';
import { actionColumn, nameColumns, type LookupCaps } from './columns';

/**
 * The transaction type list. Creating and editing happen on their own pages rather than in a
 * dialog — the form carries a name in two languages and a country mapping across the whole
 * catalogue, which is more than a popup should hold.
 */
export function TransactionTypesTab({ caps }: { caps: LookupCaps }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const location = useLocation();
  const tab = useLookupTab<TransactionTypeDto, never>('transaction-types');
  const list = useTransactionTypes(tab.params);
  const countries = useCountries({ page: 1, pageSize: 200, isActive: true });

  // The form page saves and navigates back, handing its confirmation over in router state.
  useEffect(() => {
    const notice = (location.state as { notice?: string } | null)?.notice;
    if (!notice) return;

    tab.setNotice(notice);
    navigate(location.pathname, { replace: true, state: null });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [location.state]);

  const countryName = (id: string) =>
    countries.data?.items.find((country) => country.id === id)?.name ?? '—';

  return (
    <div className="space-y-4">
      <Notice message={tab.notice} onDismiss={tab.dismissNotice} />

      <DataTable
        data={list.data}
        isPending={list.isPending}
        rowKey={(row) => row.id}
        onSearch={tab.setSearch}
        onPageChange={tab.setPage}
        onPageSizeChange={tab.setPageSize}
        onSortChange={tab.setSort}
        filters={
          <ParentFilter
            label={t('lookups.country')}
            value={tab.params.countryId}
            options={countries.data?.items ?? []}
            onChange={(countryId) => tab.setParams((p) => ({ ...p, countryId, page: 1 }))}
          />
        }
        toolbar={
          caps.canCreate ? (
            <NewButton
              label={t('lookups.createTitle', { entity: t('lookups.transactionTypes') })}
              onClick={() => navigate('/lookups/transactionTypes/new')}
            />
          ) : null
        }
        columns={[
          {
            key: 'code',
            header: t('lookups.code'),
            getValue: (row) => row.code,
            render: (row) => (
              <span className="font-mono text-xs" dir="ltr">
                {row.code || '—'}
              </span>
            ),
          },
          ...nameColumns<TransactionTypeDto>(t),
          {
            key: 'countries',
            header: t('lookups.countries'),
            getValue: (row) => (row.countryIds ?? []).map(countryName).join(' '),
            render: (row) =>
              (row.countryIds ?? []).length === 0 ? (
                <span className="text-subtle">—</span>
              ) : (
                <div className="flex flex-wrap gap-1">
                  {(row.countryIds ?? []).map((id) => (
                    <span
                      key={id}
                      className="rounded bg-muted px-1.5 py-0.5 text-xs text-muted-foreground"
                    >
                      {countryName(id)}
                    </span>
                  ))}
                </div>
              ),
          },
          actionColumn<TransactionTypeDto>(
            t,
            caps,
            // The row travels with the navigation, so the form opens without a second fetch.
            (row) =>
              navigate(`/lookups/transactionTypes/${row.id}/edit`, {
                state: { transactionType: row },
              }),
            tab.setToDelete,
            (row) => row.nameEn,
          ),
        ]}
      />

      <ConfirmDialog
        open={tab.toDelete !== null}
        title={t('lookups.deleteTitle')}
        body={t('lookups.deleteBody')}
        confirmLabel={t('common.delete')}
        onClose={() => tab.setToDelete(null)}
        onConfirm={tab.confirmDelete}
        isPending={tab.remove.isPending}
        destructive
      />
    </div>
  );
}
