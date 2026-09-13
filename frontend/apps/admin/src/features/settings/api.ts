import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/shared/api/client';
import type { PagedResult, SortParams } from '@/shared/ui/DataTable';

/** Where the key the platform would send with is coming from. */
export type EmailKeySource = 'None' | 'Database' | 'Configuration' | 'Unreadable';

export interface EmailSettingsDto {
  isConfigured: boolean;
  /** `SG.••••••••AbCd` — enough to recognise the key, never enough to use it. */
  maskedApiKey: string | null;
  source: EmailKeySource;
  /** False when the server has no encryption key, so the form explains rather than fails. */
  canEdit: boolean;
}

export interface UpdateEmailSettingsBody {
  /** Null or empty clears the stored key and falls back to configuration. */
  apiKey: string | null;
}

const emailSettingsKey = ['admin', 'settings', 'email'] as const;

export function useEmailSettings() {
  return useQuery({
    queryKey: emailSettingsKey,
    queryFn: () => apiClient.get<EmailSettingsDto>('admin/settings/email'),
  });
}

/** The response is the refreshed settings, so it seeds the cache instead of forcing a refetch. */
export function useUpdateEmailSettings() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (body: UpdateEmailSettingsBody) =>
      apiClient.put<EmailSettingsDto>('admin/settings/email', body),
    onSuccess: (result) => queryClient.setQueryData(emailSettingsKey, result),
  });
}

// ------------------------------------------------------- Email routing

/** Mirrors the API's EmailType. */
export type EmailTypeName = 'OrderCreated' | 'ForgotPassword' | 'ContactUs';

export interface EmailBccRecipientDto {
  id: string;
  email: string;
  displayName: string | null;
}

export interface EmailRoutingDto {
  type: number;
  typeName: EmailTypeName;
  /** Null falls back to the sender in server configuration. */
  fromAddress: string | null;
  fromName: string | null;
  /** False for a kind nothing sends yet, so the page can say so. */
  isWired: boolean;
  bcc: EmailBccRecipientDto[];
  /** What this kind will actually be sent from — the override, or the platform default. */
  effectiveFromAddress: string;
  effectiveFromName: string | null;
  /** True when nothing here overrides the platform default. */
  usesDefaultSender: boolean;
}

/** Whether the platform can send email at all, ahead of any per-type configuration. */
export interface EmailDeliveryStatusDto {
  /** 'SendGrid' | 'Smtp' | 'Log' — Log means nothing leaves the machine. */
  transport: string;
  defaultFromAddress: string;
  defaultFromName: string | null;
  canSend: boolean;
  /** A stable code the page translates, e.g. 'NoApiKey:None' or 'LogOnly'. */
  reason: string | null;
}

export interface TestEmailResultDto {
  delivered: boolean;
  /** 'Delivered' | 'Logged' | 'NoApiKey' | 'Rejected' | 'Failed'. */
  status: string;
  /** The transport's own words, where there are any. */
  detail: string | null;
  fromAddress: string;
  to: string;
}

export interface EmailBccRecipientInput {
  id?: string | null;
  email: string;
  displayName: string | null;
}

export interface SaveEmailRoutingBody {
  type: number;
  fromAddress: string | null;
  fromName: string | null;
  bcc: EmailBccRecipientInput[];
}

const emailRoutingKey = ['admin', 'settings', 'email-routing'] as const;

export function useEmailRouting() {
  return useQuery({
    queryKey: emailRoutingKey,
    queryFn: () => apiClient.get<EmailRoutingDto[]>('admin/settings/email-routing'),
  });
}

/**
 * Whether email can be delivered at all.
 *
 * Its own query because it answers a different question from the routing list: not "who is this
 * from" but "will anything arrive". A missing provider key otherwise fails silently, which looks
 * exactly like a broken feature.
 */
export function useEmailDeliveryStatus() {
  return useQuery({
    queryKey: ['admin', 'settings', 'email-delivery'],
    queryFn: () => apiClient.get<EmailDeliveryStatusDto>('admin/settings/email-delivery'),
  });
}

/** The sender every kind of email falls back to when it sets no override of its own. */
export interface EmailDefaultSenderDto {
  fromAddress: string;
  fromName: string | null;
  /** 'Database' when an operator set it here, 'Configuration' when it is the deployed value. */
  source: string;
  /** What the transport would fall back to if the stored default were cleared. */
  configuredFromAddress: string;
  configuredFromName: string | null;
}

const emailDefaultSenderKey = ['admin', 'settings', 'email-default-sender'];

export function useEmailDefaultSender() {
  return useQuery({
    queryKey: emailDefaultSenderKey,
    queryFn: () => apiClient.get<EmailDefaultSenderDto>('admin/settings/email-default-sender'),
  });
}

export function useSaveEmailDefaultSender() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (body: { fromAddress: string | null; fromName: string | null }) =>
      apiClient.put<EmailDefaultSenderDto>('admin/settings/email-default-sender', body),
    // The banner and every per-kind panel show the fallback, so all of them are now stale.
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['admin', 'settings'] }),
  });
}

