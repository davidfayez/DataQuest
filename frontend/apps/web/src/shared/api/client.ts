import { HttpClient } from '@dv/api-client';
import { QueryClient } from '@tanstack/react-query';
import { ApiError } from '@dv/api-client';
import { env } from '../config/env';
import { getActiveLanguage } from '../config/i18n';
import { sessionStore } from '@/features/auth/session';

/**
 * The single API client for the app. Auth headers, locale negotiation and 401 handling live here
 * rather than at call sites, so no feature can accidentally talk to the API unauthenticated.
 */
export const apiClient = new HttpClient({
  baseUrl: env.apiBaseUrl,
  getAccessToken: () => sessionStore.getAccessToken(),
  getLanguage: () => getActiveLanguage(),
  onUnauthorized: () => sessionStore.clear(),
});

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 30_000,
      refetchOnWindowFocus: false,
      retry: (failureCount, error) => {
        // Retrying a rejected request only produces the same rejection more slowly.
        if (error instanceof ApiError && error.status < 500) return false;
        return failureCount < 2;
      },
    },
    mutations: {
      retry: false,
    },
  },
});

/**
 * Query keys in one place, so a mutation can invalidate exactly what it affected.
 *
 * Anything the server localizes is keyed by language as well as by id. The API resolves lookup
 * names from `Accept-Language`, so without the locale in the key a cached English response would
 * still be served after the user switches to Arabic.
 */
export const queryKeys = {
  countries: (lang: string) => ['countries', lang] as const,
  addressees: (lang: string) => ['addressees', lang] as const,
  currencies: (countryId: string, lang: string) =>
    ['countries', countryId, 'currencies', lang] as const,
  transactionTypes: (lang: string) => ['transaction-types', lang] as const,
  subTransactionTypes: (transactionTypeId: string, lang: string) =>
    ['transaction-types', transactionTypeId, 'sub-types', lang] as const,
  authorities: (subTypeId: string, lang: string) =>
    ['sub-types', subTypeId, 'authorities', lang] as const,
  serviceTypes: (authorityId: string, subTransactionTypeId: string, lang: string) =>
    ['authorities', authorityId, 'service-types', subTransactionTypeId, lang] as const,
  applications: (filters?: unknown) => ['applications', filters ?? {}] as const,
  applicationFilters: (lang: string) => ['applications', 'filters', lang] as const,
  application: (id: string) => ['applications', id] as const,
  timeline: (id: string) => ['applications', id, 'timeline'] as const,
  statusLog: (id: string) => ['applications', id, 'status-log'] as const,
  changeLog: (id: string, page: number) => ['applications', id, 'change-log', page] as const,
  results: (id: string) => ['applications', id, 'results'] as const,
  wallet: (page?: number, type?: number, currencyId?: string) =>
    ['wallet', page ?? 1, type ?? 'all', currencyId ?? 'main'] as const,
  walletRequests: (page?: number) => ['wallet-requests', page ?? 1] as const,
  // Under the same prefix as the list, so one wallet mutation invalidates both.
  walletRequest: (id: string) => ['wallet-requests', 'detail', id] as const,
  paymentMethods: (currencyId?: string) => ['payment-methods', currencyId ?? 'main'] as const,
};
