import { useQuery } from '@tanstack/react-query';
import { adminKeys, apiClient } from '@/shared/api/client';

export const WalletRequestType = { Deposit: 0, Withdrawal: 1 } as const;

export const WalletRequestStatus = {
  Pending: 0,
  Approved: 1,
  Rejected: 2,
  Cancelled: 3,
} as const;

export interface WalletRequestFile {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  createdAtUtc: string;
}

export interface AdminWalletRequest {
  id: string;
  orderId: string;
  orderNumber: string;
  type: number;
  typeName: string;
  status: number;
  statusName: string;
  amount: number;
  currencyCode: string;
  applicantNote: string | null;
  reviewerNote: string | null;
  requestedByName: string | null;
  reviewedByName: string | null;
  reviewedAtUtc: string | null;
  createdAtUtc: string;
  /** Populated only for a deposit raised through a payment method. */
  paymentMethodName: string | null;
  paymentMethodTypeName: string | null;
  paymentAccountLabel: string | null;
  paymentAccountNumber: string | null;
  referenceNumber: string | null;
  /** What the reviewer confirmed arrived; the wallet is credited with this, not the claim. */
  confirmedAmount: number | null;
  confirmedReference: string | null;
  files: WalletRequestFile[];
}

/** One entry in a request's own trail, read from the audit log. */
export interface WalletRequestHistoryEntry {
  id: string;
  action: string;
  status: number | null;
  statusName: string | null;
  actorName: string | null;
  details: { key: string; value: string }[];
  createdAtUtc: string;
}

/** The queue's own pill tones, mapped onto the shared component's status vocabulary. */
export const STATUS_PILL: Record<number, string> = {
  [WalletRequestStatus.Pending]: 'PendingPayment',
  [WalletRequestStatus.Approved]: 'Success',
  [WalletRequestStatus.Rejected]: 'Failed',
  [WalletRequestStatus.Cancelled]: 'Draft',
};

export const STATUS_LABEL: Record<number, string> = {
  [WalletRequestStatus.Pending]: 'walletRequests.pending',
  [WalletRequestStatus.Approved]: 'walletRequests.approved',
  [WalletRequestStatus.Rejected]: 'walletRequests.rejected',
  [WalletRequestStatus.Cancelled]: 'walletRequests.cancelled',
};

/** One request in full — what its history page reads before showing the trail. */
export function useWalletRequest(requestId: string) {
  return useQuery({
    queryKey: adminKeys.walletRequest(requestId),
    queryFn: () => apiClient.get<AdminWalletRequest>(`admin/wallet-requests/${requestId}`),
    enabled: Boolean(requestId),
  });
}

/**
 * The request's trail.
 *
 * Read from the audit log through a request-scoped endpoint, so a reviewer sees this without
 * needing the platform-wide audit permission.
 */
export function useWalletRequestHistory(requestId: string) {
  return useQuery({
    queryKey: adminKeys.walletRequestHistory(requestId),
    queryFn: () =>
      apiClient.get<WalletRequestHistoryEntry[]>(`admin/wallet-requests/${requestId}/history`),
    enabled: Boolean(requestId),
  });
}

/** What the browser can render itself, given the bytes: receipts are photos or PDFs. */
export const isPreviewableProof = (contentType: string): boolean =>
  contentType.startsWith('image/') || contentType === 'application/pdf';

/**
 * The receipt's bytes.
 *
 * Fetched through the API client rather than linked: the endpoint needs the session's bearer
 * token, and that lives in memory — a plain href or src arrives without it and 401s.
 */
export function fetchWalletRequestFile(requestId: string, fileId: string): Promise<Blob> {
  return apiClient.getBlob(`admin/wallet-requests/${requestId}/files/${fileId}`);
}

/** Saves a receipt under its own name. */
export async function downloadWalletRequestFile(
  requestId: string,
  file: WalletRequestFile,
): Promise<void> {
  const blob = await fetchWalletRequestFile(requestId, file.id);
  const url = URL.createObjectURL(blob);

  const link = document.createElement('a');
  link.href = url;
  link.download = file.fileName;
  document.body.append(link);
  link.click();
  link.remove();

  // Revoked on the next tick: revoking synchronously can beat the click in some browsers.
  setTimeout(() => URL.revokeObjectURL(url), 0);
}
