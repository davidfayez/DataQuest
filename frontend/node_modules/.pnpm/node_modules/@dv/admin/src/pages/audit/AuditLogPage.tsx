import { AdminPageHeader, Dialog, Field, Input, Select } from '@dv/ui';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { adminKeys, apiClient } from '@/shared/api/client';
import { auditActionLabel } from '@/shared/lib/auditActionLabel';
import { formatAuditData } from '@/shared/lib/auditData';
import { formatDateTime } from '@/shared/lib/format';
import { DataTable, type PagedResult, type SortParams } from '@/shared/ui/DataTable';
import { RowActions } from '@/shared/ui/RowActions';

/** A label/value row for the audit detail dialog. */
function DetailRow({ label, value, ltr }: { label: string; value: string; ltr?: boolean }) {
  return (
    <div className="flex items-baseline justify-between gap-4 px-3 py-2 text-sm">
      <dt className="shrink-0 text-muted-foreground">{label}</dt>
      <dd className="text-end font-medium" dir={ltr ? 'ltr' : undefined}>
        {value}
      </dd>
    </div>
  );
}

interface AuditEntry {
  id: string;
  action: string;
  entityType: string;
  entityId: string | null;
  actorType: number;
  actorTypeName: string;
  actorName: string | null;
  data: string | null;
  ipAddress: string | null;
  createdAtUtc: string;
}

const ENTITY_TYPES = ['Application', 'Order', 'Wallet', 'Country', 'Currency', 'Role', 'AdminUser'];
const ACTOR_TYPES = [
  { value: 0, label: 'System' },
  { value: 1, label: 'Applicant' },
  { value: 2, label: 'Admin' },
];

interface Filters extends SortParams {
  page: number;
  pageSize: number;
  search?: string;
  action?: string;
  entityType?: string;
  actorType?: number;
  fromUtc?: string;
  toUtc?: string;
}

