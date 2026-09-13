import { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { useLocation, useNavigate } from 'react-router-dom';
import {
  useCountries,
  useSubTransactionTypes,
  useTransactionTypes,
  type SubTransactionTypeDto,
} from '@/features/lookups/api';
import { ConfirmDialog } from '@/shared/ui/CrudDialog';
import { DataTable } from '@/shared/ui/DataTable';
import { NewButton } from '../LookupsPage';
import { useLookupTab } from '../useLookupTab';
import { Notice, ParentFilter } from './shared';
import { actionColumn, nameColumns, type LookupCaps } from './columns';

/**
 * The sub-transaction type list. Creating and editing happen on their own pages rather than in a
 * dialog — the form carries a parent, a name in two languages and a country mapping, which is
 * more than a popup should hold.
 */
export function SubTransactionTypesTab({ caps }: { caps: LookupCaps }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const location = useLocation();
  const tab = useLookupTab<SubTransactionTypeDto, never>('sub-transaction-types');
  const list = useSubTransactionTypes(tab.params);
  const parents = useTransactionTypes({ page: 1, isActive: true });
  const countries = useCountries({ page: 1, pageSize: 200, isActive: true });

  // The form page saves and navigates back, handing its confirmation over in router state.
  useEffect(() => {
    const notice = (location.state as { notice?: string } | null)?.notice;
    if (!notice) return;

    tab.setNotice(notice);
    navigate(location.pathname, { replace: true, state: null });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [location.state]);

  const parentName = (id: string) =>
    parents.data?.items.find((parent) => parent.id === id)?.name ?? '—';

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
            label={t('lookups.transactionType')}
            value={tab.params.transactionTypeId}
            options={parents.data?.items ?? []}
            onChange={(transactionTypeId) =>
              tab.setParams((p) => ({ ...p, transactionTypeId, page: 1 }))
            }
          />
        }
        toolbar={
          caps.canCreate ? (
            <NewButton
              label={t('lookups.createTitle', { entity: t('lookups.subTransactionTypes') })}
              onClick={() => navigate('/lookups/subTransactionTypes/new')}
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
          ...nameColumns<SubTransactionTypeDto>(t),
          {
            key: 'parent',
            header: t('lookups.transactionType'),
            render: (row) => parentName(row.transactionTypeId),
          },
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
          actionColumn<SubTransactionTypeDto>(
            t,
            caps,
            // The row travels with the navigation, so the form opens without a second fetch.
            (row) =>
              navigate(`/lookups/subTransactionTypes/${row.id}/edit`, {
                state: { subTransactionType: row },
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
