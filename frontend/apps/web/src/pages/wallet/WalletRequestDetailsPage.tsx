import { Alert, Button, Card, CardContent, LoadingState, Spinner, buttonVariants, cn } from '@dv/ui';
import {
  ArrowLeft,
  CheckCircle2,
  CircleDashed,
  Clock,
  FileX2,
  Minus,
  Plus,
  XCircle,
} from 'lucide-react';
import { useState, type ReactNode } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router-dom';
import {
  useCancelWalletRequest,
  useWalletRequest,
  WalletRequestStatus,
  WalletRequestType,
} from '@/entities/wallet/api';
import { WalletRequestAttachment } from '@/features/wallet/WalletRequestAttachment';
import { WalletRequestStatusBadge } from '@/features/wallet/WalletRequestsSection';
import { formatCurrency, formatDateTime } from '@/shared/lib/format';
import { useApiErrorMessage } from '@/shared/lib/useApiError';

/**
 * One deposit or payout request in full.
 *
 * The list can only ever be a summary, and a top-up under review is exactly when the applicant
 * wants to check what they actually sent — the amount, the account, the reference, and the receipt
 * itself. All of it lives here, on a URL they can return to or send to support.
 */
export function WalletRequestDetailsPage() {
  const { t, i18n } = useTranslation();
  const { lang = 'en', id = '' } = useParams<{ lang: string; id: string }>();
  const locale = i18n.resolvedLanguage ?? 'en';
  const toMessage = useApiErrorMessage();

  const request = useWalletRequest(id);
  const cancel = useCancelWalletRequest();
  const [confirming, setConfirming] = useState(false);

  if (request.isPending) {
    return <LoadingState label={t('common.loading')} />;
  }

  if (request.isError || !request.data) {
    return (
      <div className="mx-auto max-w-3xl px-4 py-10 sm:px-6">
        <Alert variant="error">{toMessage(request.error)}</Alert>
      </div>
    );
  }

  const data = request.data;
  const isDeposit = data.type === WalletRequestType.Deposit;
  const decided = data.status !== WalletRequestStatus.Pending;

  return (
    <div className="mx-auto max-w-4xl px-4 py-8 sm:px-6">
      <Link
        to={`/${lang}/wallet`}
        className={buttonVariants({ variant: 'ghost', size: 'sm' })}
        data-testid="back-to-wallet"
      >
        <ArrowLeft className="size-4 rtl:rotate-180" aria-hidden="true" />
        {t('wallet.backToWallet')}
      </Link>

      {/* The amount is the headline: it is what the applicant came to check. */}
      <header className="mt-4 rounded-2xl border border-border bg-card p-6">
        <div className="flex flex-wrap items-start justify-between gap-4">
          <div className="flex items-start gap-3">
            <span
              className={cn(
                'flex size-11 shrink-0 items-center justify-center rounded-full border',
                isDeposit
                  ? 'border-success-border bg-success-muted text-success'
                  : 'border-warning-border bg-warning-muted text-warning',
              )}
            >
              {isDeposit ? (
                <Plus className="size-5" aria-hidden="true" />
              ) : (
                <Minus className="size-5" aria-hidden="true" />
              )}
            </span>

            <div>
              <h1 className="text-xl font-semibold sm:text-2xl">
                {t(isDeposit ? 'wallet.depositTitle' : 'wallet.withdrawTitle')}
              </h1>
              <p className="mt-1 text-3xl font-semibold" data-testid="request-amount">
                {formatCurrency(data.amount, data.currencyCode, locale)}
              </p>
            </div>
          </div>

          <WalletRequestStatusBadge status={data.status} />
        </div>

        <Timeline
          status={data.status}
          createdAtUtc={data.createdAtUtc}
          reviewedAtUtc={data.reviewedAtUtc}
          locale={locale}
        />
      </header>

      <div className="mt-6 grid gap-6 lg:grid-cols-[minmax(0,1fr)_20rem]">
        <div className="space-y-6">
          {/* What was submitted, repeated back. While a top-up is pending this is the only place
              the applicant can check they quoted the right reference. */}
          <Card>
            <CardContent className="p-6">
              <h2 className="font-medium">{t('wallet.whatYouSent')}</h2>

              <dl className="mt-4 space-y-3 text-sm">
                <Row label={t('wallet.amount')}>
                  {formatCurrency(data.amount, data.currencyCode, locale)}
                </Row>

                {data.paymentMethodName && (
                  <Row label={t('wallet.paymentMethod')}>
                    {data.paymentMethodName}
                    {data.paymentMethodTypeName ? (
                      <span className="text-muted-foreground"> · {data.paymentMethodTypeName}</span>
                    ) : null}
                  </Row>
                )}

                {data.paymentAccountLabel && (
                  <Row label={t('wallet.paidTo')}>
                    {data.paymentAccountLabel}
                    {data.paymentAccountNumber ? (
                      <>
                        <br />
                        <span className="font-mono text-xs text-muted-foreground" dir="ltr">
                          {data.paymentAccountNumber}
                        </span>
                      </>
                    ) : null}
                  </Row>
                )}

                {data.referenceNumber && (
                  <Row label={t('wallet.referenceNumber')}>
                    <span className="font-mono" dir="ltr">
                      {data.referenceNumber}
                    </span>
                  </Row>
                )}

                {data.applicantNote && (
                  <Row label={t('wallet.yourNote')}>
                    <span className="whitespace-pre-wrap">{data.applicantNote}</span>
                  </Row>
                )}
              </dl>
            </CardContent>
          </Card>

          {/* Documents get their own card rather than a line in the summary: the receipt is the
              thing being checked, so it is shown rather than counted. */}
          <Card>
            <CardContent className="p-6">
              <h2 className="font-medium">{t('wallet.proofTitle')}</h2>

              {data.files.length === 0 ? (
                <p className="mt-4 flex items-center gap-2 text-sm text-muted-foreground">
                  <FileX2 className="size-4" aria-hidden="true" />
                  {t('wallet.noProof')}
                </p>
              ) : (
                <ul className="mt-4 flex flex-wrap items-start gap-3" data-testid="request-files">
                  {data.files.map((file) => (
                    <WalletRequestAttachment key={file.id} requestId={data.id} file={file} />
                  ))}
                </ul>
              )}
            </CardContent>
          </Card>
        </div>

        <div className="space-y-6">
          {/* The decision, and above all the reason for it — which matters most when the answer
              was no. */}
          <Card>
            <CardContent className="p-6">
              <h2 className="font-medium">{t('wallet.decision')}</h2>

              {decided ? (
                <dl className="mt-4 space-y-3 text-sm">
                  {data.reviewedAtUtc && (
                    <Row label={t('wallet.reviewedOn')}>
                      {formatDateTime(data.reviewedAtUtc, locale)}
                    </Row>
                  )}

                  {data.reviewedByName && (
                    <Row label={t('wallet.reviewedBy')}>{data.reviewedByName}</Row>
                  )}

                  {/* Shown only where the reviewer found something other than the claim. */}
                  {data.confirmedAmount !== null && data.confirmedAmount !== data.amount && (
                    <Row label={t('wallet.confirmedAmount')}>
                      {formatCurrency(data.confirmedAmount, data.currencyCode, locale)}
                    </Row>
                  )}

                  {data.confirmedReference && data.confirmedReference !== data.referenceNumber && (
                    <Row label={t('wallet.confirmedReference')}>
                      <span className="font-mono" dir="ltr">
                        {data.confirmedReference}
                      </span>
                    </Row>
                  )}

                  {data.reviewerNote && (
                    <Row label={t('wallet.reviewerNote')}>
                      <span className="whitespace-pre-wrap">{data.reviewerNote}</span>
                    </Row>
                  )}
                </dl>
              ) : (
                <p className="mt-4 text-sm text-muted-foreground">
                  {t(isDeposit ? 'wallet.awaitingDeposit' : 'wallet.awaitingWithdrawal')}
                </p>
              )}
            </CardContent>
          </Card>

          <Card>
            <CardContent className="p-6">
              <h2 className="font-medium">{t('wallet.requestInfo')}</h2>

              <dl className="mt-4 space-y-3 text-sm">
                <Row label={t('wallet.orderNumber')}>
                  <span className="font-mono" dir="ltr">
                    {data.orderNumber}
                  </span>
                </Row>

                <Row label={t('wallet.submittedOn')}>
                  {formatDateTime(data.createdAtUtc, locale)}
                </Row>

              </dl>
            </CardContent>
          </Card>

          {/* Offered strictly from the server's flag, so a request decided in another tab cannot
              be withdrawn from a stale view. */}
          {data.canCancel && (
            <Card>
              <CardContent className="space-y-3 p-6">
                {cancel.isError && <Alert variant="error">{toMessage(cancel.error)}</Alert>}

                {confirming ? (
                  <>
                    <p className="text-sm text-muted-foreground">{t('wallet.cancelConfirmBody')}</p>
                    <div className="flex flex-wrap gap-2">
                      <Button variant="outline" size="sm" onClick={() => setConfirming(false)}>
                        {t('dashboard.keepIt')}
                      </Button>
                      <Button
                        variant="destructive"
                        size="sm"
                        disabled={cancel.isPending}
                        data-testid="confirm-cancel-request"
                        onClick={() => cancel.mutate(data.id, { onSuccess: () => setConfirming(false) })}
                      >
                        {cancel.isPending && <Spinner />}
                        {t('wallet.confirmCancelRequest')}
                      </Button>
                    </div>
                  </>
                ) : (
                  <Button
                    variant="outline"
                    className="w-full"
                    onClick={() => setConfirming(true)}
                    data-testid="cancel-request"
                  >
                    {t('wallet.cancelRequest')}
                  </Button>
                )}
              </CardContent>
            </Card>
          )}
        </div>
      </div>
    </div>
  );
}

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="grid gap-0.5">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="break-words">{children}</dd>
    </div>
  );
}

