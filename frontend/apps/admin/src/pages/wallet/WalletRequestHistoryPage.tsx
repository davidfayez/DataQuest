import { AdminPageHeader, AdminPanel, Alert, Button, LoadingState, cn } from '@dv/ui';
import {
  ArrowLeft,
  Ban,
  CheckCircle2,
  Clock,
  FileX2,
  PlusCircle,
  XCircle,
  type LucideIcon,
} from 'lucide-react';
import { Fragment } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useNavigate, useParams } from 'react-router-dom';
import {
  STATUS_LABEL,
  STATUS_PILL,
  useWalletRequest,
  useWalletRequestHistory,
  WalletRequestStatus,
  WalletRequestType,
  type AdminWalletRequest,
  type WalletRequestDocumentValue,
  type WalletRequestFile,
  type WalletRequestHistoryEntry,
} from '@/features/wallet/api';
import { ProofFile } from '@/features/wallet/ProofFile';
import { formatCurrency, formatDateTime } from '@/shared/lib/format';
import { useApiErrorMessage } from '@/shared/lib/useApiError';
import { StatusPill } from '@/shared/ui/StatusPill';

/**
 * How each recorded event reads at a glance: a marker and a tone, so the shape of a request's life
 * is legible before the words are. Anything the server records that is not listed falls back to a
 * neutral marker rather than disappearing.
 */
const EVENT_MARKS: Record<string, { icon: LucideIcon; tone: string }> = {
  'WalletRequest.Created': { icon: PlusCircle, tone: 'bg-sky-50 text-sky-600 ring-sky-200' },
  'WalletRequest.Approved': {
    icon: CheckCircle2,
    tone: 'bg-emerald-50 text-emerald-600 ring-emerald-200',
  },
  'WalletRequest.Rejected': { icon: XCircle, tone: 'bg-red-50 text-red-600 ring-red-200' },
  'WalletRequest.Cancelled': { icon: Ban, tone: 'bg-ink-100 text-ink-500 ring-ink-200' },
};

const FALLBACK_MARK = { icon: Clock, tone: 'bg-ink-100 text-ink-500 ring-ink-200' };

/**
 * One request's trail, on its own page.
 *
 * It was a dialog, which is the wrong container for it: the trail is the thing a reviewer opens
 * when they need to answer for a decision — days later, often to someone else — so it has to
 * survive a refresh, be linkable, and print. A page also has the room to put the request itself
 * beside its history, which the dialog never did; reading "approved 480 of 500" means nothing
 * without the claim next to it.
 */
export function WalletRequestHistoryPage() {
  const { t, i18n } = useTranslation();
  const { id = '' } = useParams();
  const locale = i18n.resolvedLanguage ?? 'en';
  const toMessage = useApiErrorMessage();
  const navigate = useNavigate();

  const request = useWalletRequest(id);
  const history = useWalletRequestHistory(id);

  if (request.isPending) {
    return <LoadingState label={t('common.loading')} />;
  }

  if (request.isError || !request.data) {
    return (
      <div className="animate-fade-in space-y-6">
        <BackLink onClick={() => navigate('/wallet-requests')} />
        <Alert variant="error">{toMessage(request.error)}</Alert>
      </div>
    );
  }

  const data = request.data;
  const entries = history.data ?? [];

  return (
    <div className="animate-fade-in space-y-6">
      <BackLink onClick={() => navigate('/wallet-requests')} />

      <AdminPageHeader
        title={t('walletRequests.historyTitle')}
        subtitle={`${data.orderNumber} · ${t(
          data.type === WalletRequestType.Deposit
            ? 'walletRequests.deposit'
            : 'walletRequests.withdrawal',
        )} · ${formatCurrency(data.amount, data.currencyCode, locale)}`}
      />

      {/* The trail leads; the request itself sits beside it as the reference the trail keeps
          pointing at. */}
      <div className="grid gap-6 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <AdminPanel
          title={t('walletRequests.trailTitle')}
          subtitle={t('walletRequests.historyFor', { orderNumber: data.orderNumber })}
        >
          {history.isPending ? (
            <LoadingState label={t('common.loading')} />
          ) : history.isError ? (
            <Alert variant="error">{toMessage(history.error)}</Alert>
          ) : entries.length === 0 ? (
            <p className="flex items-center gap-2 py-6 text-sm text-ink-400">
              <FileX2 className="size-4" aria-hidden="true" />
              {t('walletRequests.historyEmpty')}
            </p>
          ) : (
            <ol className="relative" data-testid="request-history">
              {entries.map((entry, index) => (
                <TrailEntry
                  key={entry.id}
                  entry={entry}
                  locale={locale}
                  isLast={index === entries.length - 1}
                />
              ))}
            </ol>
          )}
        </AdminPanel>

        <RequestSummary request={data} locale={locale} />
      </div>
    </div>
  );
}

