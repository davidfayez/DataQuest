import { Button, cn } from '@dv/ui';
import { AlertTriangle, ExternalLink, Plus, QrCode, Wallet } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useLocation, useNavigate } from 'react-router-dom';
import { useCountries } from '@/features/lookups/api';
import {
  useDeletePaymentMethod,
  usePaymentMethods,
  usePaymentMethodTypes,
  type AdminPaymentMethodDto,
  type PaymentMethodListParams,
} from '@/features/payments/api';
import { ConfirmDialog } from '@/shared/ui/CrudDialog';
import { DataTable } from '@/shared/ui/DataTable';
import { actionColumn, type LookupCaps } from '../lookups/tabs/columns';
import { Notice, ParentFilter } from '../lookups/tabs/shared';

/**
 * The channels applicants may add funds through. Create and edit are their own page: a method
 * spans two languages, four notes, a country and currency mapping and a list of receiving
 * accounts, which is far more than a dialog should hold.
 */
export function PaymentMethodsTable({ caps }: { caps: LookupCaps }) {
  const { t } = useTranslation();
  const navigate = useNavigate();
  const location = useLocation();

  const [params, setParams] = useState<PaymentMethodListParams>({ page: 1 });
  const [toDelete, setToDelete] = useState<AdminPaymentMethodDto | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const list = usePaymentMethods(params);
  const countries = useCountries({ page: 1, pageSize: 300, isActive: true });
  const types = usePaymentMethodTypes({ page: 1, pageSize: 100 });
  const remove = useDeletePaymentMethod();

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
        // The API deactivates rather than deletes anything requests already quote; say which.
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
        filters={
          <>
            <ParentFilter
              label={t('lookups.country')}
              value={params.countryId}
              options={(countries.data?.items ?? []).map((c) => ({ id: c.id, name: c.name }))}
              onChange={(countryId) => setParams((p) => ({ ...p, countryId, page: 1 }))}
            />
            <ParentFilter
              label={t('payments.type')}
              value={params.paymentMethodTypeId}
              options={(types.data?.items ?? []).map((type) => ({
                id: type.id,
                name: type.name,
              }))}
              onChange={(paymentMethodTypeId) =>
                setParams((p) => ({ ...p, paymentMethodTypeId, page: 1 }))
              }
            />
          </>
        }
        toolbar={
          caps.canCreate ? (
            <Button onClick={() => navigate('/payments/methods/new')} data-testid="payment-new">
              <Plus className="size-4" aria-hidden="true" />
              {t('payments.newMethod')}
            </Button>
          ) : null
        }
        columns={[
          {
            key: 'nameEn',
            header: t('lookups.nameEn'),
            getValue: (row) => row.nameEn,
            render: (row) => (
              <div className="flex items-center gap-2">
                <KindIcon method={row} />
                <span className="font-medium">{row.nameEn}</span>
              </div>
            ),
          },
          {
            key: 'nameAr',
            header: t('lookups.nameAr'),
            getValue: (row) => row.nameAr,
            render: (row) => <span dir="rtl">{row.nameAr}</span>,
          },
          {
            key: 'type',
            header: t('payments.type'),
            getValue: (row) => row.typeName,
            render: (row) => (
              <div>
                <div>{row.typeName}</div>
                <div className="text-xs text-muted-foreground">
                  {t(`payments.kind.${row.kindName}`)}
                </div>
              </div>
            ),
          },
          {
            key: 'reach',
            header: t('payments.reach'),
            sortable: false,
            getValue: (row) => String(row.countryIds.length),
            render: (row) => {
              // Either count at zero means no order can ever match this method, so the summary is
              // called out rather than left as one grey number among others.
              const unreachable = row.countryIds.length === 0 || row.currencyIds.length === 0;

              return (
                <span
                  className={cn('text-sm', unreachable ? 'text-warning' : 'text-muted-foreground')}
                >
                  {t('payments.reachSummary', {
                    countries: row.countryIds.length,
                    currencies: row.currencyIds.length,
                  })}
                </span>
              );
            },
          },
          {
            key: 'accounts',
            header: t('payments.accounts'),
            align: 'center',
            getValue: (row) => String(row.accounts.length),
            render: (row) =>
              row.requiresAccountNumber ? (
                <span>{row.accounts.filter((account) => account.isActive).length}</span>
              ) : (
                <span className="text-subtle">—</span>
              ),
          },
          {
            key: 'isActive',
            header: t('lookups.isActive'),
            align: 'center',
            getValue: (row) => (row.isActive ? '1' : '0'),
            render: (row) => {
              // Active but incomplete is the state worth shouting about: the admin believes the
              // method is live while applicants cannot see it at all.
              if (row.isActive && !row.isUsable) {
                return (
                  <span
                    className="inline-flex items-center gap-1 text-warning"
                    title={t('payments.incompleteHint')}
                  >
                    <AlertTriangle className="size-3.5" aria-hidden="true" />
                    {t('payments.incomplete')}
                  </span>
                );
              }

              // Complete, active, and still invisible: the picker matches on the order's country
              // and wallet currency, so a method linked to neither is offered to nobody. This read
              // as a green "Active" before, which is how a catalogue of configured providers
              // arrives at the applicant site as one.
              if (row.isActive && (row.countryIds.length === 0 || row.currencyIds.length === 0)) {
                return (
                  <span
                    className="inline-flex items-center gap-1 text-warning"
                    title={t('payments.unreachableHint')}
                  >
                    <AlertTriangle className="size-3.5" aria-hidden="true" />
                    {t('payments.unreachable')}
                  </span>
                );
              }

              return (
                <span className={row.isActive ? 'text-success' : 'text-muted-foreground'}>
                  {row.isActive ? t('common.active') : t('common.inactive')}
                </span>
              );
            },
          },
          actionColumn<AdminPaymentMethodDto>(
            t,
            caps,
            (row) => navigate(`/payments/methods/${row.id}/edit`),
            setToDelete,
            (row) => row.nameEn,
          ),
        ]}
      />

      <ConfirmDialog
        open={toDelete !== null}
        title={t('payments.deleteTitle')}
        body={t('payments.deleteBody')}
        confirmLabel={t('common.delete')}
        onClose={() => setToDelete(null)}
        onConfirm={confirmDelete}
        isPending={remove.isPending}
        destructive
      />
    </div>
  );
}

/**
 * What paying this method looks like, at a glance.
 *
 * Drawn from what the method actually asks for rather than from its kind: after the kinds were
 * collapsed to two, an icon keyed on them would say "transfer" for a QR code, a bank and a link
 * alike.
 */
function KindIcon({ method }: { method: AdminPaymentMethodDto }) {
  const Icon = method.requiresBarcode
    ? QrCode
    : method.requiresAccountNumber
      ? Wallet
      : ExternalLink;

  return <Icon className="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />;
}
