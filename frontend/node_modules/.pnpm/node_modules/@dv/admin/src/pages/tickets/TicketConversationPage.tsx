import {
  AdminPageHeader,
  AdminPanel,
  Alert,
  Button,
  Field,
  Input,
  LoadingState,
  Select,
  cn,
} from '@dv/ui';
import { ArrowLeft, Lock, Mail, Plus, Send, Trash2, User } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { useNavigate, useParams } from 'react-router-dom';
import { adminSession, Permissions } from '@/features/auth/session';
import type {
  TicketStatus} from '@/features/tickets/api';
import {
  ticketStatusKey,
  TICKET_STATUSES,
  useCreateTicketAction,
  useNotifyTicketAction,
  useTicket,
  type TicketActionDto,
  type TicketDetailsDto,
  type TicketDocumentDraft,
} from '@/features/tickets/api';
import { formatDateTime } from '@/shared/lib/format';
import { useApiErrorMessage } from '@/shared/lib/useApiError';
import { PendingAttachment, TicketAttachment } from './TicketAttachment';

const NONE = '';

/**
 * The whole conversation on one ticket, and the box for adding to it.
 *
 * Its own page because it is its own job: reading a thread and writing the next message is what
 * support spend their time on, and sharing a screen with status, assignment and linking meant the
 * thread was squeezed into a column and the composer was below the fold.
 */
export function TicketConversationPage() {
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
      <Button
        variant="ghost"
        size="sm"
        onClick={() => navigate(`/tickets/${data.id}`)}
        data-testid="back-to-ticket"
      >
        <ArrowLeft className="size-4" aria-hidden="true" />
        {t('tickets.backToTicket')}
      </Button>

      <AdminPageHeader
        title={data.subject}
        subtitle={`${data.ticketNumber} · ${data.name} · ${data.email}`}
      />

      <div className="mx-auto max-w-4xl space-y-6">
        <Thread ticket={data} locale={locale} />
        <Composer ticket={data} />
      </div>
    </div>
  );
}

/**
 * The thread, as a conversation with sides.
 *
 * The sender's messages sit at the start of the reading direction and support's answers at the
 * end, so the shape of the exchange is readable before a word of it is. Internal notes run the
 * full width in their own colour — they belong to neither side of the conversation, and looking
 * like a reply is exactly what they must not do.
 */
function Thread({ ticket, locale }: { ticket: TicketDetailsDto; locale: string }) {
  const { t } = useTranslation();

  return (
    <section>
      <h2 className="mb-3 text-sm font-semibold text-subtle">{t('tickets.thread')}</h2>

      {/* The enquiry itself opens the thread — it is the first thing the sender said. */}
      <ol className="space-y-4">
        <li>
          <div className="max-w-[85%] rounded-2xl border border-ink-200 bg-ink-50 p-4">
            <p className="mb-2 flex flex-wrap items-center gap-2 text-xs">
              <span className="inline-flex items-center gap-1 font-semibold text-ink-700">
                <User className="size-3.5" aria-hidden="true" />
                {t('tickets.fromApplicant')}
              </span>
              <span className="text-subtle">{formatDateTime(ticket.createdAtUtc, locale)}</span>
            </p>

            <p className="whitespace-pre-wrap text-sm leading-7">{ticket.description}</p>

            {ticket.files.length > 0 && (
              <ul className="mt-3 flex flex-wrap items-start gap-2">
                {ticket.files.map((file) => (
                  <TicketAttachment key={file.id} ticketId={ticket.id} file={file} />
                ))}
              </ul>
            )}
          </div>
        </li>

        {ticket.actions.map((action) => (
          <li key={action.id}>
            <ActionRow ticket={ticket} action={action} locale={locale} />
          </li>
        ))}
      </ol>
    </section>
  );
}

