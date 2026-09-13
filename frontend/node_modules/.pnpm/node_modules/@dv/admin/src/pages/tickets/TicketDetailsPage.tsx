import {
  AdminPageHeader,
  AdminPanel,
  Alert,
  Button,
  Field,
  LoadingState,
  SearchableSelect,
  Select,
  cn,
} from '@dv/ui';
import {
  ArrowLeft,
  ClipboardList,
  ExternalLink,
  FileX2,
  MessagesSquare,
  Users,
} from 'lucide-react';
import { useState, type ComponentType } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { adminSession, Permissions } from '@/features/auth/session';
import type {
  TicketStatus} from '@/features/tickets/api';
import {
  ticketStatusKey,
  TICKET_STATUSES,
  useAssignTicket,
  useChangeTicketStatus,
  useLinkTicket,
  useTicket,
  useTicketAssignees,
  useTicketOrderApplications,
  useTicketOrderSearch,
  type TicketDetailsDto,
} from '@/features/tickets/api';
import { formatDateTime } from '@/shared/lib/format';
import { StatusPill } from '@/shared/ui/StatusPill';
import { TicketAttachment } from './TicketAttachment';
import { useApiErrorMessage } from '@/shared/lib/useApiError';

const NONE = '';

export function TicketDetailsPage() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const { id = '' } = useParams();
  const locale = i18n.resolvedLanguage ?? 'en';
  const toMessage = useApiErrorMessage();

  const ticket = useTicket(id);

  if (ticket.isPending) {
    return <LoadingState label={t('common.loading')} />;
  }

  if (ticket.isError || !ticket.data) {
    return <Alert variant="error">{toMessage(ticket.error)}</Alert>;
  }

  const data = ticket.data;

  return (
    <div className="animate-fade-in space-y-6">
      <Button variant="ghost" size="sm" onClick={() => navigate('/tickets')} data-testid="back">
        <ArrowLeft className="size-4" aria-hidden="true" />
        {t('tickets.backToQueue')}
      </Button>

      <AdminPageHeader
        title={data.subject}
        subtitle={`${data.ticketNumber} · ${data.categoryName}`}
      />

      <div className="grid gap-6 lg:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]">
        <div className="space-y-6">
          <Enquiry ticket={data} locale={locale} />
          <ConversationCard ticket={data} locale={locale} />
        </div>

        <div className="space-y-6">
          <StatusCard ticket={data} />
          <AssignmentCard ticket={data} />
          <LinkCard ticket={data} />
        </div>
      </div>
    </div>
  );
}

/**
 * A door to the conversation rather than the conversation itself.
 *
 * The thread and the composer moved to their own page: this one is for the facts about a ticket —
 * who sent it, what they said, who is carrying it, what it is linked to — and a thread of replies
 * stacked underneath pushed all of that off the screen the moment a ticket had any history.
 */
function ConversationCard({ ticket, locale }: { ticket: TicketDetailsDto; locale: string }) {
  const { t } = useTranslation();
  const navigate = useNavigate();

  const fromSupport = ticket.actions.filter((action) => !action.isFromApplicant);
  const unsent = fromSupport.filter((action) => action.canNotify);
  const last = ticket.actions.at(-1);

  return (
    <AdminPanel title={t('tickets.thread')} subtitle={t('tickets.threadSubtitle')}>
      <div className="space-y-4">
        <dl className="grid grid-cols-3 gap-3 text-center">
          <Stat label={t('tickets.statMessages')} value={ticket.actions.length} />
          <Stat label={t('tickets.statReplies')} value={fromSupport.length} />
          {/* The one number worth acting on: an answer written and never sent. */}
          <Stat label={t('tickets.statUnsent')} value={unsent.length} warn={unsent.length > 0} />
        </dl>

        {last ? (
          <div className="rounded-xl bg-ink-50 p-3">
            <p className="flex flex-wrap items-center gap-2 text-xs">
              {/* Which kind it was, not just when. A preview of an internal note that looks like
                  a preview of a reply is the one thing this card must not do. */}
              <span
                className={cn(
                  'font-semibold',
                  last.isFromApplicant
                    ? 'text-ink-700'
                    : last.isInternal
                      ? 'text-warning-foreground'
                      : 'text-brand-700',
                )}
              >
                {t(
                  last.isFromApplicant
                    ? 'tickets.fromApplicant'
                    : last.isInternal
                      ? 'tickets.internalNote'
                      : 'tickets.publicReply',
                )}
              </span>
              <span className="text-subtle">
                {t('tickets.lastActivity', { when: formatDateTime(last.createdAtUtc, locale) })}
              </span>
            </p>
            {last.body && <p className="mt-1 line-clamp-2 text-sm">{last.body}</p>}
          </div>
        ) : (
          <p className="rounded-xl bg-ink-50 px-4 py-6 text-center text-sm text-ink-500">
            {t('tickets.noActions')}
          </p>
        )}

        <Button
          className="w-full"
          onClick={() => navigate(`/tickets/${ticket.id}/conversation`)}
          data-testid="open-conversation"
        >
          <MessagesSquare className="size-4" aria-hidden="true" />
          {t('tickets.openConversation')}
        </Button>
      </div>
    </AdminPanel>
  );
}

