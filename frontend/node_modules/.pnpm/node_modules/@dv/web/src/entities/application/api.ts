import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient, queryKeys } from '@/shared/api/client';
import { useLanguage } from '@/shared/lib/useLanguage';
import type {
  ApplicationDetailsDto,
  ApplicationFileDto,
  ApplicationWriteModel,
  AuthorityDto,
  ServiceTypeDto,
  SubTransactionTypeDto,
  TransactionTypeDto,
} from './types';

// ------------------------------------------------------------------ cascade
// Every lookup query is keyed by language, because the server resolves the display name from
// Accept-Language and a cached bundle from another locale would otherwise be reused.

export function useTransactionTypes() {
  const lang = useLanguage();

  return useQuery({
    queryKey: queryKeys.transactionTypes(lang),
    queryFn: () => apiClient.get<TransactionTypeDto[]>('transaction-types', { language: lang }),
  });
}

export function useSubTransactionTypes(transactionTypeId: string | undefined) {
  const lang = useLanguage();

  return useQuery({
    queryKey: queryKeys.subTransactionTypes(transactionTypeId ?? 'none', lang),
    queryFn: () =>
      apiClient.get<SubTransactionTypeDto[]>(`transaction-types/${transactionTypeId}/sub-types`, {
        language: lang,
      }),
    enabled: Boolean(transactionTypeId),
  });
}

export function useAuthorities(subTransactionTypeId: string | undefined) {
  const lang = useLanguage();

  return useQuery({
    queryKey: queryKeys.authorities(subTransactionTypeId ?? 'none', lang),
    queryFn: () => apiClient.get<AuthorityDto[]>(`sub-types/${subTransactionTypeId}/authorities`, {
        language: lang,
      }),
    enabled: Boolean(subTransactionTypeId),
  });
}

export function useServiceTypes(
  authorityId: string | undefined,
  subTransactionTypeId: string | undefined,
) {
  const lang = useLanguage();

  return useQuery({
    queryKey: queryKeys.serviceTypes(authorityId ?? 'none', subTransactionTypeId ?? 'none', lang),
    queryFn: () =>
      apiClient.get<ServiceTypeDto[]>(`authorities/${authorityId}/service-types`, {
        language: lang,
        query: { subTransactionTypeId },
      }),
    enabled: Boolean(authorityId && subTransactionTypeId),
  });
}

// ------------------------------------------------------------ applications

export function useApplication(applicationId: string | undefined) {
  return useQuery({
    queryKey: queryKeys.application(applicationId ?? 'none'),
    queryFn: () => apiClient.get<ApplicationDetailsDto>(`applications/${applicationId}`),
    enabled: Boolean(applicationId),
  });
}

export function useCreateApplication() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (model: ApplicationWriteModel) =>
      apiClient.post<ApplicationDetailsDto>('applications', model),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['applications'] }),
  });
}

export function useUpdateApplication(applicationId: string | undefined) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (model: ApplicationWriteModel) =>
      apiClient.put<ApplicationDetailsDto>(`applications/${applicationId}`, model),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['applications'] }),
  });
}

export function useSubmitApplication() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (applicationId: string) =>
      apiClient.post<ApplicationDetailsDto>(`applications/${applicationId}/submit`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['applications'] }),
  });
}

export interface UploadFileArgs {
  applicationId: string;
  applicationServiceId: string;
  requiredFileId: string;
  file: File;
}

export function useUploadApplicationFile() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({ applicationId, applicationServiceId, requiredFileId, file }: UploadFileArgs) => {
      const form = new FormData();
      form.append('file', file);
      form.append('applicationServiceId', applicationServiceId);
      form.append('requiredFileId', requiredFileId);

      return apiClient.upload<ApplicationFileDto>(`applications/${applicationId}/files`, form);
    },
    // The required-file checklist is computed server-side, so it is refetched rather than
    // patched locally — that keeps the gating condition identical to the one submit enforces.
    onSuccess: (_data, variables) =>
      queryClient.invalidateQueries({ queryKey: queryKeys.application(variables.applicationId) }),
  });
}

/** Saves every answer to the required documents custom fields in one call. */
export function useSaveDocumentValues() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      applicationId,
      values,
    }: {
      applicationId: string;
      values: Array<{ applicationServiceId: string; requiredFileFieldId: string; value: string }>;
    }) =>
      apiClient.put<ApplicationDetailsDto>(`applications/${applicationId}/document-values`, {
        values,
      }),
    onSuccess: (_data, variables) =>
      queryClient.invalidateQueries({ queryKey: queryKeys.application(variables.applicationId) }),
  });
}
