import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { DeleteOutcome, ListParams, RequiredFileDto } from '@/features/lookups/api';
import type { RequiredFileInput } from '@/pages/lookups/tabs/RequiredDocumentsEditor';
import { adminKeys, apiClient } from '@/shared/api/client';
import { useLanguage } from '@/shared/lib/useLanguage';
import type { PagedResult } from '@/shared/ui/DataTable';

/**
 * Mirrors the API's PaymentMethodKind: how money reaches the platform, and nothing more.
 *
 * What a method must be configured with is no longer implied by this — it is the four `requires*`
 * switches on the type, which combine freely.
 */
export enum PaymentMethodKind {
  /** The applicant sends money to us and says so; a reviewer confirms it before crediting. */
  Transfer = 1,
  /** The applicant pays through PayPal, configured with provider credentials on the method. */
  PayPal = 4,
}

/** True where money arrives out of band and a reviewer confirms it. */
export const isTransferKind = (kind: PaymentMethodKind): boolean =>
  kind === PaymentMethodKind.Transfer;

/**
 * A payment type. The four `requires*` flags are the whole contract between this catalogue and
 * every form in both apps — the method editor renders the fields they demand, and the applicant's
 * deposit form validates against the same four.
 */
export interface PaymentMethodTypeDto {
  id: string;
  name: string;
  nameAr: string;
  nameEn: string;
  /** The description in the current language, falling back to the other. */
  description: string | null;
  descriptionAr: string | null;
  descriptionEn: string | null;
  isActive: boolean;
  /** Read-only: a new type is a transfer, and an existing type keeps its kind. */
  kind: PaymentMethodKind;
  kindName: string;
  /** Receiving numbers the applicant picks from, and pays. */
  requiresAccountNumber: boolean;
  /** Every receiving number carries a scannable QR code. */
  requiresBarcode: boolean;
  /** Every receiving row names the bank it belongs to. */
  requiresBank: boolean;
  /** The method carries an address applicants are sent to in order to pay. */
  requiresExternalUrl: boolean;
  /**
   * The method talks to a payment provider, so its editor offers the credentials panel. Sent by
   * the server rather than worked out here: the command that stores credentials reads the same
   * property, and when the two were separate judgements they drifted.
   */
  usesProviderCredentials: boolean;
  requiresProofDocument: boolean;
  requiresReferenceNumber: boolean;
  /** True for transfer types: money arrives out of band and a reviewer confirms it. */
  needsApproval: boolean;
  sortOrder: number;
  /** How many methods use this type; a type in use is deactivated rather than deleted. */
  methodCount: number;
}

export interface PaymentMethodAccountDto {
  id: string;
  label: string;
  labelAr: string;
  labelEn: string;
  accountNumber: string;
  accountHolder: string | null;
  bankId: string | null;
  bankName: string | null;
  hasBarcode: boolean;
  barcodeFileName: string | null;
  isActive: boolean;
  sortOrder: number;
}

/** One mailbox told about activity on a payment method. */
export interface PaymentNotificationEmailDto {
  id: string;
  email: string;
  displayName: string | null;
  notifyOnSubmitted: boolean;
  notifyOnApproved: boolean;
  notifyOnRejected: boolean;
}

export interface PaymentNotificationEmailInput {
  id?: string | null;
  email: string;
  displayName: string | null;
  notifyOnSubmitted: boolean;
  notifyOnApproved: boolean;
  notifyOnRejected: boolean;
}

/** Mirrors the API's PaymentIntegrationMode. */
export enum PaymentIntegrationMode {
  Sandbox = 0,
  Live = 1,
}

/**
 * A method's provider integration as the admin page is allowed to see it.
 *
 * Carries no secret — only whether each one is set. There is deliberately no way to read a stored
 * credential back out of the API, so the editor shows "configured" and offers to replace it.
 */