function Stat({ label, value, warn }: { label: string; value: number; warn?: boolean }) {
  return (
    <div className={cn('rounded-xl border p-3', warn ? 'border-warning/50 bg-warning/10' : 'border-border')}>
      <dd className={cn('text-xl font-semibold', warn && 'text-warning-foreground')}>{value}</dd>
      <dt className="mt-0.5 text-[11px] leading-tight text-subtle">{label}</dt>
    </div>
  );
}

/** What the sender actually wrote, with whatever they attached to it. */
function Enquiry({ ticket, locale }: { ticket: TicketDetailsDto; locale: string }) {
  const { t } = useTranslation();

  return (
    <AdminPanel title={t('tickets.enquiry')} subtitle={formatDateTime(ticket.createdAtUtc, locale)}>
      <div className="space-y-5">
        <dl className="grid gap-x-6 gap-y-3 sm:grid-cols-2">
          <Detail label={t('tickets.name')} value={ticket.name} />
          <Detail label={t('tickets.email')} value={ticket.email} ltr />
          <Detail label={t('tickets.phone')} value={ticket.fullPhone ?? '—'} ltr />
          <Detail label={t('tickets.language')} value={ticket.languageCode.toUpperCase()} ltr />
        </dl>

        {/* whitespace-pre-wrap: the sender's own paragraphs are part of what they said. */}
        <p className="whitespace-pre-wrap rounded-xl bg-ink-50 p-4 text-sm leading-7">
          {ticket.description}
        </p>

        {ticket.files.length > 0 && (
          <div className="space-y-2">
            <p className="text-xs font-medium text-subtle">{t('tickets.attachments')}</p>
            <ul className="flex flex-wrap items-start gap-2">
              {ticket.files.map((file) => (
                <TicketAttachment key={file.id} ticketId={ticket.id} file={file} />
              ))}
            </ul>
          </div>
        )}
      </div>
    </AdminPanel>
  );
}

function Detail({ label, value, ltr }: { label: string; value: string; ltr?: boolean }) {
  return (
    <div>
      <dt className="text-xs text-subtle">{label}</dt>
      <dd className="text-sm font-medium" dir={ltr ? 'ltr' : undefined}>
        {value}
      </dd>
    </div>
  );
}

/** Everything support has recorded, oldest first, with internal notes clearly set apart. */
function StatusCard({ ticket }: { ticket: TicketDetailsDto }) {
  const { t } = useTranslation();
  const change = useChangeTicketStatus(ticket.id);
  const toMessage = useApiErrorMessage();

  const canUpdate = adminSession.has(Permissions.TicketsUpdate);

  return (
    <AdminPanel title={t('tickets.statusColumn')}>
      <div className="space-y-3">
        <StatusPill
          statusName={ticket.statusName}
          label={t(`tickets.status.${ticketStatusKey(ticket.status)}`)}
        />

        {change.isError && <Alert variant="error">{toMessage(change.error)}</Alert>}

        <Select
          aria-label={t('tickets.statusColumn')}
          disabled={!canUpdate || change.isPending}
          value={String(ticket.status)}
          onChange={(event) => change.mutate(Number(event.target.value) as TicketStatus)}
          data-testid="ticket-status"
        >
          {TICKET_STATUSES.map((status) => (
            <option key={status} value={status}>
              {t(`tickets.status.${ticketStatusKey(status)}`)}
            </option>
          ))}
        </Select>
      </div>
    </AdminPanel>
  );
}