/** Sends a real test email down the same path a live one takes, and reports what happened. */
export function useSendTestEmail() {
  return useMutation({
    mutationFn: (body: { type: number; to: string }) =>
      apiClient.post<TestEmailResultDto>('admin/settings/email-routing/test', body),
  });
}

export function useSaveEmailRouting() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (body: SaveEmailRoutingBody) =>
      apiClient.put<EmailRoutingDto>('admin/settings/email-routing', body),
    // One kind at a time is saved, so the whole list is refetched rather than patched in place.
    onSuccess: () => queryClient.invalidateQueries({ queryKey: emailRoutingKey }),
  });
}

/**
 * The AI assistant's settings. The key is never returned by the API — only whether one is stored —
 * so this type carries a flag, not a value.
 */
export interface AiSettingsDto {
  isEnabled: boolean;
  model: string;
  hasApiKey: boolean;
  /** False when the platform has no encryption key, so a provider key cannot be stored at all. */
  canStore: boolean;
}

export interface UpdateAiSettingsBody {
  isEnabled: boolean;
  model: string;
  /** Blank leaves the stored key alone, which is how the switch and model are saved on their own. */
  apiKey?: string;
  /** Removes the stored key, since blank means "keep". */
  clearApiKey?: boolean;
}

const aiKey = ['admin', 'settings', 'ai'] as const;

export function useAiSettings() {
  return useQuery({
    queryKey: aiKey,
    queryFn: () => apiClient.get<AiSettingsDto>('admin/settings/ai'),
  });
}

export function useSaveAiSettings() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (body: UpdateAiSettingsBody) =>
      apiClient.put<AiSettingsDto>('admin/settings/ai', body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: aiKey }),
  });
}

/** One press of "forgot password", as the security log shows it. */
export interface PasswordResetLogEntry {
  id: string;
  orderNumber: string;
  orderId: string | null;
  maskedEmail: string | null;
  ipAddress: string | null;
  country: string | null;
  countryCode: string | null;
  city: string | null;
  continent: string | null;
  continentCode: string | null;
  /** The region's code, such as `TAS`. */
  region: string | null;
  /** The region's name, such as `Tashkent`. */
  regionName: string | null;
  district: string | null;
  postalCode: string | null;
  latitude: number | null;
  longitude: number | null;
  timeZone: string | null;
  /** Offset from UTC in seconds. */
  utcOffsetSeconds: number | null;
  currency: string | null;
  /** Who sells the connection. */
  isp: string | null;
  /** Who the address is registered to, often not the ISP. */
  organisation: string | null;
  /** The autonomous system, such as `AS15169 Google LLC`. */
  autonomousSystem: string | null;
  /**
   * The reverse-DNS name of the address — as close to a machine name as a web server can get, and
   * usually the ISP's name for the line rather than the visitor's own computer.
   */
  reverseDns: string | null;
  isMobileNetwork: boolean | null;
  /** A known proxy, VPN or Tor exit. */
  isProxy: boolean | null;
  /** A data centre rather than a home or office connection. */
  isHosting: boolean | null;
  browser: string | null;
  operatingSystem: string | null;
  deviceType: string | null;
  userAgent: string | null;
  acceptLanguage: string | null;
  languageCode: string | null;
  validityMinutes: number;
  expiresAtUtc: string | null;
  usedAtUtc: string | null;
  /** Pending, Used, Expired, Superseded or UnknownOrder. */
  outcome: string;
  requestedAtUtc: string;
}

export interface PasswordResetListParams extends SortParams {
  page?: number;
  pageSize?: number;
  search?: string;
  onlyUnknownOrders?: boolean;
}

export interface PasswordResetSettingsDto {
  validityMinutes: number;
  minMinutes: number;
  maxMinutes: number;
}

const resetLogKey = (params: PasswordResetListParams) =>
  ['admin', 'settings', 'password-resets', params] as const;

export function usePasswordResetLog(params: PasswordResetListParams) {
  return useQuery({
    queryKey: resetLogKey(params),
    queryFn: () =>
      apiClient.get<PagedResult<PasswordResetLogEntry>>('admin/settings/password-resets', {
        query: {
          page: params.page,
          pageSize: params.pageSize,
          sortBy: params.sortBy,
          sortDescending: params.sortDescending,
          search: params.search,
          onlyUnknownOrders: params.onlyUnknownOrders,
        },
      }),
  });
}

const resetValidityKey = ['admin', 'settings', 'password-reset-validity'] as const;

export function usePasswordResetValidity() {
  return useQuery({
    queryKey: resetValidityKey,
    queryFn: () =>
      apiClient.get<PasswordResetSettingsDto>('admin/settings/password-reset-validity'),
  });
}

export function useSavePasswordResetValidity() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (validityMinutes: number) =>
      apiClient.put<PasswordResetSettingsDto>('admin/settings/password-reset-validity', {
        validityMinutes,
      }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: resetValidityKey }),
  });
}
