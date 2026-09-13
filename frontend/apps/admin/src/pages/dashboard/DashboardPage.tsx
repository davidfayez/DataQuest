import {
  AdminPageHeader,
  AdminPanel,
  AdminStatCard,
  Alert,
  LoadingState,
  cn,
} from '@dv/ui';
import { Banknote, ClipboardList, PackageSearch, Wallet } from 'lucide-react';
import { useQuery } from '@tanstack/react-query';
import { useTranslation } from 'react-i18next';
import { adminKeys, apiClient } from '@/shared/api/client';
import { auditActionLabel } from '@/shared/lib/auditActionLabel';
import { formatDateTime, formatNumber } from '@/shared/lib/format';
import { useApiErrorMessage } from '@/shared/lib/useApiError';
import { StatusPill } from '@/shared/ui/StatusPill';

interface StatusCount {
  status: number;
  statusName: string;
  count: number;
}

interface RecentActivity {
  action: string;
  entityType: string;
  entityId: string | null;
  actorName: string | null;
  createdAtUtc: string;
}

interface DashboardDto {
  statusCounts: StatusCount[];
  totalApplications: number;
  openQueue: number;
  orderCount: number;
  revenueCollected: number;
  revenueRefunded: number;
  revenueNet: number;
  walletFloat: number;
  recentActivity: RecentActivity[];
}

const STATUS_KEY: Record<string, string> = {
  Draft: 'draft',
  PendingPayment: 'pendingPayment',
  Pending: 'pending',
  InProgress: 'inProgress',
  MissedInfo: 'missedInfo',
  Success: 'success',
  Failed: 'failed',
  Refunded: 'refunded',
};

const STATUS_BAR: Record<string, string> = {
  Draft: 'bg-muted-foreground/50',
  PendingPayment: 'bg-warning',
  Pending: 'bg-info',
  InProgress: 'bg-primary',
  MissedInfo: 'bg-warning',
  Success: 'bg-success',
  Failed: 'bg-destructive',
  Refunded: 'bg-muted-foreground',
};

export function DashboardPage() {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage ?? 'en';
  const toMessage = useApiErrorMessage();

  const dashboard = useQuery({
    queryKey: adminKeys.dashboard,
    queryFn: () => apiClient.get<DashboardDto>('admin/dashboard'),
  });

  if (dashboard.isPending) return <LoadingState label={t('common.loading')} />;

  if (dashboard.isError || !dashboard.data) {
    return (
      <Alert variant="error" title={t('errors.genericTitle')}>
        {toMessage(dashboard.error)}
      </Alert>
    );
  }

  const data = dashboard.data;
  const total = data.totalApplications || 1;

  return (
    <div className="animate-in fade-in duration-300">
      <AdminPageHeader title={t('dashboard.title')} subtitle={t('dashboard.byStatus')} />

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        <AdminStatCard
          icon={Banknote}
          label={t('dashboard.revenueNet')}
          value={formatNumber(data.revenueNet, locale)}
          tint="success"
        />
        <AdminStatCard
          icon={PackageSearch}
          label={t('dashboard.orders')}
          value={formatNumber(data.orderCount, locale)}
          tint="info"
        />
        <AdminStatCard
          icon={ClipboardList}
          label={t('dashboard.totalApplications')}
          value={formatNumber(data.totalApplications, locale)}
          tint="primary"
        />
        <AdminStatCard
          icon={Wallet}
          label={t('dashboard.openQueue')}
          value={formatNumber(data.openQueue, locale)}
          hint={`${t('dashboard.walletFloat')}: ${formatNumber(data.walletFloat, locale)}`}
          tint="warning"
        />
      </div>

      <div className="mt-6 grid gap-6 xl:grid-cols-12">
        <AdminPanel title={t('dashboard.byStatus')} className="xl:col-span-5">
          {data.statusCounts.length === 0 ? (
            <p className="py-8 text-center text-sm text-muted-foreground">{t('dashboard.noActivity')}</p>
          ) : (
            <div className="space-y-4">
              <div className="flex h-3 overflow-hidden rounded-full bg-muted">
                {data.statusCounts.map((entry) => (
                  <div
                    key={entry.statusName}
                    className={cn('h-full', STATUS_BAR[entry.statusName] ?? 'bg-muted-foreground')}
                    style={{ width: `${(entry.count / total) * 100}%` }}
                    title={`${entry.statusName}: ${entry.count}`}
                  />
                ))}
              </div>
              <ul className="space-y-3">
                {data.statusCounts.map((entry) => (
                  <li key={entry.statusName} className="flex items-center gap-4" data-testid={`status-${entry.statusName}`}>
                    <StatusPill
                      statusName={entry.statusName}
                      label={t(`status.${STATUS_KEY[entry.statusName] ?? 'draft'}`)}
                    />
                    <div className="h-2 flex-1 overflow-hidden rounded-full bg-muted">
                      <div
                        className={cn('h-full rounded-full', STATUS_BAR[entry.statusName] ?? 'bg-muted-foreground')}
                        style={{ width: `${(entry.count / total) * 100}%` }}
                      />
                    </div>
                    <span className="w-8 text-end font-mono text-sm font-bold">{formatNumber(entry.count, locale)}</span>
                  </li>
                ))}
              </ul>
            </div>
          )}
        </AdminPanel>

        <AdminPanel title={t('dashboard.recentActivity')} className="xl:col-span-7" noPadding>
          {data.recentActivity.length === 0 ? (
            <p className="px-6 py-12 text-center text-sm text-muted-foreground">{t('dashboard.noActivity')}</p>
          ) : (
            <ul className="divide-y divide-border">
              {data.recentActivity.map((entry, index) => (
                <li key={`${entry.action}-${index}`} className="flex items-center gap-4 px-5 py-3.5">
                  <div className="min-w-0 flex-1">
                    <p className="text-sm font-medium text-foreground">{auditActionLabel(entry.action, t)}</p>
                    <p className="text-xs text-muted-foreground">
                      {entry.actorName ?? '—'} · {formatDateTime(entry.createdAtUtc, locale)}
                    </p>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </AdminPanel>
      </div>
    </div>
  );
}
