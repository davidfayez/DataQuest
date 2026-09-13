import { AdminPageHeader, Alert } from '@dv/ui';
import { useTranslation } from 'react-i18next';
import { useAdminSession } from '@/features/auth/useAdminSession';
import type { LookupCaps } from '../lookups/tabs/columns';
import { PaymentTypesTable } from './PaymentTypesTable';

/**
 * The payment-type catalogue — the lookup that decides what configuring a payment method involves
 * and what the applicant is asked for.
 *
 * Shares the payment-methods permission set: a role that could add a method but not the type it
 * needs could only ever produce a channel nobody can pay through.
 */
export function PaymentTypesPage() {
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
      <AdminPageHeader title={t('payments.typesTitle')} subtitle={t('payments.typesSubtitle')} />

      {!canManageAny && <Alert variant="info">{t('roles.viewOnlyNotice')}</Alert>}

      <PaymentTypesTable caps={caps} />
    </div>
  );
}