export interface PaymentIntegrationDto {
  provider: string | null;
  mode: PaymentIntegrationMode;
  modeName: string;
  merchantId: string | null;
  integrationId: string | null;
  hasApiKey: boolean;
  hasPassword: boolean;
  hasWebhookSecret: boolean;
  baseUrl: string | null;
  redirectUrl: string | null;
  cancelUrl: string | null;
  callbackUrl: string | null;
  sessionTimeoutMinutes: number | null;
  /** True when a callback is configured that nothing can be verified against. */
  callbackIsUnverified: boolean;
}

/**
 * The integration as the editor sends it.
 *
 * The three secrets are three-state on purpose: `null` leaves what is stored alone, `''` clears it,
 * and a value replaces it. An untouched field must send `null`, or saving an unrelated change
 * would wipe credentials the page never had the chance to display.
 */
export interface PaymentIntegrationInput {
  provider: string | null;
  mode: PaymentIntegrationMode;
  merchantId: string | null;
  integrationId: string | null;
  apiKey: string | null;
  password: string | null;
  webhookSecret: string | null;
  baseUrl: string | null;
  redirectUrl: string | null;
  cancelUrl: string | null;
  callbackUrl: string | null;
  sessionTimeoutMinutes: number | null;
}

export interface AdminPaymentMethodDto {
  id: string;
  name: string;
  nameAr: string;
  nameEn: string;
  isActive: boolean;
  paymentMethodTypeId: string;
  typeName: string;
  kind: PaymentMethodKind;
  kindName: string;
  descriptionAr: string | null;
  descriptionEn: string | null;
  publicNoteAr: string | null;
  publicNoteEn: string | null;
  privateNoteAr: string | null;
  privateNoteEn: string | null;
  externalUrl: string | null;
  /** What this method's type asks for, so a row can say what paying it looks like. */
  requiresAccountNumber: boolean;
  requiresBarcode: boolean;
  requiresBank: boolean;
  requiresExternalUrl: boolean;
  usesProviderCredentials: boolean;
  sortOrder: number;
  countryIds: string[];
  currencyIds: string[];
  accounts: PaymentMethodAccountDto[];
  notificationEmails: PaymentNotificationEmailDto[];
  /** Null when the method is paid by hand rather than through a provider. */
  integration: PaymentIntegrationDto | null;
  /** Server-decided: whether the method is complete enough to appear in the applicant's picker. */
  isUsable: boolean;
  /** The documents an applicant uploads with every deposit through this method. */
  requiredFiles: RequiredFileDto[];
  /** The gateway integration the method pays through, from the Payment type integrations page. */
  gatewayIntegrationId: string | null;
  gatewayIntegrationName: string | null;
  gatewayCode: string | null;
  /** Sandbox or Live, as the chosen integration declares it. */
  gatewayModeName: string | null;
  /** This method's own settings for that gateway. */
  gatewaySettings: Record<string, string>;
  /** Which gateway secrets are stored; the values come from their own endpoint. */
  configuredGatewaySecrets: string[];
  /** False when the server has no encryption key, so gateway secrets cannot be saved. */
  canStoreGatewaySecrets: boolean;
}

/** One account as the editor submits it; an id present edits the row and keeps its barcode. */
export interface PaymentMethodAccountInput {
  id?: string | null;
  labelAr: string;
  labelEn: string;
  accountNumber: string;
  accountHolder: string | null;
  /** Required by a bank-transfer type; ignored by every other kind. */
  bankId: string | null;
  isActive: boolean;
  sortOrder: number;
}