function ActionRow({
  ticket,
  action,
  locale,
}: {
  ticket: TicketDetailsDto;
  action: TicketActionDto;
  locale: string;
}) {
  const { t } = useTranslation();
  const notify = useNotifyTicketAction(ticket.id);
  const toMessage = useApiErrorMessage();

  const canNotify = adminSession.has(Permissions.TicketsNotify);

  return (
    <div
      className={cn(
        'rounded-2xl border p-4',
        // Three states, distinguishable before reading a label: what the sender wrote, what they
        // were told, and what stayed with the team.
        action.isFromApplicant
          ? 'max-w-[85%] border-ink-200 bg-ink-50'
          : action.isInternal
            ? 'border-warning/40 bg-warning/10'
            : 'ms-auto max-w-[85%] border-brand-200 bg-brand-50/40',
      )}
      data-testid={`action-${action.id}`}
    >
      <div className="mb-2 flex flex-wrap items-center justify-between gap-2">
        <div className="flex flex-wrap items-center gap-2 text-xs">
          {action.isFromApplicant ? (
            <span className="inline-flex items-center gap-1 font-semibold text-ink-700">
              <User className="size-3.5" aria-hidden="true" />
              {t('tickets.fromApplicant')}
            </span>
          ) : action.isInternal ? (
            <span className="inline-flex items-center gap-1 font-semibold text-warning-foreground">
              <Lock className="size-3.5" aria-hidden="true" />
              {t('tickets.internalNote')}
            </span>
          ) : (
            <span className="inline-flex items-center gap-1 font-semibold text-brand-700">
              <Mail className="size-3.5" aria-hidden="true" />
              {t('tickets.publicReply')}
            </span>
          )}
          <span className="text-subtle">
            {action.isFromApplicant
              ? ticket.name
              : (action.authorName ?? t('tickets.unknownAuthor'))}{' '}
            · {formatDateTime(action.createdAtUtc, locale)}
          </span>
        </div>

        {action.changedStatusTo !== null && (
          <span className="text-xs text-subtle">
            {t('tickets.movedTo', {
              status: t(`tickets.status.${ticketStatusKey(action.changedStatusTo)}`),
            })}
          </span>
        )}
      </div>

      {action.body && <p className="whitespace-pre-wrap text-sm leading-7">{action.body}</p>}

      {action.documents.length > 0 && (
        <ul className="mt-3 space-y-2">
          {action.documents.map((document) => (
            <li key={document.id} className="rounded-xl border border-border bg-background p-3">
              <p className="text-sm font-semibold">{document.title}</p>
              {document.description && (
                <p className="mt-0.5 text-xs text-subtle">{document.description}</p>
              )}
              <ul className="mt-2 flex flex-wrap items-start gap-2">
                {document.files.map((file) => (
                  <TicketAttachment key={file.id} ticketId={ticket.id} file={file} />
                ))}
              </ul>
            </li>
          ))}
        </ul>
      )}

      {notify.isError && (
        <Alert variant="error" className="mt-3">
          {toMessage(notify.error)}
        </Alert>
      )}

      <div className="mt-3 flex flex-wrap items-center justify-between gap-2 border-t border-border/60 pt-3">
        {action.isFromApplicant ? (
          // Their own message: nothing to email, and nothing to say about delivery.
          <p className="text-xs text-subtle">{t('tickets.applicantWrote')}</p>
        ) : action.notifiedAtUtc ? (
          <p className="text-xs text-success">
            {t('tickets.emailedAt', {
              when: formatDateTime(action.notifiedAtUtc, locale),
              email: action.notifiedEmail ?? ticket.email,
            })}
          </p>
        ) : action.isInternal ? (
          // Said plainly rather than by omission: the reason there is no button here is the whole
          // point of marking a note internal.
          <p className="text-xs text-subtle">{t('tickets.internalNeverEmailed')}</p>
        ) : (
          <p className="text-xs text-subtle">{t('tickets.notEmailedYet')}</p>
        )}

        {action.canNotify && canNotify && (
          <Button
            size="sm"
            variant="outline"
            disabled={notify.isPending}
            onClick={() => notify.mutate(action.id)}
            data-testid={`notify-${action.id}`}
          >
            <Send className="size-3.5" aria-hidden="true" />
            {t('tickets.notifyByEmail')}
          </Button>
        )}
      </div>
    </div>
  );
}

