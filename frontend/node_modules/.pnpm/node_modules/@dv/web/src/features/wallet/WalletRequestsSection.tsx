import { Alert, Button, Card, CardContent, cn } from '@dv/ui';
import { ChevronLeft, ChevronRight, ImageIcon, Minus, Paperclip, Plus } from 'lucide-react';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router-dom';
import {
  useWalletRequests,
  WalletRequestStatus,
  WalletRequestType,
  type WalletRequestDto,
} from '@/entities/wallet/api';
import { formatCurrency, formatDateTime } from '@/shared/lib/format';
import { useApiErrorMessage } from '@/shared/lib/useApiError';

const STATUS_STYLES: Record<WalletRequestStatus, string> = {
  [WalletRequestStatus.Pending]: 'bg-warning-muted text-foreground border-warning-border',
  [WalletRequestStatus.Approved]: 'bg-success-muted text-foreground border-success-border',
  [WalletRequestStatus.Rejected]: 'bg-destructive-muted text-foreground border-destructive-border',
  [WalletRequestStatus.Cancelled]: 'bg-muted text-muted-foreground border-border',
};

const STATUS_KEYS: Record<WalletRequestStatus, string> = {
  [WalletRequestStatus.Pending]: 'wallet.requestPending',
  [WalletRequestStatus.Approved]: 'wallet.requestApproved',
  [WalletRequestStatus.Rejected]: 'wallet.requestRejected',
  [WalletRequestStatus.Cancelled]: 'wallet.requestCancelled',
};

export function WalletRequestStatusBadge({ status }: { status: WalletRequestStatus }) {
  const { t } = useTranslation();

  return (
    <span
      className={`inline-flex items-center rounded-full border px-2.5 py-0.5 text-xs font-medium ${
        STATUS_STYLES[status] ?? STATUS_STYLES[WalletRequestStatus.Cancelled]
      }`}
    >
      {t(STATUS_KEYS[status] ?? 'wallet.requestPending')}
    </span>
  );
}

/**
 * The order's deposit and payout requests, as a summary list.
 *
 * Each row answers only the questions that get asked at a glance — how much, which way, and where
 * it has got to — and opens its own page for the rest. Repeating every reference and note inline
 * made a wallet with a handful of top-ups unreadable, and still could not show the receipt.
 */
export function WalletRequestsSection({ currency }: { currency: string }) {
  const { t } = useTranslation();
  const { lang = 'en' } = useParams<{ lang: string }>();
  const toMessage = useApiErrorMessage();

  const [page, setPage] = useState(1);
  const requests = useWalletRequests(page);

  const items = requests.data?.items ?? [];
  const paging = requests.data;

  // Page 1 being empty means the order has never raised a request; there is nothing to introduce.
  if (requests.isPending || (items.length === 0 && page === 1)) {
    return null;
  }

  return (
    <section className="space-y-3">
      <div className="flex flex-wrap items-baseline justify-between gap-2">
        <h2 className="font-medium">{t('wallet.requestsTitle')}</h2>
        <p className="text-sm text-muted-foreground">{t('wallet.requestsSubtitle')}</p>
      </div>

      {requests.isError && <Alert variant="error">{toMessage(requests.error)}</Alert>}

      <ul className="space-y-2">
        {items.map((request) => (
          <li key={request.id}>
            <RequestRow request={request} currency={currency} lang={lang} />
          </li>
        ))}
      </ul>

      {paging && paging.totalPages > 1 && (
        <div className="flex items-center justify-between gap-3">
          <p className="text-sm text-muted-foreground" data-testid="requests-page-label">
            {t('wallet.pageOf', { page: paging.page, total: paging.totalPages })}
          </p>

          <div className="flex gap-2">
            <Button
              variant="outline"
              size="sm"
              disabled={!paging.hasPrevious || requests.isFetching}
              onClick={() => setPage((current) => Math.max(1, current - 1))}
              data-testid="requests-previous"
            >
              <ChevronLeft className="size-4 rtl:rotate-180" aria-hidden="true" />
              {t('common.previous')}
            </Button>

            <Button
              variant="outline"
              size="sm"
              disabled={!paging.hasNext || requests.isFetching}
              onClick={() => setPage((current) => current + 1)}
              data-testid="requests-next"
            >
              {t('common.next')}
              <ChevronRight className="size-4 rtl:rotate-180" aria-hidden="true" />
            </Button>
          </div>
        </div>
      )}
    </section>
  );
}

/**
 * One request as a single link.
 *
 * The whole card is the target rather than a "details" button in the corner: it is the only action
 * a row has, and a larger target is easier on a phone. Cancelling now lives on the details page,
 * where the applicant can see what they are about to withdraw before they do it.
 */
function RequestRow({
  request,
  currency,
  lang,
}: {
  request: WalletRequestDto;
  currency: string;
  lang: string;
}) {
  const { t, i18n } = useTranslation();
  const locale = i18n.resolvedLanguage ?? 'en';

  const isDeposit = request.type === WalletRequestType.Deposit;
  const images = request.files.filter((file) => file.contentType.startsWith('image/')).length;

  return (
    <Card
      className="transition-colors hover:border-primary focus-within:border-primary"
      data-testid={`wallet-request-${request.id}`}
    >
      <CardContent className="p-0">
        <Link
          to={`/${lang}/wallet/requests/${request.id}`}
          className="flex flex-wrap items-center gap-x-4 gap-y-3 rounded-lg p-4 outline-none"
          data-testid={`open-request-${request.id}`}
        >
          <span
            className={cn(
              'flex size-10 shrink-0 items-center justify-center rounded-full border',
              isDeposit
                ? 'border-success-border bg-success-muted text-success'
                : 'border-warning-border bg-warning-muted text-warning',
            )}
            aria-hidden="true"
          >
            {isDeposit ? <Plus className="size-5" /> : <Minus className="size-5" />}
          </span>

          <span className="min-w-0 flex-1 space-y-1">
            <span className="flex flex-wrap items-center gap-2">
              <span className="font-medium">
                {t(isDeposit ? 'wallet.depositTitle' : 'wallet.withdrawTitle')}
              </span>
              <WalletRequestStatusBadge status={request.status} />
            </span>

            <span className="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm text-muted-foreground">
              <span>{formatDateTime(request.createdAtUtc, locale)}</span>

              {request.paymentMethodName && <span>{request.paymentMethodName}</span>}

              {request.referenceNumber && (
                <span className="font-mono text-xs" dir="ltr">
                  {request.referenceNumber}
                </span>
              )}

              {/* The count still earns its place here — it is what tells the applicant whether
                  opening the request will show them their receipt. */}
              {request.files.length > 0 && (
                <span className="inline-flex items-center gap-1">
                  {images > 0 ? (
                    <ImageIcon className="size-3.5" aria-hidden="true" />
                  ) : (
                    <Paperclip className="size-3.5" aria-hidden="true" />
                  )}
                  {request.files.length}
                </span>
              )}
            </span>
          </span>

          <span className="flex items-center gap-3">
            <span className="font-semibold whitespace-nowrap">
              {formatCurrency(request.amount, request.currencyCode || currency, locale)}
            </span>
            <ChevronRight className="size-4 shrink-0 text-muted-foreground rtl:rotate-180" aria-hidden="true" />
          </span>
        </Link>
      </CardContent>
    </Card>
  );
}

export type { WalletRequestDto };
