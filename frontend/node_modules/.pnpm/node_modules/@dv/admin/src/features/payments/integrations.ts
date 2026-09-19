import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { DeleteOutcome, ListParams } from '@/features/lookups/api';
import { apiClient } from '@/shared/api/client';
import { useLanguage } from '@/shared/lib/useLanguage';
import type { PagedResult } from '@/shared/ui/DataTable';
import { PaymentIntegrationMode } from './api';

const BASE = 'admin/payment-integrations';

/** Mirrors GatewayFieldType on the server. */
export const GatewayFieldType = {
  Text: 0,
  Secret: 1,
  Url: 2,
  Email: 3,
  Number: 4,
  Select: 5,
  Boolean: 6,
} as const;

export interface PaymentGatewayField {
  key: string;
  label: string;
  hint: string | null;
  type: number;
  typeName: string;
  isRequired: boolean;
  pattern: string | null;
  placeholder: string | null;
  multiline: boolean;
  maxLength: number;
  options: { value: string; label: string }[];
}

export interface PaymentGateway {
  code: string;
  name: string;
  category: number;
  /** Global, MiddleEastAfrica, AsiaLatinAmerica, BankTransfer, BuyNowPayLater, Wallet, Other. */
  categoryName: string;
  region: string;
  website: string | null;
  fields: PaymentGatewayField[];
}

export interface PaymentGatewayIntegrationDto {
  id: string;
  name: string;
  nameAr: string;
  nameEn: string;
  description: string | null;
  descriptionAr: string | null;
  descriptionEn: string | null;
  gatewayCode: string;
  gatewayName: string;
  gatewayCategory: number | null;
  mode: number;
  modeName: string;
  sortOrder: number;
  isActive: boolean;
  /** How many payment methods pay through this integration. */
  methodCount: number;
}

export interface UpsertPaymentGatewayIntegrationBody {
  id?: string | null;
  nameAr: string;
  nameEn: string;
  descriptionAr: string;
  descriptionEn: string;
  gatewayCode: string;
  mode: number;
  sortOrder: number;
  isActive: boolean;
}

export const EMPTY_INTEGRATION_BODY: UpsertPaymentGatewayIntegrationBody = {
  nameAr: '',
  nameEn: '',
  descriptionAr: '',
  descriptionEn: '',
  gatewayCode: '',
  mode: PaymentIntegrationMode.Sandbox,
  sortOrder: 0,
  isActive: true,
};

const keys = {
  all: ['admin', 'payment-integrations'] as const,
  gateways: (lang: string) => ['admin', 'payment-integrations', 'gateways', lang] as const,
  list: (params: unknown, lang: string) => ['admin', 'payment-integrations', 'list', params, lang] as const,
  one: (id: string, lang: string) => ['admin', 'payment-integrations', 'one', id, lang] as const,
  secret: (methodId: string, key: string) =>
    ['admin', 'payment-methods', 'secret', methodId, key] as const,
};

/** The gateway catalogue. It only changes with a deployment, so it is kept for the session. */
export function usePaymentGateways() {
  const language = useLanguage();
  return useQuery({
    queryKey: keys.gateways(language),
    queryFn: () => apiClient.get<PaymentGateway[]>(`${BASE}/gateways`, { language }),
    staleTime: Infinity,
  });
}

export function usePaymentGatewayIntegrations(params: ListParams & { gatewayCode?: string }) {
  const language = useLanguage();
  return useQuery({
    queryKey: keys.list(params, language),
    queryFn: () =>
      apiClient.get<PagedResult<PaymentGatewayIntegrationDto>>(BASE, {
        query: {
          page: params.page ?? 1,
          pageSize: params.pageSize ?? 25,
          sortBy: params.sortBy,
          sortDescending: params.sortDescending,
          search: params.search || undefined,
          isActive: params.isActive,
          gatewayCode: params.gatewayCode || undefined,
        },
        language,
      }),
    placeholderData: (previous) => previous,
  });
}

export function usePaymentGatewayIntegration(id: string | undefined) {
  const language = useLanguage();
  return useQuery({
    queryKey: keys.one(id ?? '', language),
    queryFn: () => apiClient.get<PaymentGatewayIntegrationDto>(`${BASE}/${id}`, { language }),
    enabled: Boolean(id),
    // Refetching on focus would overwrite whatever is being typed into the form.
    refetchOnWindowFocus: false,
  });
}

/**
 * One of a payment method's stored gateway secrets, in full, for the eye button. Fetched only when
 * asked for, never kept once the field is gone, and never refetched behind the admin's back.
 */
export function useMethodGatewaySecret(methodId: string | undefined, key: string, enabled: boolean) {
  return useQuery({
    queryKey: keys.secret(methodId ?? '', key),
    queryFn: async () =>
      (await apiClient.get<{ key: string; value: string | null }>(
        `admin/payment-methods/${methodId}/secrets/${encodeURIComponent(key)}`,
      )).value ?? '',
    enabled: enabled && Boolean(methodId),
    gcTime: 0,
    staleTime: Infinity,
    refetchOnWindowFocus: false,
  });
}

function useInvalidateIntegrations() {
  const queryClient = useQueryClient();
  return () => {
    void queryClient.invalidateQueries({ queryKey: keys.all });
    // Method pages show which integration they use.
    void queryClient.invalidateQueries({ queryKey: ['admin', 'payment-methods'] });
  };
}

export function useSavePaymentGatewayIntegration() {
  const invalidate = useInvalidateIntegrations();
  return useMutation({
    mutationFn: (body: UpsertPaymentGatewayIntegrationBody) =>
      apiClient.post<PaymentGatewayIntegrationDto>(BASE, body),
    onSuccess: invalidate,
  });
}

export function useDeletePaymentGatewayIntegration() {
  const invalidate = useInvalidateIntegrations();
  return useMutation({
    mutationFn: (id: string) => apiClient.delete<DeleteOutcome>(`${BASE}/${id}`),
    onSuccess: invalidate,
  });
}
