import { AdminPageHeader, Select, cn } from '@dv/ui';
import { Paperclip } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate } from 'react-router-dom';
import type {
  TicketStatus} from '@/features/tickets/api';
import {
  TICKET_STATUSES,
  ticketStatusKey,
  useTicketCategories,
  useTickets,
  type TicketListItemDto,
  type TicketListParams
} from '@/features/tickets/api';
import { DataTable } from '@/shared/ui/DataTable';
import { StatusPill } from '@/shared/ui/StatusPill';
import { formatDateTime } from '@/shared/lib/format';

/** Anything other than a real value reads as "no filter", so the querystring stays clean. */
const ALL = '';

/**
 * The support queue.
 *
 * Newest first, because a support desk is worked from the top and an enquiry that has sat since
 * yesterday is not more urgent than one that arrived unanswered this morning. The filter that
 * earns its place is "unassigned" — the queue's real question is not what is open but what nobody
 * has picked up.
 */
export function TicketsPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const locale = i18n.resolvedLanguage ?? 'en';

  const [params, setParams] = useState<TicketListParams>({ page: 1, pageSize: 25 });

  const list = useTickets(params);
  const categories = useTicketCategories({ page: 1, pageSize: 100, isActive: true });

  function update(patch: Partial<TicketListParams>) {
    setParams((current) => ({ ...current, ...patch, page: 1 }));
  }

  return (
    <div className="animate-fade-in space-y-6">
      <AdminPageHeader title={t('tickets.title')} subtitle={t('tickets.subtitle')} />

      <DataTable
        data={list.data}
        isPending={list.isPending}
        rowKey={(row) => row.id}
        emptyMessage={t('tickets.empty')}
        onSearch={(search) => update({ search })}
        onPageChange={(page) => setParams((p) => ({ ...p, page }))}
        onPageSizeChange={(pageSize) => update({ pageSize })}
        // Back to the first page: the row that sorts first belongs on page one.
        onSortChange={(sort) =>
          update({ sortBy: sort?.key, sortDescending: sort?.descending })
        }
        filters={
          <div className="flex flex-wrap items-center gap-2">
            <Select
              aria-label={t('tickets.filterStatus')}
              className="w-44"
              value={params.status === undefined ? ALL : String(params.status)}
              onChange={(event) =>
                update({
                  status:
                    event.target.value === ALL
                      ? undefined
                      : (Number(event.target.value) as TicketStatus),
                })
              }
              data-testid="filter-status"
            >
              <option value={ALL}>{t('tickets.allStatuses')}</option>
              {TICKET_STATUSES.map((status) => (
                <option key={status} value={status}>
                  {t(`tickets.status.${ticketStatusKey(status)}`)}
                </option>
              ))}
            </Select>

            <Select
              aria-label={t('tickets.filterCategory')}
              className="w-52"
              value={params.ticketCategoryId ?? ALL}
              onChange={(event) =>
                update({ ticketCategoryId: event.target.value || undefined })
              }
              data-testid="filter-category"
            >
              <option value={ALL}>{t('tickets.allCategories')}</option>
              {(categories.data?.items ?? []).map((category) => (
                <option key={category.id} value={category.id}>
                  {category.name}
                </option>
              ))}
            </Select>

            {/* The queue's most useful question: what has nobody picked up? */}
            <label className="flex items-center gap-2 whitespace-nowrap text-sm">
              <input
                type="checkbox"
                className="size-4 rounded border-border"
                checked={params.unassigned === true}
                onChange={(event) =>
                  update({ unassigned: event.target.checked ? true : undefined })
                }
                data-testid="filter-unassigned"
              />
              {t('tickets.unassignedOnly')}
            </label>
          </div>
        }
        columns={[
          {
            key: 'ticketNumber',
            header: t('tickets.reference'),
            getValue: (row) => row.ticketNumber,
            render: (row) => (
              <button
                type="button"
                onClick={() => navigate(`/tickets/${row.id}`)}
                className="font-mono text-sm font-semibold text-brand-700 hover:underline"
                dir="ltr"
                data-testid={`open-${row.ticketNumber}`}
              >
                {row.ticketNumber}
              </button>
            ),
          },
          {
            key: 'subject',
            header: t('tickets.subject'),
            getValue: (row) => row.subject,
            render: (row) => (
              <div className="max-w-xs">
                <p className="truncate font-medium">{row.subject}</p>
                <p className="truncate text-xs text-subtle">{row.categoryName}</p>
              </div>
            ),
          },
          {
            key: 'name',
            header: t('tickets.from'),
            getValue: (row) => row.name,
            render: (row) => (
              <div className="max-w-[14rem]">
                <p className="truncate">{row.name}</p>
                <p className="truncate text-xs text-subtle" dir="ltr">
                  {row.email}
                </p>
              </div>
            ),
          },
          {
            key: 'status',
            header: t('tickets.statusColumn'),
            getValue: (row) => row.statusName,
            render: (row) => (
              <StatusPill
                statusName={row.statusName}
                label={t(`tickets.status.${ticketStatusKey(row.status)}`)}
              />
            ),
          },
          {
            key: 'assignedToName',
            header: t('tickets.assignee'),
            getValue: (row) => row.assignedToName ?? '',
            render: (row) =>
              row.assignedToName ? (
                <span>{row.assignedToName}</span>
              ) : (
                // Called out rather than left blank: an unassigned ticket is the one state on
                // this page that needs somebody to do something.
                <span className="rounded-full bg-amber-50 px-2 py-0.5 text-xs font-medium text-amber-700 ring-1 ring-amber-200">
                  {t('tickets.unassigned')}
                </span>
              ),
          },
          {
            key: 'linked',
            header: t('tickets.linked'),
            getValue: (row) => row.orderNumber ?? '',
            render: (row) =>
              row.orderNumber ? (
                <div className="text-xs" dir="ltr">
                  <p className="font-mono">{row.orderNumber}</p>
                  {row.applicationNumber && (
                    <p className="font-mono text-subtle">{row.applicationNumber}</p>
                  )}
                </div>
              ) : (
                <span className="text-subtle">—</span>
              ),
          },
          {
            key: 'files',
            header: '',
            align: 'center',
            sortable: false,
            filterable: false,
            render: (row) =>
              row.fileCount > 0 ? (
                <span
                  className="inline-flex items-center gap-1 text-xs text-subtle"
                  title={t('tickets.attachmentCount', { count: row.fileCount })}
                >
                  <Paperclip className="size-3.5" aria-hidden="true" />
                  {row.fileCount}
                </span>
              ) : null,
          },
          {
            key: 'createdAtUtc',
            header: t('tickets.received'),
            getValue: (row) => row.createdAtUtc,
            render: (row: TicketListItemDto) => (
              <div className="whitespace-nowrap text-xs">
                <p>{formatDateTime(row.createdAtUtc, locale)}</p>
                <p
                  className={cn(
                    'text-subtle',
                    // A ticket nobody has answered is the thing worth spotting in this column.
                    !row.lastRepliedAtUtc && 'text-amber-700',
                  )}
                >
                  {row.lastRepliedAtUtc
                    ? t('tickets.repliedAt', { when: formatDateTime(row.lastRepliedAtUtc, locale) })
                    : t('tickets.neverReplied')}
                </p>
              </div>
            ),
          },
        ]}
      />
    </div>
  );
}
