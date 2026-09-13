import { Alert, LoadingState, buttonVariants, cn } from '@dv/ui';
import { LifeBuoy, MessageSquare, Plus } from 'lucide-react';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router-dom';
import {
  ticketStatusKey,
  useMyTickets,
  type MyTicketListItemDto,
  TicketStatus,
} from '@/entities/ticket/api';
import { formatDate } from '@/shared/lib/format';
import { useApiErrorMessage } from '@/shared/lib/useApiError';

/** The tint each status carries. Answered is the one worth spotting from across the list. */
const TONES: Record<TicketStatus, string> = {
  [TicketStatus.Pending]: 'bg-amber-50 text-amber-700 ring-amber-200',
  [TicketStatus.InProgress]: 'bg-sky-50 text-sky-700 ring-sky-200',
  [TicketStatus.Answered]: 'bg-emerald-50 text-emerald-700 ring-emerald-200',
  [TicketStatus.Closed]: 'bg-muted text-muted-foreground ring-border',
};

export function TicketStatusPill({ status }: { status: TicketStatus }) {
  const { t } = useTranslation();

  return (
    <span
      className={cn(
        'inline-flex items-center whitespace-nowrap rounded-full px-2.5 py-1 text-xs font-semibold ring-1',
        TONES[status],
      )}
    >
      {t(`myTickets.status.${ticketStatusKey(status)}`)}
    </span>
  );
}

/**
 * The applicant's own enquiries.
 *
 * Shows what support has actually sent them and nothing else — the internal side of a ticket is
 * filtered out on the server, so there is nothing here that could accidentally reveal it.
 */
export function MyTicketsPage() {
  const { t, i18n } = useTranslation();
  const { lang = 'en' } = useParams<{ lang: string }>();
  const locale = i18n.resolvedLanguage ?? 'en';
  const toMessage = useApiErrorMessage();

  const tickets = useMyTickets();

  if (tickets.isPending) {
    return <LoadingState label={t('common.loading')} />;
  }

  return (
    <div className="mx-auto max-w-4xl px-4 py-10 sm:px-6">
      <div className="mb-8 flex flex-wrap items-start justify-between gap-4">
        <div>
          <h1 className="text-2xl font-semibold">{t('myTickets.title')}</h1>
          <p className="mt-1 text-sm text-muted-foreground">{t('myTickets.subtitle')}</p>
        </div>

        <Link to={`/${lang}/contact`} className={buttonVariants()}>
          <Plus className="size-4" aria-hidden="true" />
          {t('myTickets.newTicket')}
        </Link>
      </div>

      {tickets.isError && <Alert variant="error">{toMessage(tickets.error)}</Alert>}

      {tickets.data?.length === 0 ? (
        <div className="rounded-2xl border border-dashed border-border py-16 text-center">
          <div className="mx-auto mb-4 flex size-12 items-center justify-center rounded-2xl bg-muted">
            <LifeBuoy className="size-6 text-muted-foreground" aria-hidden="true" />
          </div>
          <p className="font-medium">{t('myTickets.emptyTitle')}</p>
          <p className="mx-auto mt-1 max-w-sm text-sm text-muted-foreground">
            {t('myTickets.emptyBody')}
          </p>
          <Link to={`/${lang}/contact`} className={cn(buttonVariants({ variant: 'outline' }), 'mt-5')}>
            {t('myTickets.newTicket')}
          </Link>
        </div>
      ) : (
        <ul className="space-y-3" data-testid="ticket-list">
          {(tickets.data ?? []).map((ticket) => (
            <TicketRow key={ticket.id} ticket={ticket} lang={lang} locale={locale} />
          ))}
        </ul>
      )}
    </div>
  );
}

function TicketRow({
  ticket,
  lang,
  locale,
}: {
  ticket: MyTicketListItemDto;
  lang: string;
  locale: string;
}) {
  const { t } = useTranslation();

  return (
    <li>
      <Link
        to={`/${lang}/tickets/${ticket.id}`}
        className="block rounded-2xl border border-border bg-card p-5 transition-colors hover:border-primary/40 hover:bg-muted/30"
        data-testid={`ticket-${ticket.ticketNumber}`}
      >
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0 flex-1">
            <p className="truncate font-semibold">{ticket.subject}</p>
            <p className="mt-1 text-xs text-muted-foreground">
              <span className="font-mono" dir="ltr">
                {ticket.ticketNumber}
              </span>
              {' · '}
              {ticket.categoryName}
              {' · '}
              {formatDate(ticket.createdAtUtc, locale)}
            </p>
          </div>

          <TicketStatusPill status={ticket.status} />
        </div>

        {/* A reply waiting to be read is the only reason to open one of these, so it is the one
            thing the row says beyond its subject. */}
        <p className="mt-3 flex items-center gap-1.5 text-xs text-muted-foreground">
          <MessageSquare className="size-3.5" aria-hidden="true" />
          {ticket.replyCount === 0
            ? t('myTickets.noRepliesYet')
            : t('myTickets.replyCount', {
                count: ticket.replyCount,
                when: formatDate(ticket.lastReplyAtUtc!, locale),
              })}
        </p>
      </Link>
    </li>
  );
}
