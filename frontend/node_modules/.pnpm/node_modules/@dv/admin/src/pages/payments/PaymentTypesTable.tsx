import { Button } from '@dv/ui';
import { Plus } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useLocation, useNavigate } from 'react-router-dom';
import type { ListParams } from '@/features/lookups/api';
import {
  useDeletePaymentMethodType,
  usePaymentMethodTypes,
  type PaymentMethodTypeDto,
} from '@/features/payments/api';
import { ConfirmDialog } from '@/shared/ui/CrudDialog';
import { DataTable } from '@/shared/ui/DataTable';
import { actionColumn, nameColumns, type LookupCaps } from '../lookups/tabs/columns';
import { Notice } from '../lookups/tabs/shared';

/**
 * The payment-type table — the lookup that turns "we also accept X" into a configurable channel
 * without a code change.
 *
 * Create and edit are their own page rather than a dialog: each of a type's four switches reshapes
 * every method of that type in both realms, which needs room to explain rather than a popup.
 */
export function PaymentTypesTable({ caps }: { caps: LookupCaps }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const location = useLocation();

  const [params, setParams] = useState<ListParams>({ page: 1 });
  const [toDelete, setToDelete] = useState<PaymentMethodTypeDto | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const list = usePaymentMethodTypes(params);
  const remove = useDeletePaymentMethodType();

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
        // The API deactivates rather than deletes a type any method still uses; say which.
        setNotice(outcome === 1 ? t('lookups.deactivated') : t('lookups.deleted'));
      },
    });
  }

  return (
    <div className="space-y-4">
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
        toolbar={
          caps.canCreate ? (
            <Button onClick={() => navigate('/payments/types/new')} data-testid="payment-type-new">
              <Plus className="size-4" aria-hidden="true" />
              {t('payments.newType')}
            </Button>
          ) : null
        }
        columns={[
          ...nameColumns<PaymentMethodTypeDto>(t),
          {
            key: 'description',
            header: t('payments.description'),
            sortable: false,
            getValue: (row) => row.description ?? '',
            render: (row) =>
              row.description ? (
                <span className="line-clamp-2 max-w-md text-sm text-ink-700">{row.description}</span>
              ) : (
                <span className="text-subtle">—</span>
              ),
          },
          {
            key: 'requires',
            header: t('payments.requires'),
            sortable: false,
            getValue: (row) => row.kindName,
            render: (row) => {
              const flags = [
                row.requiresAccountNumber && t('payments.flag.accounts'),
                row.requiresBarcode && t('payments.flag.barcode'),
                row.requiresProofDocument && t('payments.flag.proof'),
                row.requiresReferenceNumber && t('payments.flag.reference'),
              ].filter(Boolean) as string[];

              if (flags.length === 0) return <span className="text-subtle">—</span>;

              return (
                <div className="flex flex-wrap gap-1">
                  {flags.map((flag) => (
                    <span
                      key={flag}
                      className="rounded bg-muted px-1.5 py-0.5 text-xs text-muted-foreground"
                    >
                      {flag}
                    </span>
                  ))}
                </div>
              );
            },
          },
          {
            key: 'methodCount',
            header: t('payments.methodsUsing'),
            align: 'center',
            getValue: (row) => row.methodCount,
            render: (row) => <span>{row.methodCount}</span>,
          },
          actionColumn<PaymentMethodTypeDto>(
            t,
            caps,
            (row) => navigate(`/payments/types/${row.id}/edit`),
            setToDelete,
            (row) => row.nameEn,
          ),
        ]}
      />

      <ConfirmDialog
        open={toDelete !== null}
        title={t('payments.deleteTypeTitle')}
        body={t('payments.deleteTypeBody')}
        confirmLabel={t('common.delete')}
        onClose={() => setToDelete(null)}
        onConfirm={confirmDelete}
        isPending={remove.isPending}
        destructive
      />
    </div>
  );
}