/**
 * Where the request has got to, in three steps.
 *
 * A status word alone does not say whether anything is still expected to happen; the line makes
 * "sent, waiting" and "sent, answered" distinguishable at a glance.
 */
function Timeline({
  status,
  createdAtUtc,
  reviewedAtUtc,
  locale,
}: {
  status: WalletRequestStatus;
  createdAtUtc: string;
  reviewedAtUtc: string | null;
  locale: string;
}) {
  const { t } = useTranslation();

  const decided = status !== WalletRequestStatus.Pending;
  const outcome =
    status === WalletRequestStatus.Approved
      ? { icon: CheckCircle2, tone: 'text-success', key: 'wallet.requestApproved' }
      : status === WalletRequestStatus.Rejected
        ? { icon: XCircle, tone: 'text-destructive', key: 'wallet.requestRejected' }
        : status === WalletRequestStatus.Cancelled
          ? { icon: XCircle, tone: 'text-muted-foreground', key: 'wallet.requestCancelled' }
          : { icon: CircleDashed, tone: 'text-muted-foreground', key: 'wallet.awaitingDecision' };

  const Outcome = outcome.icon;

  return (
    <ol className="mt-6 flex flex-wrap items-center gap-x-3 gap-y-2 border-t border-border pt-4 text-sm">
      <li className="flex items-center gap-2">
        <CheckCircle2 className="size-4 text-success" aria-hidden="true" />
        <span>{t('wallet.timelineSent')}</span>
        <span className="text-xs text-muted-foreground">{formatDateTime(createdAtUtc, locale)}</span>
      </li>

      <Separator />

      <li className="flex items-center gap-2">
        {decided ? (
          <CheckCircle2 className="size-4 text-success" aria-hidden="true" />
        ) : (
          <Clock className="size-4 animate-pulse text-warning" aria-hidden="true" />
        )}
        <span className={decided ? undefined : 'font-medium'}>{t('wallet.timelineReview')}</span>
      </li>

      <Separator />

      <li className="flex items-center gap-2">
        <Outcome className={cn('size-4', outcome.tone)} aria-hidden="true" />
        <span className={decided ? 'font-medium' : 'text-muted-foreground'}>{t(outcome.key)}</span>
        {reviewedAtUtc && (
          <span className="text-xs text-muted-foreground">
            {formatDateTime(reviewedAtUtc, locale)}
          </span>
        )}
      </li>
    </ol>
  );
}

function Separator() {
  return <li aria-hidden="true" className="h-px w-6 bg-border" />;
}
