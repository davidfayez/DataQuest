import { AdminPageHeader, Alert, Button } from '@dv/ui';
import { Plus } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useLocation, useNavigate } from 'react-router-dom';
import { useAdminSession } from '@/features/auth/useAdminSession';
import type { ListParams } from '@/features/lookups/api';
import {
  useDeletePaymentGatewayIntegration,
  usePaymentGatewayIntegrations,
  usePaymentGateways,
  type PaymentGatewayIntegrationDto,
} from '@/features/payments/integrations';
import { ConfirmDialog } from '@/shared/ui/CrudDialog';
import { DataTable } from '@/shared/ui/DataTable';
import { actionColumn, nameColumns, type LookupCaps } from '../lookups/tabs/columns';
import { Notice, ParentFilter } from '../lookups/tabs/shared';
import { ModeBadge } from './IntegrationBadges';

const FORM_PATH = '/payments/integrations';

/**
 * The Payment type integrations page: configured connections to payment gateways — PayPal,
 * Fawry, a SEPA account — which payment methods then choose from.
 *
 * Shares the payment-methods permissions, like the type catalogue and banks: none of them means
 * anything without the others.
 */
export function PaymentIntegrationsPage() {
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

  const [params, setParams] = useState<ListParams & { gatewayCode?: string }>({ page: 1 });
  const [toDelete, setToDelete] = useState<PaymentGatewayIntegrationDto | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const list = usePaymentGatewayIntegrations(params);
  const gateways = usePaymentGateways();
  const remove = useDeletePaymentGatewayIntegration();

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
        // Still chosen by a method: retired rather than removed.
        setNotice(outcome === 1 ? t('lookups.deactivated') : t('lookups.deleted'));
      },
    });
  }

  return (
    <div className="animate-fade-in space-y-6">
      <AdminPageHeader
        title={t('integrations.title')}
        subtitle={t('integrations.subtitle')}
      />

      {!caps.canCreate && !caps.canUpdate && !caps.canDelete && (
        <Alert variant="info">{t('roles.viewOnlyNotice')}</Alert>
      )}

      <Notice message={notice} onDismiss={() => setNotice(null)} />

      <DataTable
        data={list.data}
        isPending={list.isPending}
        rowKey={(row) => row.id}
        onSearch={(search) => setParams((p) => ({ ...p, search, page: 1 }))}
        onPageChange={(page) => setParams((p) => ({ ...p, page }))}
        onPageSizeChange={(pageSize) => setParams((p) => ({ ...p, pageSize, page: 1 }))}
        onSortChange={(sort) =>
          setParams((p) => ({ ...p, sortBy: sort?.key, sortDescending: sort?.descending, page: 1 }))
        }
        filters={
          <ParentFilter
            label={t('integrations.gateway')}
            value={params.gatewayCode}
            options={(gateways.data ?? []).map((gateway) => ({ id: gateway.code, name: gateway.name }))}
            onChange={(gatewayCode) => setParams((p) => ({ ...p, gatewayCode, page: 1 }))}
          />
        }
        toolbar={
          caps.canCreate ? (
            <Button onClick={() => navigate(`${FORM_PATH}/new`)} data-testid="integration-new">
              <Plus className="size-4" aria-hidden="true" />
              {t('integrations.new')}
            </Button>
          ) : null
        }
        columns={[
          ...nameColumns<PaymentGatewayIntegrationDto>(t),
          {
            key: 'gateway',
            header: t('integrations.gateway'),
            sortable: false,
            getValue: (row) => row.gatewayName,
            render: (row) => (
              <span className="inline-flex items-center gap-2">
                <span className="font-medium">{row.gatewayName}</span>
                <ModeBadge mode={row.mode} />
              </span>
            ),
          },
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
            key: 'methodCount',
            header: t('payments.methodsUsing'),
            align: 'center',
            sortable: false,
            getValue: (row) => row.methodCount,
            render: (row) => <span>{row.methodCount}</span>,
          },
          actionColumn<PaymentGatewayIntegrationDto>(
            t,
            caps,
            (row) => navigate(`${FORM_PATH}/${row.id}/edit`),
            setToDelete,
            (row) => row.nameEn,
          ),
        ]}
      />

      <ConfirmDialog
        open={toDelete !== null}
        title={t('integrations.deleteTitle')}
        body={t('integrations.deleteBody')}
        confirmLabel={t('common.delete')}
        onClose={() => setToDelete(null)}
        onConfirm={confirmDelete}
        isPending={remove.isPending}
        destructive
      />
    </div>
  );
}