function BackLink({ onClick }: { onClick: () => void }) {
  const { t } = useTranslation();

  return (
    <Button variant="ghost" size="sm" onClick={onClick} data-testid="back-to-wallet-requests">
      <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden="true" />
      {t('walletRequests.backToQueue')}
    </Button>
  );
}

/**
 * One event on the trail.
 *
 * The connecting line is drawn per entry rather than on the list, and stops at the last one — a
 * line running past the final event reads as "and then something else", which is exactly wrong on
 * a record of what happened.
 */
function TrailEntry({
  entry,
  locale,
  isLast,
}: {
  entry: WalletRequestHistoryEntry;
  locale: string;
  isLast: boolean;
}) {
  const { t } = useTranslation();

  const mark = EVENT_MARKS[entry.action] ?? FALLBACK_MARK;
  const Icon = mark.icon;

  return (
    <li className="relative flex gap-4 pb-6 last:pb-0">
      {!isLast && (
        <span
          className="absolute start-[1.125rem] top-9 bottom-0 w-px bg-ink-100"
          aria-hidden="true"
        />
      )}

      <span
        className={cn(
          'relative z-10 flex size-9 shrink-0 items-center justify-center rounded-full ring-1',
          mark.tone,
        )}
      >
        <Icon className="size-4" aria-hidden="true" />
      </span>

      <div className="min-w-0 flex-1 pt-1">
        <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
          <p className="font-medium text-ink-950">
            {t(`walletRequests.event.${entry.action}`, { defaultValue: entry.action })}
          </p>
          <p className="text-xs text-ink-400">{formatDateTime(entry.createdAtUtc, locale)}</p>
        </div>

        <p className="mt-0.5 text-xs text-ink-400">
          {entry.actorName ?? t('walletRequests.actorUnknown')}
        </p>

        {entry.details.length > 0 && (
          <dl className="mt-3 grid grid-cols-[auto,1fr] gap-x-4 gap-y-1.5 rounded-xl bg-cream/50 p-3 text-sm">
            {entry.details.map((detail) => (
              <Fragment key={detail.key}>
                <dt className="text-ink-400">
                  {t(`walletRequests.detail.${detail.key}`, { defaultValue: detail.key })}
                </dt>
                <dd className="font-medium break-words text-ink-950">{detail.value}</dd>
              </Fragment>
            ))}
          </dl>
        )}
      </div>
    </li>
  );
}

