import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient, queryKeys } from '@/shared/api/client';
import type { RequiredFileFieldDto, RequiredFileSampleDto } from '@/entities/application/types';

export enum WalletTransactionType {
  TopUp = 0,
  Payment = 1,
  Refund = 2,
  /** Funds held the moment a payout is requested, so they cannot also be spent. */
  Withdrawal = 3,
  /** Returns a held payout when the request is rejected or cancelled. */
  WithdrawalReversal = 4,
}

export enum WalletRequestType {
  Deposit = 0,
  Withdrawal = 1,
}

export enum WalletRequestStatus {
  Pending = 0,
  Approved = 1,
  Rejected = 2,
  Cancelled = 3,
}

export interface WalletSummaryDto {
  id: string;
  balance: number;
  currencyId: string;
  currencyCode: string;
  currencySymbol: string;
}

export interface WalletTransactionDto {
  id: string;
  type: WalletTransactionType;
  typeName: string;
  amount: number;
  /** Negative for payments and holds, positive for credits — ready to render as-is. */
  signedAmount: number;
  balanceAfter: number;
  referenceApplicationIds: string[];
  referenceApplicationNumbers: string[];
  performedByName: string | null;
  note: string | null;
  createdAtUtc: string;
}

/** What the server permits on this environment; the page renders its actions from these. */
export interface WalletFeaturesDto {
  depositRequestsEnabled: boolean;
  withdrawalRequestsEnabled: boolean;
  simulatedDepositsEnabled: boolean;
  minimumRequestAmount: number;
  maximumRequestAmount: number;
}

/**
 * Mirrors the API's PaymentMethodKind: how money reaches the platform.
 *
 * What the deposit form asks for comes from the method's `requires*` flags, not from this — a
 * transfer provider may want receiving numbers, a QR code, a bank name, a link, or a combination.
 */
export enum PaymentMethodKind {
  /** The applicant sends money to us and says so; a reviewer confirms it. */
  Transfer = 1,
  /** The applicant pays through PayPal. */
  PayPal = 4,
}

/** One receiving account the applicant may say they paid. */
export interface PaymentAccountOptionDto {
  id: string;
  label: string;
  accountNumber: string;
  accountHolder: string | null;
  /** Set only on a bank transfer; the wallet kinds identify their provider by the number alone. */
  bankName: string | null;
  hasBarcode: boolean;
}

/**
 * A payment method offered for this order, already filtered by the server to its country and
 * wallet currency. The `requires*` flags drive which fields the deposit form shows, so a new
 * provider configured in the admin panel needs no change here.
 */
export interface PaymentMethodOptionDto {
  id: string;
  name: string;
  description: string | null;
  publicNote: string | null;
  paymentMethodTypeId: string;
  typeName: string;
  kind: PaymentMethodKind;
  kindName: string;
  externalUrl: string | null;
  requiresAccountNumber: boolean;
  requiresBarcode: boolean;
  requiresBank: boolean;
  requiresExternalUrl: boolean;
  requiresProofDocument: boolean;
  requiresReferenceNumber: boolean;
  accounts: PaymentAccountOptionDto[];
  /** Documents asked for with every deposit through this method, with their details. */
  requiredFiles: PaymentMethodDocumentDto[];
}

/** One document a payment method asks for, with the limits the server enforces on it. */
export interface PaymentMethodDocumentDto {
  id: string;
  name: string;
  isMandatory: boolean;
  maxSizeBytes: number;
  maxFiles: number;
  fields: RequiredFileFieldDto[];
  allowedExtensions: string[];
  samples: RequiredFileSampleDto[];
}

export interface WalletRequestFileDto {
  id: string;
  fileName: string;
  contentType: string;
  sizeBytes: number;
  createdAtUtc: string;
  /** The payment method document this file was sent for; null for general proof of transfer. */
  requiredFileId: string | null;
  documentName: string | null;
}

/** A detail the applicant filled in beside one of the method's documents. */
export interface WalletRequestDocumentValueDto {
  requiredFileId: string;
  documentName: string;
  fieldName: string;
  value: string;
}

