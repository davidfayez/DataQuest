import { Alert, Button, LoadingState, Spinner, buttonVariants, cn } from '@dv/ui';
import { ArrowLeft, CheckCheck, FileText, Lock, Paperclip, Send } from 'lucide-react';
import { useEffect, useRef, useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router-dom';
import {
  TICKET_FILE_RULES,
  useMyTicket,
  useReplyToMyTicket,
  type MyTicketMessageDto,
} from '@/entities/ticket/api';
import { AttachmentCard } from '@/features/contact/AttachmentPicker';
import { TicketAttachment } from '@/features/tickets/TicketAttachment';
import { formatDateTime } from '@/shared/lib/format';
import { useApiErrorMessage } from '@/shared/lib/useApiError';
import { TicketStatusPill } from './MyTicketsPage';

/**
 * One enquiry, as a conversation.
 *
 * Both sides are here: the applicant's original message, everything support has replied, and
 * anything the applicant has added since. A thread only one side can write on is not a
 * conversation — it pushes somebody with one more detail into opening a second ticket, which
 * support then has to reconcile with the first.
 *
 * Support's internal notes never reach this page. They are filtered out in the query rather than
 * hidden here, so there is nothing on the wire to hide.
 */
export function MyTicketDetailsPage() {
  const { t, i18n } = useTranslation();
  const { lang = 'en', id = '' } = useParams<{ lang: string; id: string }>();
  const locale = i18n.resolvedLanguage ?? 'en';
  const toMessage = useApiErrorMessage();

  const ticket = useMyTicket(id);

  if (ticket.isPending) {
    return <LoadingState label={t('common.loading')} />;
  }

  if (ticket.isError || !ticket.data) {
    return (
      <div className="mx-auto max-w-3xl px-4 py-10 sm:px-6">
        <Alert variant="error">{toMessage(ticket.error)}</Alert>
      </div>
    );
  }

  const data = ticket.data;

  return (
    <div className="mx-auto max-w-3xl px-4 py-8 sm:px-6">
      <Link
        to={`/${lang}/tickets`}
        className={buttonVariants({ variant: 'ghost', size: 'sm' })}
        data-testid="back-to-tickets"
      >
        <ArrowLeft className="size-4" aria-hidden="true" />
        {t('myTickets.backToList')}
      </Link>

      <header className="mt-4 mb-6 rounded-2xl border border-border bg-card p-5">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <h1 className="text-xl font-semibold sm:text-2xl">{data.subject}</h1>
          <TicketStatusPill status={data.status} />
        </div>

        <dl className="mt-3 flex flex-wrap gap-x-6 gap-y-1 text-xs text-muted-foreground">
          <div className="flex gap-1.5">
            <dt>{t('myTickets.reference')}</dt>
            <dd className="font-mono font-medium text-foreground" dir="ltr">
              {data.ticketNumber}
            </dd>
          </div>
          <div className="flex gap-1.5">
            <dt>{t('myTickets.category')}</dt>
            <dd className="text-foreground">{data.categoryName}</dd>
          </div>
          <div className="flex gap-1.5">
            <dt>{t('myTickets.opened')}</dt>
            <dd className="text-foreground">{formatDateTime(data.createdAtUtc, locale)}</dd>
          </div>
        </dl>
      </header>

      {/* The enquiry itself is the first thing the applicant said, so it opens the thread as a
          message of theirs rather than as a separate panel above it. */}
      <ol className="space-y-4">
        <li>
          <Bubble
            fromSupport={false}
            at={data.createdAtUtc}
            locale={locale}
            body={data.description}
          >
            {data.files.length > 0 && (
              <ul className="mt-3 flex flex-wrap items-start gap-2">
                {data.files.map((file) => (
                  <TicketAttachment key={file.id} ticketId={data.id} file={file} />
                ))}
              </ul>
            )}
          </Bubble>
        </li>

        {data.messages.map((message) => (
          <li key={message.id}>
            <Message ticketId={data.id} message={message} locale={locale} />
          </li>
        ))}
      </ol>

      {data.canReply ? (
        <Composer ticketId={data.id} />
      ) : (
        <div className="mt-6 flex items-start gap-3 rounded-2xl border border-dashed border-border p-5 text-sm">
          <Lock className="mt-0.5 size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
          <div>
            <p className="font-medium">{t('myTickets.closedTitle')}</p>
            <p className="mt-1 text-muted-foreground">{t('myTickets.closedBody')}</p>
            <Link
              to={`/${lang}/contact`}
              className={cn(buttonVariants({ variant: 'outline', size: 'sm' }), 'mt-3')}
            >
              {t('myTickets.newTicket')}
            </Link>
          </div>
        </div>
      )}
    </div>
  );
}

/**
 * One side's message.
 *
 * Alignment uses a logical margin, so the applicant's own messages sit at the end of the reading
 * direction in both English and Arabic rather than always on the right.
 */
function Bubble({
  fromSupport,
  at,
  locale,
  body,
  emailed,
  children,
}: {
  fromSupport: boolean;
  at: string;
  locale: string;
  body: string | null;
  emailed?: boolean;
  children?: ReactNode;
}) {
  const { t } = useTranslation();

  return (
    <div
      className={cn(
        'max-w-[92%] rounded-2xl border p-4 sm:max-w-[85%]',
        fromSupport ? 'border-primary/25 bg-primary/[0.04]' : 'ms-auto border-border bg-muted/40',
      )}
      data-testid={fromSupport ? 'message-support' : 'message-mine'}
    >
      <p className="mb-2 flex flex-wrap items-center gap-2 text-xs">
        <span className={cn('font-semibold', fromSupport ? 'text-primary' : 'text-foreground')}>
          {t(fromSupport ? 'myTickets.supportReply' : 'myTickets.yourMessage')}
        </span>
        <span className="text-muted-foreground">{formatDateTime(at, locale)}</span>

        {/* Said out loud so somebody who never got an email knows the reply is not missing. */}
        {emailed && (
          <span className="inline-flex items-center gap-1 text-muted-foreground">
            <CheckCheck className="size-3.5" aria-hidden="true" />
            {t('myTickets.alsoEmailed')}
          </span>
        )}
      </p>

      {body && <p className="whitespace-pre-wrap text-sm leading-7">{body}</p>}

      {children}
    </div>
  );
}

function Message({
  ticketId,
  message,
  locale,
}: {
  ticketId: string;
  message: MyTicketMessageDto;
  locale: string;
}) {
  return (
    <Bubble
      fromSupport={message.fromSupport}
      at={message.createdAtUtc}
      locale={locale}
      body={message.body}
      emailed={Boolean(message.emailedAtUtc)}
    >
      {message.documents.map((document) => (
        <div key={document.id} className="mt-3 rounded-xl border border-border bg-background p-3">
          {/* An applicant's own attachments are titled with the ticket number by the server, which
              says nothing worth a heading — the files speak for themselves. */}
          {message.fromSupport && (
            <p className="flex items-center gap-2 text-sm font-semibold">
              <FileText className="size-4 text-muted-foreground" aria-hidden="true" />
              {document.title}
            </p>
          )}

          {document.description && (
            <p className="mt-1 text-xs leading-6 text-muted-foreground">{document.description}</p>
          )}

          <ul className={cn('flex flex-wrap items-start gap-2', message.fromSupport && 'mt-3')}>
            {document.files.map((file) => (
              <TicketAttachment key={file.id} ticketId={ticketId} file={file} />
            ))}
          </ul>
        </div>
      ))}
    </Bubble>
  );
}

/** Writing back. Accepts the same file types and limits the original enquiry did. */
function Composer({ ticketId }: { ticketId: string }) {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage ?? 'en';
  const toMessage = useApiErrorMessage();

  const reply = useReplyToMyTicket(ticketId);

  const [body, setBody] = useState('');
  const [files, setFiles] = useState<File[]>([]);
  const [rejected, setRejected] = useState<string | null>(null);
  const textarea = useRef<HTMLTextAreaElement>(null);

  // Grows with what is being written rather than making somebody scroll a three-line box.
  useEffect(() => {
    const element = textarea.current;
    if (!element) return;

    element.style.height = 'auto';
    element.style.height = `${Math.min(element.scrollHeight, 320)}px`;
  }, [body]);

  const canSend = (body.trim() !== '' || files.length > 0) && !reply.isPending;

  function add(picked: FileList | null) {
    if (!picked) return;

    const problems: string[] = [];
    const kept: File[] = [];

    for (const file of [...picked]) {
      const extension = file.name.split('.').pop()?.toLowerCase() ?? '';

      // Checked here only to save a failed upload — the server sniffs the real bytes, which is
      // the check that counts.
      if (!TICKET_FILE_RULES.extensions.includes(extension as never)) {
        problems.push(t('contact.fileType', { name: file.name }));
      } else if (file.size > TICKET_FILE_RULES.maxBytes) {
        problems.push(t('contact.fileTooBig', { name: file.name }));
      } else {
        kept.push(file);
      }
    }

    const room = TICKET_FILE_RULES.maxFiles - files.length;
    if (kept.length > room) {
      problems.push(t('contact.tooManyFiles', { max: TICKET_FILE_RULES.maxFiles }));
    }

    setRejected(problems.length > 0 ? problems.join(' ') : null);
    setFiles((current) => [...current, ...kept.slice(0, Math.max(room, 0))]);
  }

  function send() {
    reply.mutate(
      { body, files },
      {
        onSuccess: () => {
          setBody('');
          setFiles([]);
          setRejected(null);
        },
      },
    );
  }

  return (
    <div className="mt-6 rounded-2xl border border-border bg-card p-4">
      <p className="mb-3 text-sm font-medium">{t('myTickets.replyTitle')}</p>

      {reply.isError && (
        <Alert variant="error" className="mb-3">
          {toMessage(reply.error)}
        </Alert>
      )}
      {rejected && (
        <Alert variant="warning" className="mb-3">
          {rejected}
        </Alert>
      )}

      <textarea
        ref={textarea}
        rows={3}
        value={body}
        onChange={(event) => setBody(event.target.value)}
        placeholder={t('myTickets.replyPlaceholder')}
        className="w-full resize-none rounded-xl border border-border bg-background p-3 text-sm leading-6 focus-visible:border-ring focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
        data-testid="reply-body"
      />

      {/* Previewed before sending, the same way the contact form does it — an image is shown as
          a thumbnail so the wrong screenshot is caught here rather than in support's queue. */}
      {files.length > 0 && (
        <ul className="mt-3 grid gap-2 sm:grid-cols-2" data-testid="reply-files">
          {files.map((file, index) => (
            <AttachmentCard
              key={`${file.name}-${index}`}
              file={file}
              locale={locale}
              disabled={reply.isPending}
              onRemove={() => setFiles((current) => current.filter((_, i) => i !== index))}
            />
          ))}
        </ul>
      )}

      <div className="mt-3 flex flex-wrap items-center justify-between gap-3">
        <label
          className={cn(
            buttonVariants({ variant: 'outline', size: 'sm' }),
            'cursor-pointer',
            files.length >= TICKET_FILE_RULES.maxFiles && 'pointer-events-none opacity-50',
          )}
        >
          <Paperclip className="size-4" aria-hidden="true" />
          {t('myTickets.attach')}
          <input
            type="file"
            multiple
            className="sr-only"
            accept={TICKET_FILE_RULES.accept}
            disabled={files.length >= TICKET_FILE_RULES.maxFiles}
            onChange={(event) => {
              add(event.target.files);
              // Cleared so picking the same file again still fires a change event.
              event.target.value = '';
            }}
            data-testid="reply-files-input"
          />
        </label>

        <Button type="button" disabled={!canSend} onClick={send} data-testid="reply-send">
          {reply.isPending && <Spinner />}
          <Send className="size-4" aria-hidden="true" />
          {t('myTickets.send')}
        </Button>
      </div>
    </div>
  );
}
