import { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { useLocation, useNavigate } from 'react-router-dom';
import { useAuthorities, useCountries, type AuthorityDto } from '@/features/lookups/api';
import { ConfirmDialog } from '@/shared/ui/CrudDialog';
import { DataTable } from '@/shared/ui/DataTable';
import { NewButton } from '../LookupsPage';
import { useLookupTab } from '../useLookupTab';
import { Notice, ParentFilter } from './shared';
import { actionColumn, nameColumns, type LookupCaps } from './columns';

/**
 * The authority list. Creating and editing happen on their own pages rather than in a dialog —
 * the form carries a country, a name in two languages and a mapping across the whole transaction
 * cascade, which is more than a popup should hold.
 */
export function AuthoritiesTab({ caps }: { caps: LookupCaps }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const location = useLocation();
  const tab = useLookupTab<AuthorityDto, never>('authorities');
  const list = useAuthorities(tab.params);
  const countries = useCountries({ page: 1, pageSize: 200, isActive: true });

  // The form page saves and navigates back, handing its confirmation over in router state.
  useEffect(() => {
    const notice = (location.state as { notice?: string } | null)?.notice;
    if (!notice) return;

    tab.setNotice(notice);
    // Cleared so a refresh or a back-navigation does not show it again.
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
              label={t('lookups.createTitle', { entity: t('lookups.authorities') })}
              onClick={() => navigate('/lookups/authorities/new')}
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
          ...nameColumns<AuthorityDto>(t),
          {
            key: 'country',
            header: t('lookups.country'),
            getValue: (row) => countryName(row.countryId),
            render: (row) => countryName(row.countryId),
          },
          {
            key: 'subTypes',
            header: t('lookups.subTypes'),
            align: 'center',
            render: (row) => row.subTransactionTypeIds.length,
          },
          actionColumn<AuthorityDto>(
            t,
            caps,
            // The row travels with the navigation, so the form opens without a second fetch.
            (row) => navigate(`/lookups/authorities/${row.id}/edit`, { state: { authority: row } }),
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
