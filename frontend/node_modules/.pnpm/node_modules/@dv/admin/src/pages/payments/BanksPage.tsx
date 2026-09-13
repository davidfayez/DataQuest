import { AdminPageHeader, Alert, Button } from '@dv/ui';
import { Plus } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useLocation, useNavigate } from 'react-router-dom';
import { useAdminSession } from '@/features/auth/useAdminSession';
import { useCountries, type ListParams } from '@/features/lookups/api';
import { useBanks, useDeleteBank, type BankDto } from '@/features/payments/api';
import { ConfirmDialog } from '@/shared/ui/CrudDialog';
import { DataTable } from '@/shared/ui/DataTable';
import { actionColumn, nameColumns, type LookupCaps } from '../lookups/tabs/columns';
import { Notice, ParentFilter } from '../lookups/tabs/shared';

/**
 * The bank catalogue a bank-transfer method picks from, scoped by country.
 *
 * Seeded with a starting list so the feature works out of the box, but editable: a merger or a new
 * licence is a row here rather than a deployment, which is the whole reason it is a lookup and not
 * a hard-coded list.
 */
export function BanksPage() {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const location = useLocation();
  const session = useAdminSession();

  const has = (name: string) => session?.permissions.has(name) ?? false;
  const caps: LookupCaps = {
    canCreate: has('PaymentMethods.Create'),
    canUpdate: has('PaymentMethods.Update'),
    canDelete: has('PaymentMethods.Delete'),
  };

  const [params, setParams] = useState<ListParams>({ page: 1, pageSize: 50 });
  const [toDelete, setToDelete] = useState<BankDto | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const list = useBanks(params);
  const countries = useCountries({ page: 1, pageSize: 300, isActive: true });
  const remove = useDeleteBank();

  // The form page saves and navigates back, handing its confirmation over in router state.
  useEffect(() => {
    const handed = (location.state as { notice?: string } | null)?.notice;
    if (!handed) return;

    setNotice(handed);
    navigate(location.pathname, { replace: true, state: null });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [location.state]);

  function confirmDelete() {
    if (!toDelete) return;

    remove.mutate(toDelete.id, {
      onSuccess: (outcome) => {
        setToDelete(null);
        // The API deactivates rather than deletes a bank an account still names; say which.
        setNotice(outcome === 1 ? t('lookups.deactivated') : t('lookups.deleted'));
      },
    });
  }

  const canManageAny = caps.canCreate || caps.canUpdate || caps.canDelete;

  return (
    <div className="animate-fade-in space-y-6">
      <AdminPageHeader title={t('banks.title')} subtitle={t('banks.subtitle')} />

      {!canManageAny && <Alert variant="info">{t('roles.viewOnlyNotice')}</Alert>}

      <Notice message={notice} onDismiss={() => setNotice(null)} />

      <DataTable
        data={list.data}
        isPending={list.isPending}
        rowKey={(row) => row.id}
        onSearch={(search) => setParams((p) => ({ ...p, search, page: 1 }))}
        onPageChange={(page) => setParams((p) => ({ ...p, page }))}
        onPageSizeChange={(pageSize) => setParams((p) => ({ ...p, pageSize, page: 1 }))}
        // Back to the first page: the row that sorts first belongs on page one.
        onSortChange={(sort) =>
          setParams((p) => ({
            ...p,
            sortBy: sort?.key,
            sortDescending: sort?.descending,
            page: 1,
          }))
        }
        filters={
          <ParentFilter
            label={t('lookups.country')}
            value={params.countryId}
            options={(countries.data?.items ?? []).map((c) => ({ id: c.id, name: c.name }))}
            onChange={(countryId) => setParams((p) => ({ ...p, countryId, page: 1 }))}
          />
        }
        toolbar={
          caps.canCreate ? (
            <Button onClick={() => navigate('/banks/new')} data-testid="bank-new">
              <Plus className="size-4" aria-hidden="true" />
              {t('banks.newBank')}
            </Button>
          ) : null
        }
        columns={[
          ...nameColumns<BankDto>(t),
          {
            key: 'countryName',
            header: t('lookups.country'),
            getValue: (row) => row.countryName,
            render: (row) => row.countryName,
          },
          {
            key: 'swiftCode',
            header: t('banks.swift'),
            getValue: (row) => row.swiftCode ?? '',
            render: (row) =>
              row.swiftCode ? (
                <span className="font-mono text-sm" dir="ltr">
                  {row.swiftCode}
                </span>
              ) : (
                <span className="text-subtle">—</span>
              ),
          },
          actionColumn<BankDto>(
            t,
            caps,
            (row) => navigate(`/banks/${row.id}/edit`),
            setToDelete,
            (row) => row.nameEn,
          ),
        ]}
      />

      <ConfirmDialog
        open={toDelete !== null}
        title={t('banks.deleteTitle')}
        body={t('banks.deleteBody')}
        confirmLabel={t('common.delete')}
        onClose={() => setToDelete(null)}
        onConfirm={confirmDelete}
        isPending={remove.isPending}
        destructive
      />
    </div>
  );
}