function AssignmentCard({ ticket }: { ticket: TicketDetailsDto }) {
  const { t } = useTranslation();
  const assignees = useTicketAssignees();
  const assign = useAssignTicket(ticket.id);
  const toMessage = useApiErrorMessage();

  const canUpdate = adminSession.has(Permissions.TicketsUpdate);

  return (
    <AdminPanel title={t('tickets.assignee')} subtitle={t('tickets.assigneeSubtitle')}>
      <div className="space-y-3">
        {assign.isError && <Alert variant="error">{toMessage(assign.error)}</Alert>}

        <Select
          aria-label={t('tickets.assignee')}
          disabled={!canUpdate || assign.isPending}
          value={ticket.assignedToAdminUserId ?? NONE}
          onChange={(event) => assign.mutate(event.target.value || null)}
          data-testid="ticket-assignee"
        >
          <option value={NONE}>{t('tickets.unassigned')}</option>
          {(assignees.data ?? []).map((assignee) => (
            <option key={assignee.id} value={assignee.id}>
              {assignee.fullName} ({assignee.openTickets})
            </option>
          ))}
        </Select>

        <p className="text-xs text-subtle">{t('tickets.assigneeHint')}</p>
      </div>
    </AdminPanel>
  );
}

/**
 * What the enquiry turned out to be about: an order, and then one of that order's applications.
 *
 * Both sides are shown as well as chosen. A picker alone said which record was linked only by
 * echoing a reference into a closed dropdown, so the obvious next move — go and look at that
 * order — meant copying the number and finding it by hand. Each link now opens.
 *
 * The application follows the order rather than standing alone: an application picked from
 * everything the platform holds would nearly always be the wrong one.
 */