export interface WalletRequestDto {
  id: string;
  orderId: string;
  orderNumber: string;
  type: WalletRequestType;
  typeName: string;
  status: WalletRequestStatus;
  statusName: string;
  amount: number;
  currencyCode: string;
  applicantNote: string | null;
  reviewerNote: string | null;
  requestedByName: string | null;
  reviewedByName: string | null;
  reviewedAtUtc: string | null;
  createdAtUtc: string;
  /** Server-decided, so the UI never re-derives the state machine. */
  canCancel: boolean;
  /** Populated only for a deposit raised through a payment method. */
  paymentMethodId: string | null;
  paymentMethodName: string | null;
  paymentMethodTypeName: string | null;
  paymentMethodKind: PaymentMethodKind | null;
  paymentMethodAccountId: string | null;
  paymentAccountLabel: string | null;
  paymentAccountNumber: string | null;
  referenceNumber: string | null;
  /** What the reviewer actually found in the bank, where it differed from the claim. */
  confirmedAmount: number | null;
  confirmedReference: string | null;
  files: WalletRequestFileDto[];
  documentValues: WalletRequestDocumentValueDto[];
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasPrevious: boolean;
  hasNext: boolean;
}

/** One of the order's balances: one per currency its country offers. */
export interface WalletBalanceDto {
  currencyId: string;
  currencyCode: string;
  currencySymbol: string;
  currencyName: string;
  balance: number;
  /** The order's main currency — the one applications are first priced in. */
  isMain: boolean;
  /** Null until money has moved in this currency. */
  walletId: string | null;
}

export interface WalletStatementDto {
  /** The balance whose ledger is shown. */
  wallet: WalletSummaryDto;
  ledger: PagedResult<WalletTransactionDto>;
  features: WalletFeaturesDto;
  pendingRequests: WalletRequestDto[];
  /** Every balance the order can hold, main currency first. */
  balances: WalletBalanceDto[];
}

export const WALLET_PAGE_SIZE = 20;

/** Every balance, plus a page of one currency's ledger (the main currency when none is given). */
export function useWallet(page = 1, type?: WalletTransactionType, currencyId?: string) {
  return useQuery({
    queryKey: queryKeys.wallet(page, type, currencyId),
    queryFn: () =>
      apiClient.get<WalletStatementDto>('orders/me/wallet', {
        query: {
          page,
          pageSize: WALLET_PAGE_SIZE,
          ...(type === undefined ? {} : { type }),
          ...(currencyId ? { currencyId } : {}),
        },
      }),
    // Paging through the ledger should not blank the table between pages.
    placeholderData: (previous) => previous,
  });
}

/** The order's own request history, including the ones already decided. */
export function useWalletRequests(page = 1) {
  return useQuery({
    queryKey: queryKeys.walletRequests(page),
    queryFn: () =>
      apiClient.get<PagedResult<WalletRequestDto>>('orders/me/wallet/requests', {
        query: { page, pageSize: 10 },
      }),
  });
}

/** One request in full, for its details page. */
export function useWalletRequest(requestId: string) {
  return useQuery({
    queryKey: queryKeys.walletRequest(requestId),
    queryFn: () => apiClient.get<WalletRequestDto>(`orders/me/wallet/requests/${requestId}`),
    enabled: Boolean(requestId),
  });
}

export function fetchWalletRequestFile(requestId: string, fileId: string): Promise<Blob> {
  return apiClient.getBlob(`orders/me/wallet/requests/${requestId}/files/${fileId}`);
}

/**
 * Saves an uploaded receipt.
 *
 * Fetched through the API client rather than linked: the access token lives in memory, so a plain
 * href would arrive without it and 401.
 */
export async function downloadWalletRequestFile(
  requestId: string,
  file: WalletRequestFileDto,
): Promise<void> {
  const blob = await fetchWalletRequestFile(requestId, file.id);
  const url = URL.createObjectURL(blob);

  const link = document.createElement('a');
  link.href = url;
  link.download = file.fileName;
  document.body.append(link);
  link.click();
  link.remove();

  // Revoked on the next tick: doing it synchronously can beat the click in some browsers.
  setTimeout(() => URL.revokeObjectURL(url), 0);
}

