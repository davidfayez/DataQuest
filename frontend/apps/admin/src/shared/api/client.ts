import { ApiError, HttpClient } from '@dv/api-client';
import { QueryClient } from '@tanstack/react-query';
import { env } from '../config/env';
import { getActiveLanguage } from '../config/i18n';
import { adminSession } from '@/features/auth/session';

export const apiClient = new HttpClient({
  baseUrl: env.apiBaseUrl,
  getAccessToken: () => adminSession.getAccessToken(),
  getLanguage: () => getActiveLanguage(),
  onUnauthorized: () => adminSession.clear(),
});

export const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      staleTime: 15_000,
      refetchOnWindowFocus: false,
      retry: (failureCount, error) => {
        if (error instanceof ApiError && error.status < 500) return false;
        return failureCount < 2;
      },
    },
    mutations: { retry: false },
  },
});

/**
 * Query keys. Anything the server localizes carries the language, otherwise switching between
 * Arabic and English would serve names cached under the previous locale.
 */
export const adminKeys = {
  profile: ['admin', 'profile'] as const,
  dashboard: ['admin', 'dashboard'] as const,
  countries: (params: unknown, lang: string) => ['admin', 'countries', params, lang] as const,
  currencies: (params: unknown, lang: string) => ['admin', 'currencies', params, lang] as const,
  addressees: (params: unknown, lang: string) => ['admin', 'addressees', params, lang] as const,
  countryCurrencies: (countryId: string, lang: string) =>
    ['admin', 'countries', countryId, 'currencies', lang] as const,
  transactionTypes: (params: unknown, lang: string) =>
    ['admin', 'transaction-types', params, lang] as const,
  subTransactionTypes: (params: unknown, lang: string) =>
    ['admin', 'sub-transaction-types', params, lang] as const,
  authorities: (params: unknown, lang: string) => ['admin', 'authorities', params, lang] as const,
  serviceTypes: (params: unknown, lang: string) => ['admin', 'service-types', params, lang] as const,
  paymentMethods: (params: unknown, lang: string) =>
    ['admin', 'payment-methods', params, lang] as const,
  paymentMethod: (id: string, lang: string) => ['admin', 'payment-methods', id, lang] as const,
  paymentMethodTypes: (params: unknown, lang: string) =>
    ['admin', 'payment-method-types', params, lang] as const,
  banks: (params: unknown, lang: string) => ['admin', 'banks', params, lang] as const,
  orders: (params: unknown) => ['admin', 'orders', params] as const,
  order: (id: string) => ['admin', 'orders', id] as const,
  orderWallet: (id: string) => ['admin', 'orders', id, 'wallet'] as const,
  walletRequests: (params: unknown) => ['admin', 'wallet-requests', params] as const,
  walletRequest: (id: string) => ['admin', 'wallet-requests', id] as const,
  walletRequestHistory: (id: string) =>
    ['admin', 'wallet-requests', id, 'history'] as const,
  applications: (params: unknown, lang: string) => ['admin', 'applications', params, lang] as const,
  application: (id: string, lang: string) => ['admin', 'applications', id, lang] as const,
  applicationTimeline: (id: string) => ['admin', 'applications', id, 'timeline'] as const,
  applicationAudit: (id: string) => ['admin', 'applications', id, 'audit'] as const,
  roles: ['admin', 'roles'] as const,
  permissions: ['admin', 'permissions'] as const,
  users: (params: unknown) => ['admin', 'users', params] as const,
  auditLog: (params: unknown) => ['admin', 'audit-log', params] as const,
  tickets: (params: unknown, lang: string) => ['admin', 'tickets', params, lang] as const,
  ticket: (id: string, lang: string) => ['admin', 'tickets', id, lang] as const,
  ticketAssignees: ['admin', 'tickets', 'assignees'] as const,
  ticketOrders: (search: string) => ['admin', 'tickets', 'orders', search] as const,
  ticketOrderApplications: (orderId: string) =>
    ['admin', 'tickets', 'orders', orderId, 'applications'] as const,
  ticketCategories: (params: unknown, lang: string) =>
    ['admin', 'ticket-categories', params, lang] as const,
};
