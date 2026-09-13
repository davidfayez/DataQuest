import { QrCode } from 'lucide-react';
import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { apiClient } from '@/shared/api/client';

/**
 * The scannable code for one receiving account.
 *
 * Fetched rather than pointed at with a plain `src`: the access token lives in memory, so an
 * `<img>` request would arrive unauthenticated and the endpoint — which is deliberately scoped to
 * methods this order may actually pay — would refuse it. The bytes are pulled with the session's
 * credentials and handed to the tag as an object URL, revoked when the dialog closes.
 */
export function AccountBarcode({ accountId, label }: { accountId: string; label: string }) {
  const { t } = useTranslation();
  const [url, setUrl] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    let objectUrl: string | null = null;
    let cancelled = false;

    apiClient
      .getBlob(`orders/me/payment-methods/accounts/${accountId}/barcode`)
      .then((blob) => {
        if (cancelled) return;
        objectUrl = URL.createObjectURL(blob);
        setUrl(objectUrl);
      })
      .catch(() => {
        if (!cancelled) setFailed(true);
      });

    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [accountId]);

  // A missing code is not worth an error: the number above it is enough to pay with.
  if (failed) return null;

  return (
    <span className="mt-2 block">
      {url ? (
        <img
          src={url}
          alt={t('wallet.barcodeAlt', { label })}
          className="size-32 rounded-lg border border-border bg-white object-contain p-1"
          data-testid={`account-barcode-${accountId}`}
        />
      ) : (
        <span className="block size-32 animate-pulse rounded-lg border border-border bg-muted" />
      )}
      <span className="mt-1 flex items-center gap-1 text-xs text-muted-foreground">
        <QrCode className="size-3.5" aria-hidden="true" />
        {t('wallet.scanBarcode')}
      </span>
    </span>
  );
}
