import { cn } from '@dv/ui';
import { useTranslation } from 'react-i18next';
import { PaymentIntegrationMode } from '@/features/payments/api';

/** Sandbox or live, at a glance: live is the one that moves real money. */
export function ModeBadge({ mode }: { mode: number }) {
  const { t } = useTranslation();
  const live = mode === PaymentIntegrationMode.Live;

  return (
    <span
      className={cn(
        'rounded px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide',
        live ? 'bg-red-50 text-red-700' : 'bg-amber-50 text-amber-700',
      )}
    >
      {live ? t('payments.modeLive') : t('payments.modeSandbox')}
    </span>
  );
}