/** Composes an action: a note or a reply, with any number of titled documents. */
function Composer({ ticket }: { ticket: TicketDetailsDto }) {
  const { t } = useTranslation();
  const create = useCreateTicketAction(ticket.id);
  const toMessage = useApiErrorMessage();

  const canUpdate = adminSession.has(Permissions.TicketsUpdate);

  const [replyToSender, setReplyToSender] = useState('');
  const [internalNote, setInternalNote] = useState('');
  const [changeStatusTo, setChangeStatusTo] = useState<string>(NONE);
  const [documents, setDocuments] = useState<TicketDocumentDraft[]>([]);

  const hasContent =
    replyToSender.trim() !== '' || internalNote.trim() !== '' || documents.length > 0;
  const documentsReady = documents.every(
    (document) => document.title.trim() !== '' && document.files.length > 0,
  );

  function reset() {
    setReplyToSender('');
    setInternalNote('');
    setDocuments([]);
    setChangeStatusTo(NONE);
  }

  function submit() {
    create.mutate(
      {
        replyToSender,
        internalNote,
        changeStatusTo: changeStatusTo === NONE ? null : (Number(changeStatusTo) as TicketStatus),
        documents,
      },
      { onSuccess: reset },
    );
  }

  function patchDocument(index: number, patch: Partial<TicketDocumentDraft>) {
    setDocuments((current) =>
      current.map((document, i) => (i === index ? { ...document, ...patch } : document)),
    );
  }

  if (!canUpdate) {
    return null;
  }

  return (
    <AdminPanel title={t('tickets.newAction')} subtitle={t('tickets.newActionSubtitle')}>
      <div className="space-y-5">
        {/* Two boxes rather than a choice between them: answering somebody and telling the team
            what happened is usually one piece of work, and making it two submissions meant the
            note was the half that got skipped. Either may be left empty. */}
        <div className="space-y-4">
          <div className="rounded-xl border border-brand-200 bg-brand-50/40 p-4">
            <Field label={t('tickets.publicReply')} htmlFor="action-reply">
              <p className="mb-2 text-xs text-subtle">
                <Mail className="me-1 inline size-3.5 align-[-2px]" aria-hidden="true" />
                {t('tickets.publicReplyHint')}
              </p>
              <textarea
                id="action-reply"
                rows={5}
                value={replyToSender}
                onChange={(event) => setReplyToSender(event.target.value)}
                placeholder={t('tickets.publicPlaceholder')}
                className="w-full rounded-lg border border-border bg-background p-3 text-sm focus-visible:border-ring focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                data-testid="action-reply"
              />
            </Field>
          </div>

          <div className="rounded-xl border border-warning/40 bg-warning/10 p-4">
            <Field label={t('tickets.internalNote')} htmlFor="action-note">
              <p className="mb-2 text-xs text-subtle">
                <Lock className="me-1 inline size-3.5 align-[-2px]" aria-hidden="true" />
                {t('tickets.internalNoteHint')}
              </p>
              <textarea
                id="action-note"
                rows={4}
                value={internalNote}
                onChange={(event) => setInternalNote(event.target.value)}
                placeholder={t('tickets.internalPlaceholder')}
                className="w-full rounded-lg border border-warning/50 bg-background p-3 text-sm focus-visible:border-ring focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                data-testid="action-note"
              />
            </Field>
          </div>
        </div>

        <fieldset className="space-y-3">
          <div className="flex flex-wrap items-baseline justify-between gap-2">
            <legend className="text-sm font-medium">{t('tickets.documents')}</legend>
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={() =>
                setDocuments((current) => [
                  ...current,
                  { title: '', description: '', isInternal: false, files: [] },
                ])
              }
              data-testid="add-document"
            >
              <Plus className="size-4" aria-hidden="true" />
              {t('tickets.addDocument')}
            </Button>
          </div>

          <p className="text-xs text-subtle">{t('tickets.documentsHint')}</p>

          {documents.map((document, index) => (
            <div
              key={index}
              className={cn(
                'space-y-3 rounded-xl border p-4',
                document.isInternal ? 'border-warning/40 bg-warning/5' : 'border-border',
              )}
            >
              <div className="flex flex-wrap items-start justify-between gap-2">
                <p className="text-xs font-semibold text-subtle">
                  {t('tickets.documentNumber', { number: index + 1 })}
                </p>

                {/* Which side a file belongs to is its own decision: support attach evidence for
                    the team as often as they attach something to send. */}
                <label className="flex items-center gap-2 text-xs">
                  <input
                    type="checkbox"
                    className="size-4 rounded border-border"
                    checked={document.isInternal}
                    onChange={(event) =>
                      patchDocument(index, { isInternal: event.target.checked })
                    }
                    data-testid={`document-internal-${index}`}
                  />
                  {t('tickets.documentInternal')}
                </label>
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  aria-label={t('common.remove')}
                  onClick={() =>
                    setDocuments((current) => current.filter((_, i) => i !== index))
                  }
                  data-testid={`remove-document-${index}`}
                >
                  <Trash2 className="size-4 text-danger" aria-hidden="true" />
                </Button>
              </div>

              <Field label={t('tickets.documentTitle')} htmlFor={`document-title-${index}`} required>
                <Input
                  id={`document-title-${index}`}
                  value={document.title}
                  onChange={(event) => patchDocument(index, { title: event.target.value })}
                  data-testid={`document-title-${index}`}
                />
              </Field>

              <Field
                label={t('tickets.documentDescription')}
                htmlFor={`document-description-${index}`}
              >
                <textarea
                  id={`document-description-${index}`}
                  rows={2}
                  value={document.description}
                  onChange={(event) => patchDocument(index, { description: event.target.value })}
                  className="w-full rounded-lg border border-border bg-background p-3 text-sm focus-visible:border-ring focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
                  data-testid={`document-description-${index}`}
                />
              </Field>

              <Field label={t('tickets.documentFiles')} htmlFor={`document-files-${index}`} required>
                <input
                  id={`document-files-${index}`}
                  type="file"
                  multiple
                  accept=".pdf,.jpg,.jpeg,.png"
                  onChange={(event) =>
                    patchDocument(index, { files: [...(event.target.files ?? [])] })
                  }
                  className="block w-full text-sm file:me-3 file:rounded-lg file:border-0 file:bg-ink-100 file:px-3 file:py-2 file:text-sm"
                  data-testid={`document-files-${index}`}
                />
              </Field>

              {/* Shown before sending, so the wrong screenshot is caught here rather than in
                  somebody's inbox. */}
              {document.files.length > 0 && (
                <ul className="grid gap-2 sm:grid-cols-2">
                  {document.files.map((file, fileIndex) => (
                    <PendingAttachment
                      key={`${file.name}-${fileIndex}`}
                      file={file}
                      onRemove={() =>
                        patchDocument(index, {
                          files: document.files.filter((_, i) => i !== fileIndex),
                        })
                      }
                    />
                  ))}
                </ul>
              )}
            </div>
          ))}
        </fieldset>

        <Field label={t('tickets.alsoMoveStatus')} htmlFor="action-status">
          <Select
            id="action-status"
            value={changeStatusTo}
            onChange={(event) => setChangeStatusTo(event.target.value)}
            data-testid="action-status"
          >
            <option value={NONE}>{t('tickets.leaveStatus')}</option>
            {TICKET_STATUSES.map((status) => (
              <option key={status} value={status}>
                {t(`tickets.status.${ticketStatusKey(status)}`)}
              </option>
            ))}
          </Select>
        </Field>

        {create.isError && <Alert variant="error">{toMessage(create.error)}</Alert>}

        {/* Recording is not sending: said here so nobody expects the sender to have been told. */}
        <Alert variant="info">{t('tickets.recordingIsNotSending')}</Alert>

        <div className="flex justify-end">
          <Button
            type="button"
            disabled={!hasContent || !documentsReady || create.isPending}
            onClick={submit}
            data-testid="submit-action"
          >
            {t('tickets.recordAction')}
          </Button>
        </div>
      </div>
    </AdminPanel>
  );
}