/** The request the trail is about, so a decision can be read against what was claimed. */
function RequestSummary({ request, locale }: { request: AdminWalletRequest; locale: string }) {
  const { t } = useTranslation();

  const decided = request.status !== WalletRequestStatus.Pending;

  return (
    <div className="space-y-6">
      <AdminPanel title={t('walletRequests.requestTitle')}>
        <dl className="space-y-3 text-sm">
          <Row label={t('walletRequests.status')}>
            <StatusPill
              statusName={STATUS_PILL[request.status] ?? 'Draft'}
              label={t(STATUS_LABEL[request.status] ?? 'walletRequests.pending')}
            />
          </Row>

          <Row label={t('walletRequests.order')}>
            <Link
              to={`/orders/${request.orderId}`}
              className="font-mono text-primary hover:underline"
              dir="ltr"
            >
              {request.orderNumber}
            </Link>
          </Row>

          <Row label={t('walletRequests.amount')}>
            {formatCurrency(request.amount, request.currencyCode, locale)}
          </Row>

          {/* Only worth a line where the reviewer found something other than the claim. */}
          {request.confirmedAmount !== null && request.confirmedAmount !== request.amount && (
            <Row label={t('walletRequests.confirmedAmount')}>
              {formatCurrency(request.confirmedAmount, request.currencyCode, locale)}
            </Row>
          )}

          {request.paymentMethodName && (
            <Row label={t('walletRequests.paidBy')}>
              {request.paymentMethodName}
              {request.paymentAccountNumber && (
                <span className="mt-0.5 block font-mono text-xs text-ink-400" dir="ltr">
                  {request.paymentAccountNumber}
                </span>
              )}
            </Row>
          )}

          {request.referenceNumber && (
            <Row label={t('walletRequests.reference')}>
              <span className="font-mono" dir="ltr">
                {request.referenceNumber}
              </span>
            </Row>
          )}

          {request.confirmedReference &&
            request.confirmedReference !== request.referenceNumber && (
              <Row label={t('walletRequests.confirmedReference')}>
                <span className="font-mono" dir="ltr">
                  {request.confirmedReference}
                </span>
              </Row>
            )}

          <Row label={t('walletRequests.requested')}>
            {formatDateTime(request.createdAtUtc, locale)}
          </Row>

          {decided && request.reviewedByName && (
            <Row label={t('walletRequests.decidedBy')}>
              {request.reviewedByName}
              {request.reviewedAtUtc && (
                <span className="mt-0.5 block text-xs text-ink-400">
                  {formatDateTime(request.reviewedAtUtc, locale)}
                </span>
              )}
            </Row>
          )}

          {request.applicantNote && (
            <Row label={t('walletRequests.applicantNote')}>
              <span className="whitespace-pre-wrap">{request.applicantNote}</span>
            </Row>
          )}

          {request.reviewerNote && (
            <Row label={t('walletRequests.decisionNote')}>
              <span className="whitespace-pre-wrap">{request.reviewerNote}</span>
            </Row>
          )}
        </dl>
      </AdminPanel>

      <RequestDocuments request={request} />
    </div>
  );
}

/** One heading on the documents panel: general proof, or one of the method's documents. */
interface DocumentGroup {
  key: string;
  title: string;
  files: WalletRequestFile[];
  values: WalletRequestDocumentValue[];
}

/**
 * The files and details the applicant sent, grouped by the document they were sent for. Proof of
 * transfer comes first; each payment method document follows with the details typed beside it.
 */
function RequestDocuments({ request }: { request: AdminWalletRequest }) {
  const { t } = useTranslation();

  const groups: DocumentGroup[] = [];
  const general = request.files.filter((file) => !file.requiredFileId);
  if (general.length > 0) {
    groups.push({ key: 'proof', title: t('walletRequests.generalProof'), files: general, values: [] });
  }

  const byDocument = new Map<string, DocumentGroup>();
  const groupFor = (id: string, name: string | null) => {
    let group = byDocument.get(id);
    if (!group) {
      group = { key: id, title: name ?? t('walletRequests.documents'), files: [], values: [] };
      byDocument.set(id, group);
      groups.push(group);
    }
    return group;
  };
  for (const file of request.files) {
    if (file.requiredFileId) groupFor(file.requiredFileId, file.documentName).files.push(file);
  }
  for (const value of request.documentValues ?? []) {
    groupFor(value.requiredFileId, value.documentName).values.push(value);
  }

  if (groups.length === 0) return null;

  const onlyProof = groups.length === 1 && groups[0]!.key === 'proof';

  return (
    <AdminPanel title={onlyProof ? t('walletRequests.proof') : t('walletRequests.documents')}>
      <div className="space-y-5" data-testid="request-documents">
        {groups.map((group) => (
          <section key={group.key} className="space-y-3">
            {!onlyProof && (
              <h3 className="text-sm font-semibold text-ink-900">{group.title}</h3>
            )}

            {group.files.length > 0 && (
              <ul className="space-y-3">
                {group.files.map((file) => (
                  <ProofFile key={file.id} requestId={request.id} file={file} />
                ))}
              </ul>
            )}

            {group.values.length > 0 && (
              <div className="rounded-xl bg-cream/50 p-3">
                <p className="mb-2 text-xs text-ink-400">{t('walletRequests.detailsProvided')}</p>
                <dl className="space-y-2 text-sm">
                  {group.values.map((value, index) => (
                    <div key={index} className="grid gap-0.5">
                      <dt className="text-xs text-ink-400">{value.fieldName}</dt>
                      <dd className="font-medium break-words text-ink-950">{value.value}</dd>
                    </div>
                  ))}
                </dl>
              </div>
            )}
          </section>
        ))}
      </div>
    </AdminPanel>
  );
}

function Row({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="grid gap-0.5">
      <dt className="text-xs text-ink-400">{label}</dt>
      <dd className="break-words text-ink-950">{children}</dd>
    </div>
  );
}
