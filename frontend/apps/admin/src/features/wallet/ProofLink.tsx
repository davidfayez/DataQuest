import { Paperclip } from 'lucide-react';
import { useState } from 'react';
import type { WalletRequestFile } from '@/features/wallet/api';
import { apiClient } from '@/shared/api/client';

/**
 * A link to one attached receipt.
 *
 * The download needs the session token, which a plain href cannot carry, so the bytes are fetched
 * and handed to the browser as a blob. Revoked after a minute — Chrome has long since opened it by
 * then, and holding the URL would pin the whole file in memory.
 */
export function ProofLink({
  requestId,
  file,
}: {
  requestId: string;
  file: WalletRequestFile;
}) {
  const [isFetching, setIsFetching] = useState(false);

  async function open() {
    setIsFetching(true);
    try {
      const blob = await apiClient.getBlob(`admin/wallet-requests/${requestId}/files/${file.id}`);
      const url = URL.createObjectURL(blob);
      window.open(url, '_blank', 'noopener');
      window.setTimeout(() => URL.revokeObjectURL(url), 60_000);
    } finally {
      setIsFetching(false);
    }
  }

  return (
    <button
      type="button"
      onClick={() => void open()}
      disabled={isFetching}
      className="flex items-center gap-1 text-start text-xs text-primary hover:underline disabled:opacity-60"
      title={file.fileName}
    >
      <Paperclip className="size-3 shrink-0" aria-hidden="true" />
      <span className="max-w-[10rem] truncate">{file.fileName}</span>
    </button>
  );
}
