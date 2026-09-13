import { useEffect } from 'react';
import { useTranslation } from 'react-i18next';
import { useLocation, useNavigate } from 'react-router-dom';
import { useCountries, type CountryDto } from '@/features/lookups/api';
import { ConfirmDialog } from '@/shared/ui/CrudDialog';
import { DataTable } from '@/shared/ui/DataTable';
import { NewButton } from '../LookupsPage';
import { useLookupTab } from '../useLookupTab';
import { Notice } from './shared';
import { actionColumn, nameColumns, type LookupCaps } from './columns';

/**
 * The country list. Creating and editing happen on their own pages rather than in a dialog — the
 * form carries codes, a name in two languages and the country's whole currency set, which is more
 * than a popup should hold.
 */
export function CountriesTab({ caps }: { caps: LookupCaps }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const location = useLocation();
  const tab = useLookupTab<CountryDto, never>('countries');
  const list = useCountries(tab.params);

  // The form page saves and navigates back, handing its confirmation over in router state.
  useEffect(() => {
    const notice = (location.state as { notice?: string } | null)?.notice;
    if (!notice) return;

    tab.setNotice(notice);
    navigate(location.pathname, { replace: true, state: null });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [location.state]);

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
        toolbar={
          caps.canCreate ? (
            <NewButton
              label={t('lookups.createTitle', { entity: t('lookups.countries') })}
              onClick={() => navigate('/lookups/countries/new')}
            />
          ) : null
        }
        columns={[
          {
            key: 'code',
            header: t('lookups.code'),
            getValue: (row) => row.code,
            render: (row) => (
              <span className="font-mono" dir="ltr">
                {row.code}
              </span>
            ),
          },
          {
            key: 'phoneCode',
            header: t('lookups.phoneCode'),
            getValue: (row) => row.phoneCode,
            render: (row) =>
              row.phoneCode ? (
                <span className="font-mono" dir="ltr">
                  {row.phoneCode}
                </span>
              ) : (
                <span className="text-subtle">—</span>
              ),
          },
          ...nameColumns<CountryDto>(t),
          {
            key: 'currencies',
            header: t('lookups.currencies'),
            getValue: (row) => row.currencyCodes.join(' '),
            render: (row) =>
              row.currencyCodes.length === 0 ? (
                <span className="text-subtle">—</span>
              ) : (
                <div className="flex flex-wrap gap-1" dir="ltr">
                  {row.currencyCodes.map((code) => (
                    <span
                      key={code}
                      className="rounded bg-muted px-1.5 py-0.5 font-mono text-xs text-muted-foreground"
                    >
                      {code}
                    </span>
                  ))}
                </div>
              ),
          },
          actionColumn<CountryDto>(
            t,
            caps,
            // The row travels with the navigation, so the form opens without a second fetch.
            (row) => navigate(`/lookups/countries/${row.id}/edit`, { state: { country: row } }),
            tab.setToDelete,
            (row) => row.code,
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