function LinkCard({ ticket }: { ticket: TicketDetailsDto }) {
  const { t } = useTranslation();
  const link = useLinkTicket(ticket.id);
  const toMessage = useApiErrorMessage();

  const canUpdate = adminSession.has(Permissions.TicketsUpdate);

  const [search, setSearch] = useState('');
  const orders = useTicketOrderSearch(search);
  const applications = useTicketOrderApplications(ticket.orderId);

  // The chosen order stays in the list even when the current search would not return it, so the
  // picker never shows an empty selection for a ticket that is already linked.
  const orderOptions = [
    ...(ticket.orderId && ticket.orderNumber
      ? [{ value: ticket.orderId, label: ticket.orderNumber, triggerLabel: ticket.orderNumber }]
      : []),
    ...(orders.data ?? [])
      .filter((order) => order.id !== ticket.orderId)
      .map((order) => ({
        value: order.id,
        label: `${order.orderNumber} — ${order.email}`,
        // The trigger only has room for the reference; the row can afford to say who it belongs to.
        triggerLabel: order.orderNumber,
        // The server matches on email too, so it has to survive the component's own filtering.
        keywords: order.email,
      })),
  ];

  const linkedOrder = (orders.data ?? []).find((order) => order.id === ticket.orderId);
  const linkedApplication = (applications.data ?? []).find(
    (application) => application.id === ticket.applicationId,
  );

  const hasApplications = (applications.data ?? []).length > 0;

  return (
    <AdminPanel title={t('tickets.linkTitle')} subtitle={t('tickets.linkSubtitle')}>
      <div className="space-y-5">
        {link.isError && <Alert variant="error">{toMessage(link.error)}</Alert>}

        <section className="space-y-2">
          <Field label={t('tickets.linkedOrder')} htmlFor="ticket-order">
            <SearchableSelect
              id="ticket-order"
              options={orderOptions}
              value={ticket.orderId ?? NONE}
              disabled={!canUpdate || link.isPending}
              placeholder={t('tickets.noOrder')}
              searchPlaceholder={t('tickets.searchOrders')}
              emptyMessage={t('tickets.noMatches')}
              clearable
              onSearchChange={setSearch}
              onChange={(orderId) =>
                // Changing the order drops the application with it: an application from the
                // previous order would now point at the wrong case entirely.
                link.mutate({ orderId: orderId || null, applicationId: null })
              }
            />
          </Field>

          {ticket.orderId && ticket.orderNumber && (
            <LinkedRecord
              to={`/orders/${ticket.orderId}`}
              icon={Users}
              reference={ticket.orderNumber}
              detail={linkedOrder?.email ?? ticket.email}
              testId="open-linked-order"
            />
          )}
        </section>

        {/* Always present, rather than appearing once an order is chosen: a field that materialises
            out of nowhere hides the fact that the two are linked at all. */}
        <section className="space-y-2">
          <Field
            label={t('tickets.linkedApplication')}
            htmlFor="ticket-application"
            hint={ticket.orderId ? undefined : t('tickets.pickOrderFirst')}
          >
            <Select
              id="ticket-application"
              disabled={
                !canUpdate ||
                !ticket.orderId ||
                link.isPending ||
                applications.isPending ||
                !hasApplications
              }
              value={ticket.applicationId ?? NONE}
              onChange={(event) =>
                link.mutate({
                  orderId: ticket.orderId,
                  applicationId: event.target.value || null,
                })
              }
              data-testid="ticket-application"
            >
              <option value={NONE}>{t('tickets.noApplication')}</option>
              {(applications.data ?? []).map((application) => (
                <option key={application.id} value={application.id}>
                  {application.applicationNumber}
                  {application.addressedTo ? ` — ${application.addressedTo}` : ''}
                </option>
              ))}
            </Select>
          </Field>

          {/* An order with nothing on it is a fact worth stating. Left unsaid, an empty dropdown
              reads as a page that failed to load. */}
          {ticket.orderId && !applications.isPending && !hasApplications && (
            <p className="flex items-center gap-2 rounded-lg bg-ink-50 px-3 py-2 text-xs text-ink-500">
              <FileX2 className="size-3.5 shrink-0" aria-hidden="true" />
              {t('tickets.noApplicationsForOrder')}
            </p>
          )}

          {ticket.applicationId && ticket.applicationNumber && (
            <LinkedRecord
              to={`/applications/${ticket.applicationId}`}
              icon={ClipboardList}
              reference={ticket.applicationNumber}
              detail={linkedApplication?.addressedTo ?? linkedApplication?.statusName ?? undefined}
              testId="open-linked-application"
            />
          )}
        </section>
      </div>
    </AdminPanel>
  );
}

/** A linked record, as a row that opens it. */
function LinkedRecord({
  to,
  icon: Icon,
  reference,
  detail,
  testId,
}: {
  to: string;
  icon: ComponentType<{ className?: string }>;
  reference: string;
  detail?: string;
  testId: string;
}) {
  const { t } = useTranslation();

  return (
    <Link
      to={to}
      data-testid={testId}
      className="flex items-center gap-3 rounded-xl px-3 py-2 ring-1 ring-ink-100/80 transition-colors hover:bg-cream/60 hover:ring-primary/30"
    >
      <span className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-cream text-ink-500">
        <Icon className="size-4" />
      </span>

      <span className="min-w-0 flex-1">
        <span className="block truncate font-mono text-sm font-medium text-ink-950" dir="ltr">
          {reference}
        </span>
        {detail && <span className="block truncate text-xs text-ink-400">{detail}</span>}
      </span>

      <span className="shrink-0 text-xs font-medium text-primary">{t('common.open')}</span>
      <ExternalLink className="size-3.5 shrink-0 text-primary rtl:rotate-90" aria-hidden="true" />
    </Link>
  );
}
