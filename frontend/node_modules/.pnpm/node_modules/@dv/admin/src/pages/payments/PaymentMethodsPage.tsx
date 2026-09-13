import { AdminPageHeader, Alert } from '@dv/ui';
import { useTranslation } from 'react-i18next';
import { useAdminSession } from '@/features/auth/useAdminSession';
import type { LookupCaps } from '../lookups/tabs/columns';
import { PaymentMethodsTable } from './PaymentMethodsTable';

/**
 * The channels applicants may add funds through.
 *
 * The payment types behind them live on their own page: the two are read at different moments —
 * this list is the day-to-day one, the catalogue is set up once and rarely revisited — so they get
 * separate sidebar entries rather than sharing a screen.
 */
export function PaymentMethodsPage() {
  const { t } = useTranslation();
  const session = useAdminSession();

  const has = (name: string) => session?.permissions.has(name) ?? false;
  const caps: LookupCaps = {
    canCreate: has('PaymentMethods.Create'),
    canUpdate: has('PaymentMethods.Update'),
    canDelete: has('PaymentMethods.Delete'),
  };

  const canManageAny = caps.canCreate || caps.canUpdate || caps.canDelete;

  return (
    <div className="animate-fade-in space-y-6">
      <AdminPageHeader title={t('payments.title')} subtitle={t('payments.subtitle')} />

      {!canManageAny && <Alert variant="info">{t('roles.viewOnlyNotice')}</Alert>}

      <PaymentMethodsTable caps={caps} />
    </div>
  );
}