export interface UpsertPaymentMethodBody {
  id?: string | null;
  paymentMethodTypeId: string;
  nameAr: string;
  nameEn: string;
  descriptionAr: string | null;
  descriptionEn: string | null;
  publicNoteAr: string | null;
  publicNoteEn: string | null;
  privateNoteAr: string | null;
  privateNoteEn: string | null;
  externalUrl: string | null;
  sortOrder: number;
  isActive: boolean;
  countryIds: string[];
  currencyIds: string[];
  accounts: PaymentMethodAccountInput[];
  notificationEmails: PaymentNotificationEmailInput[];
  /** Omitted entirely leaves any stored integration untouched. */
  integration?: PaymentIntegrationInput | null;
  /**
   * The documents asked for with every deposit. Omitted leaves the stored ones untouched; an empty
   * list removes them. Reference files are not sent here — they have their own endpoints.
   */
  requiredFiles?: RequiredFileInput[];
  /** Null for none, which also clears the settings saved for it. */
  gatewayIntegrationId?: string | null;
  /** The chosen gateway's settings, replaced outright. */
  gatewaySettings?: Record<string, string>;
  /** A key left out keeps what is stored, an empty value removes it, anything else replaces it. */
  gatewaySecrets?: Record<string, string>;
}

/**
 * No kind: a new type is a transfer, and an existing type keeps the kind it has.
 */
export interface UpsertPaymentMethodTypeBody {
  id?: string | null;
  nameAr: string;
  nameEn: string;
  /** Required in both languages. */
  descriptionAr: string;
  descriptionEn: string;
  requiresAccountNumber: boolean;
  requiresBarcode: boolean;
  requiresBank: boolean;
  requiresExternalUrl: boolean;
  requiresProofDocument: boolean;
  requiresReferenceNumber: boolean;
  sortOrder: number;
  isActive: boolean;
}

/** A bank in the catalogue, scoped to the country it operates in. */
export interface BankDto {
  id: string;
  countryId: string;
  countryName: string;
  name: string;
  nameAr: string;
  nameEn: string;
  swiftCode: string | null;
  sortOrder: number;
  isActive: boolean;
}

export interface UpsertBankBody {
  id?: string | null;
  countryId: string;
  nameAr: string;
  nameEn: string;
  swiftCode: string | null;
  sortOrder: number;
  isActive: boolean;
}

export interface PaymentMethodListParams extends ListParams {
  paymentMethodTypeId?: string;
  kind?: PaymentMethodKind;
}

const BASE = 'admin/payment-methods';

export function usePaymentMethods(params: PaymentMethodListParams) {
  const lang = useLanguage();

  return useQuery({
    queryKey: adminKeys.paymentMethods(params, lang),
    queryFn: () =>
      apiClient.get<PagedResult<AdminPaymentMethodDto>>(BASE, {
        query: {
          page: params.page ?? 1,
          pageSize: params.pageSize ?? 25,
          sortBy: params.sortBy,
          sortDescending: params.sortDescending,
          search: params.search || undefined,
          isActive: params.isActive,
          countryId: params.countryId,
          paymentMethodTypeId: params.paymentMethodTypeId,
          kind: params.kind,
        },
        language: lang,
      }),
  });
}

/**
 * One method with its whole configuration. The editor opens on this rather than hunting the row
 * out of a list page, so a bookmarked edit URL works and the account ids are always current.
 */
export function usePaymentMethod(id: string | undefined) {
  const lang = useLanguage();

  return useQuery({
    queryKey: adminKeys.paymentMethod(id ?? 'none', lang),
    queryFn: () =>
      apiClient.get<AdminPaymentMethodDto>(`${BASE}/${id}`, { language: lang }),
    enabled: Boolean(id),
  });
}

export function usePaymentMethodTypes(params: ListParams & { kind?: PaymentMethodKind }) {
  const lang = useLanguage();

  return useQuery({
    queryKey: adminKeys.paymentMethodTypes(params, lang),
    queryFn: () =>
      apiClient.get<PagedResult<PaymentMethodTypeDto>>(`${BASE}/types`, {
        query: {
          page: params.page ?? 1,
          pageSize: params.pageSize ?? 25,
          sortBy: params.sortBy,
          sortDescending: params.sortDescending,
          search: params.search || undefined,
          isActive: params.isActive,
          kind: params.kind,
        },
        language: lang,
      }),
  });
}