/** Everything a wallet mutation touches: the balance, the ledger, and the request list. */
function useWalletInvalidation() {
  const queryClient = useQueryClient();

  return () => {
    void queryClient.invalidateQueries({ queryKey: ['wallet'] });
    void queryClient.invalidateQueries({ queryKey: ['wallet-requests'] });
  };
}

export function useCreateWalletRequest() {
  const invalidate = useWalletInvalidation();

  return useMutation({
    mutationFn: (body: { type: WalletRequestType; amount: number; note?: string; currencyId?: string }) =>
      apiClient.post<WalletRequestDto>('orders/me/wallet/requests', {
        type: body.type,
        amount: body.amount,
        note: body.note?.trim() || null,
        currencyId: body.currencyId ?? null,
      }),
    onSuccess: invalidate,
  });
}

/**
 * The methods this order may pay through in a currency (its main one when none is given). Empty
 * until the order's setup is finished.
 */
export function usePaymentMethods(currencyId?: string) {
  return useQuery({
    queryKey: queryKeys.paymentMethods(currencyId),
    queryFn: () =>
      apiClient.get<PaymentMethodOptionDto[]>('orders/me/payment-methods', {
        query: currencyId ? { currencyId } : {},
      }),
  });
}

export interface CreateDepositBody {
  /** Which balance the money is for; the order's main currency when omitted. */
  currencyId?: string;
  paymentMethodId: string;
  paymentMethodAccountId: string | null;
  amount: number;
  note: string | null;
  files: File[];
  /** Files per payment method document, keyed by the document's id. */
  documents?: Record<string, File[]>;
  /** Answers to the documents' fields, keyed by field id. */
  values?: Record<string, string>;
}

/**
 * Raises a top-up through a payment method. Multipart rather than JSON because the receipt is part
 * of the request: the server refuses a claim that arrives without the proof its type demands, so
 * uploading afterwards would leave a window where neither is true.
 */
export function useCreateDepositRequest() {
  const invalidate = useWalletInvalidation();

  return useMutation({
    mutationFn: (body: CreateDepositBody) => {
      const form = new FormData();
      form.append('paymentMethodId', body.paymentMethodId);
      if (body.paymentMethodAccountId) {
        form.append('paymentMethodAccountId', body.paymentMethodAccountId);
      }
      form.append('amount', String(body.amount));
      if (body.currencyId) form.append('currencyId', body.currencyId);
      if (body.note) form.append('note', body.note);
      for (const file of body.files) form.append('files', file);

      let index = 0;
      for (const [requiredFileId, files] of Object.entries(body.documents ?? {})) {
        if (files.length === 0) continue;
        form.append(`documents[${index}].requiredFileId`, requiredFileId);
        for (const file of files) form.append(`documents[${index}].files`, file);
        index++;
      }

      index = 0;
      for (const [fieldId, value] of Object.entries(body.values ?? {})) {
        if (value.trim() === '') continue;
        form.append(`values[${index}].fieldId`, fieldId);
        form.append(`values[${index}].value`, value.trim());
        index++;
      }

      return apiClient.post<WalletRequestDto>('orders/me/wallet/requests/deposit', form);
    },
    onSuccess: invalidate,
  });
}

export function useCancelWalletRequest() {
  const invalidate = useWalletInvalidation();

  return useMutation({
    mutationFn: (requestId: string) =>
      apiClient.post<WalletRequestDto>(`orders/me/wallet/requests/${requestId}/cancel`, {}),
    onSuccess: invalidate,
  });
}

/**
 * Credits the wallet outright, with no operator in the loop. The endpoint exists only where the
 * server enables it, which is why the control that calls this is gated on `simulatedDepositsEnabled`.
 */
export function useSimulateDeposit() {
  const invalidate = useWalletInvalidation();

  return useMutation({
    mutationFn: ({ amount, currencyId }: { amount: number; currencyId?: string }) =>
      apiClient.post<WalletSummaryDto>('orders/me/wallet/simulate-deposit', {
        amount,
        currencyId: currencyId ?? null,
      }),
    onSuccess: invalidate,
  });
}
