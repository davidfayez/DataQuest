import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient, queryKeys } from '@/shared/api/client';
import { useLanguage } from '@/shared/lib/useLanguage';
import type { PagedResult } from '../wallet/api';
import type { ApplicationStatus } from './types';

export interface ApplicationListItemDto {
  id: string;
  applicationNumber: string;
  addressedTo: string;
  status: ApplicationStatus;
  statusName: string;
  isPaid: boolean;
  paidAtUtc: string | null;
  totalCost: number;
  currencyCode: string;
  serviceCount: number;
  createdAtUtc: string;
  /** Server-computed. The UI renders actions from these and never re-derives them. */
  canEdit: boolean;
  canDelete: boolean;
  canRefund: boolean;
  /** Both scripts, because the search matches either one. */
  applicantNameAr: string | null;
  applicantNameEn: string | null;
  transactionTypeName: string | null;
  subTransactionTypeName: string | null;
  verificationAuthorityName: string | null;
  serviceTypeNames: string[];
}

/**
 * The columns the server will sort on. Anything else falls back to newest-first.
 *
 * The purchased services are absent on purpose: a row may hold several, so there is no single
 * value to order on — that column renders unsorted.
 */
export const ApplicationSort = {
  ApplicationNumber: 'applicationNumber',
  AddressedTo: 'addressedTo',
  ApplicantName: 'applicantName',
  Status: 'status',
  TotalCost: 'totalCost',
  CreatedAt: 'createdAt',
  TransactionType: 'transactionType',
  SubTransactionType: 'subTransactionType',
  Authority: 'authority',
  ServiceCount: 'serviceCount',
  IsPaid: 'isPaid',
} as const;

export type ApplicationSortField = (typeof ApplicationSort)[keyof typeof ApplicationSort];

export interface ApplicationQuery {
  page: number;
  pageSize: number;
  search?: string;
  status?: ApplicationStatus;
  transactionTypeId?: string;
  subTransactionTypeId?: string;
  verificationAuthorityId?: string;
  serviceTypeId?: string;
  sortBy: ApplicationSortField;
  sortDescending: boolean;
}

export interface ApplicationFilterOptionDto {
  id: string;
  name: string;
  count: number;
}

export interface ApplicationStatusOptionDto {
  status: ApplicationStatus;
  statusName: string;
  count: number;
}

export interface ApplicationFiltersDto {
  statuses: ApplicationStatusOptionDto[];
  transactionTypes: ApplicationFilterOptionDto[];
  subTransactionTypes: ApplicationFilterOptionDto[];
  verificationAuthorities: ApplicationFilterOptionDto[];
  serviceTypes: ApplicationFilterOptionDto[];
}

export const APPLICATIONS_PAGE_SIZES = [10, 25, 50, 100] as const;

export function useMyApplications(query: ApplicationQuery) {
  const lang = useLanguage();

  return useQuery({
    queryKey: queryKeys.applications({ ...query, lang }),
    queryFn: () =>
      apiClient.get<PagedResult<ApplicationListItemDto>>('applications', {
        language: lang,
        query: {
          page: query.page,
          pageSize: query.pageSize,
          search: query.search || undefined,
          status: query.status,
          transactionTypeId: query.transactionTypeId,
          subTransactionTypeId: query.subTransactionTypeId,
          verificationAuthorityId: query.verificationAuthorityId,
          serviceTypeId: query.serviceTypeId,
          sortBy: query.sortBy,
          sortDescending: query.sortDescending,
        },
      }),
    // Sorting or paging should not blank the table while the next page loads.
    placeholderData: (previous) => previous,
  });
}

/**
 * The filter values the caller's applications actually contain. Server-derived, so every option
 * in the dropdowns returns at least one row.
 */
export function useApplicationFilters() {
  const lang = useLanguage();

  return useQuery({
    queryKey: queryKeys.applicationFilters(lang),
    queryFn: () => apiClient.get<ApplicationFiltersDto>('applications/filters', { language: lang }),
  });
}

export function useDeleteApplication() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (applicationId: string) => apiClient.delete<void>(`applications/${applicationId}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['applications'] }),
  });
}

export interface PaidApplicationDto {
  applicationId: string;
  applicationNumber: string;
  amount: number;
  status: ApplicationStatus;
  paidAtUtc: string;
}

export interface PaymentResultDto {
  transactionId: string;
  amountPaid: number;
  balanceAfter: number;
  currencyCode: string;
  applications: PaidApplicationDto[];
}

/** Settles several applications in one wallet transaction; the server makes it all-or-nothing. */
export function usePayApplications() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (applicationIds: string[]) =>
      apiClient.post<PaymentResultDto>('payments', { applicationIds }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['applications'] });
      void queryClient.invalidateQueries({ queryKey: ['wallet'] });
    },
  });
}

export interface RefundResultDto {
  transactionId: string;
  applicationId: string;
  applicationNumber: string;
  amountRefunded: number;
  balanceAfter: number;
  status: ApplicationStatus;
}

export function useRefundApplication() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ applicationId, note }: { applicationId: string; note?: string }) =>
      apiClient.post<RefundResultDto>(`applications/${applicationId}/refund`, { note: note ?? null }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['applications'] });
      void queryClient.invalidateQueries({ queryKey: ['wallet'] });
    },
  });
}