/** Everything a payment mutation touches; the two lists share a cache root. */
function useInvalidatePayments() {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: ['admin'] });
}

export function useSavePaymentMethod() {
  const invalidate = useInvalidatePayments();

  return useMutation({
    mutationFn: (body: UpsertPaymentMethodBody) =>
      apiClient.post<AdminPaymentMethodDto>(BASE, body),
    onSuccess: invalidate,
  });
}

export function useDeletePaymentMethod() {
  const invalidate = useInvalidatePayments();

  return useMutation({
    mutationFn: (id: string) => apiClient.delete<DeleteOutcome>(`${BASE}/${id}`),
    onSuccess: invalidate,
  });
}

export function useSavePaymentMethodType() {
  const invalidate = useInvalidatePayments();

  return useMutation({
    mutationFn: (body: UpsertPaymentMethodTypeBody) =>
      apiClient.post<PaymentMethodTypeDto>(`${BASE}/types`, body),
    onSuccess: invalidate,
  });
}

export function useDeletePaymentMethodType() {
  const invalidate = useInvalidatePayments();

  return useMutation({
    mutationFn: (id: string) => apiClient.delete<DeleteOutcome>(`${BASE}/types/${id}`),
    onSuccess: invalidate,
  });
}

/**
 * Uploads a barcode against an account that already exists, which is why the editor saves the
 * method before it will accept one.
 */
export function useUploadAccountBarcode() {
  const invalidate = useInvalidatePayments();

  return useMutation({
    mutationFn: ({ accountId, file }: { accountId: string; file: File }) => {
      const form = new FormData();
      form.append('file', file);
      return apiClient.post<PaymentMethodAccountDto>(
        `${BASE}/accounts/${accountId}/barcode`,
        form,
      );
    },
    onSuccess: invalidate,
  });
}

export function useDeleteAccountBarcode() {
  const invalidate = useInvalidatePayments();

  return useMutation({
    mutationFn: (accountId: string) =>
      apiClient.delete<PaymentMethodAccountDto>(`${BASE}/accounts/${accountId}/barcode`),
    onSuccess: invalidate,
  });
}

/**
 * Fetches an account's barcode bytes.
 *
 * A plain `<img src>` cannot be used: the access token lives in memory rather than a cookie, so
 * the request would arrive unauthenticated and the endpoint would refuse it. Callers turn the blob
 * into an object URL and revoke it when they unmount.
 */
export function fetchAccountBarcode(accountId: string): Promise<Blob> {
  return apiClient.getBlob(`${BASE}/accounts/${accountId}/barcode`);
}

// ------------------------------------------------------------------- Banks

const BANKS = 'admin/banks';

/**
 * The bank catalogue. `countryId` narrows it the way the method editor needs — a method may only
 * name banks from the countries it serves.
 */
export function useBanks(params: ListParams & { countryId?: string; enabled?: boolean }) {
  const lang = useLanguage();

  return useQuery({
    queryKey: adminKeys.banks(params, lang),
    queryFn: () =>
      apiClient.get<PagedResult<BankDto>>(BANKS, {
        query: {
          page: params.page ?? 1,
          pageSize: params.pageSize ?? 25,
          sortBy: params.sortBy,
          sortDescending: params.sortDescending,
          search: params.search || undefined,
          isActive: params.isActive,
          countryId: params.countryId,
        },
        language: lang,
      }),
    enabled: params.enabled ?? true,
  });
}

export function useSaveBank() {
  const invalidate = useInvalidatePayments();

  return useMutation({
    mutationFn: (body: UpsertBankBody) => apiClient.post<BankDto>(BANKS, body),
    onSuccess: invalidate,
  });
}

export function useDeleteBank() {
  const invalidate = useInvalidatePayments();

  return useMutation({
    mutationFn: (id: string) => apiClient.delete<DeleteOutcome>(`${BANKS}/${id}`),
    onSuccess: invalidate,
  });
}