export function AuditLogPage() {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage ?? 'en';
  const [filters, setFilters] = useState<Filters>({ page: 1, pageSize: 25 });
  const [detail, setDetail] = useState<AuditEntry | null>(null);

  const log = useQuery({
    queryKey: adminKeys.auditLog(filters),
    queryFn: () =>
      apiClient.get<PagedResult<AuditEntry>>('admin/audit-log', {
        query: {
          page: filters.page,
          pageSize: filters.pageSize,
          sortBy: filters.sortBy,
          sortDescending: filters.sortDescending,
          search: filters.search || undefined,
          action: filters.action || undefined,
          entityType: filters.entityType || undefined,
          actorType: filters.actorType,
          fromUtc: filters.fromUtc || undefined,
          toUtc: filters.toUtc || undefined,
        },
      }),
  });

  return (
    <div className="animate-fade-in space-y-6">
      <AdminPageHeader title={t('audit.title')} subtitle={t('audit.subtitle')} />

      <DataTable
        data={log.data}
        isPending={log.isPending}
        rowKey={(row) => row.id}
        onSearch={(search) => setFilters((f) => ({ ...f, search, page: 1 }))}
        onPageChange={(page) => setFilters((f) => ({ ...f, page }))}
        onPageSizeChange={(pageSize) => setFilters((f) => ({ ...f, pageSize, page: 1 }))}
        // Back to the first page: the row that sorts first belongs on page one.
        onSortChange={(sort) =>
          setFilters((f) => ({
            ...f,
            sortBy: sort?.key,
            sortDescending: sort?.descending,
            page: 1,
          }))
        }
        filters={
          <>
            <Field label={t('audit.filterAction')} htmlFor="filter-action" className="w-44">
              <Input
                id="filter-action"
                value={filters.action ?? ''}
                onChange={(event) =>
                  setFilters((f) => ({ ...f, action: event.target.value, page: 1 }))
                }
                data-testid="filter-action"
              />
            </Field>

            <Field label={t('audit.filterEntity')} htmlFor="filter-entity" className="w-44">
              <Select
                id="filter-entity"
                value={filters.entityType ?? ''}
                onChange={(event) =>
                  setFilters((f) => ({ ...f, entityType: event.target.value || undefined, page: 1 }))
                }
                data-testid="filter-entity"
              >
                <option value="">{t('common.all')}</option>
                {ENTITY_TYPES.map((entity) => (
                  <option key={entity} value={entity}>
                    {entity}
                  </option>
                ))}
              </Select>
            </Field>

            <Field label={t('audit.filterActor')} htmlFor="filter-actor" className="w-40">
              <Select
                id="filter-actor"
                value={filters.actorType ?? ''}
                onChange={(event) =>
                  setFilters((f) => ({
                    ...f,
                    actorType: event.target.value === '' ? undefined : Number(event.target.value),
                    page: 1,
                  }))
                }
              >
                <option value="">{t('common.all')}</option>
                {ACTOR_TYPES.map((actor) => (
                  <option key={actor.value} value={actor.value}>
                    {actor.label}
                  </option>
                ))}
              </Select>
            </Field>

            <Field label={t('audit.filterFrom')} htmlFor="filter-from" className="w-40">
              <Input
                id="filter-from"
                type="date"
                value={filters.fromUtc ?? ''}
                onChange={(event) => setFilters((f) => ({ ...f, fromUtc: event.target.value, page: 1 }))}
              />
            </Field>
          </>
        }
        columns={[
          {
            key: 'action',
            header: t('audit.action'),
            getValue: (row) => row.action,
            render: (row) => (
              <span className="font-medium">{auditActionLabel(row.action, t)}</span>
            ),
          },
          {
            key: 'entityType',
            header: t('audit.entityType'),
            getValue: (row) => row.entityType,
            render: (row) => row.entityType,
          },
          {
            key: 'actorName',
            header: t('audit.actor'),
            getValue: (row) => `${row.actorName ?? ''} ${row.actorTypeName}`,
            render: (row) => (
              <div>
                <p>{row.actorName ?? '—'}</p>
                <p className="text-xs text-muted-foreground">{row.actorTypeName}</p>
              </div>
            ),
          },
          {
            key: 'createdAtUtc',
            header: t('audit.date'),
            getValue: (row) => row.createdAtUtc,
            render: (row) => (
              <span className="whitespace-nowrap">{formatDateTime(row.createdAtUtc, locale)}</span>
            ),
          },
          {
            key: 'actions',
            header: '',
            align: 'end',
            sortable: false,
            filterable: false,
            render: (row) =>
              row.data ? (
                <RowActions onView={() => setDetail(row)} viewTestId={`view-audit-${row.id}`} />
              ) : null,
          },
        ]}
      />

      <Dialog
        open={detail !== null}
        onClose={() => setDetail(null)}
        title={detail ? auditActionLabel(detail.action, t) : ''}
        className="w-[min(38rem,calc(100vw-2rem))]"
      >
        {detail && (
          <div className="space-y-4">
            {/* Who, what and when — the context for the change. */}
            <dl className="divide-y divide-border rounded-lg border border-border">
              <DetailRow label={t('audit.entityType')} value={detail.entityType} />
              <DetailRow
                label={t('audit.actor')}
                value={`${detail.actorName ?? '—'} · ${detail.actorTypeName}`}
              />
              <DetailRow label={t('audit.date')} value={formatDateTime(detail.createdAtUtc, locale)} />
              {detail.ipAddress && (
                <DetailRow label={t('audit.ipAddress')} value={detail.ipAddress} ltr />
              )}
            </dl>

            {/* The change itself, one readable row per field. */}
            {(() => {
              const rows = formatAuditData(detail.data, t, locale);
              if (rows.length === 0) return null;
              return (
                <div>
                  <h3 className="mb-1.5 text-xs font-semibold uppercase tracking-[0.06em] text-muted-foreground">
                    {t('audit.details')}
                  </h3>
                  <dl className="divide-y divide-border rounded-lg border border-border">
                    {rows.map((row) => (
                      <DetailRow key={row.label} label={row.label} value={row.value} ltr={row.ltr} />
                    ))}
                  </dl>
                </div>
              );
            })()}
          </div>
        )}
      </Dialog>
    </div>
  );
}
