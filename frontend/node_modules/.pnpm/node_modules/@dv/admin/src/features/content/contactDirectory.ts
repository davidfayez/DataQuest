import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type { ListParams } from '@/features/lookups/api';
import { apiClient } from '@/shared/api/client';
import { useLanguage } from '@/shared/lib/useLanguage';
import type { PagedResult } from '@/shared/ui/DataTable';

/** Mirrors the API's ContactEntryKind — the two groups the public contact page shows. */
export enum ContactEntryKind {
  AuthorizedAgent = 0,
  Administration = 1,
}

export interface ContactDirectoryEntryDto {
  id: string;
  kind: ContactEntryKind;
  kindName: string;
  countryId: string | null;
  countryName: string | null;
  titleAr: string | null;
  titleEn: string | null;
  addressAr: string | null;
  addressEn: string | null;
  phone: string | null;
  email: string | null;
  isActive: boolean;
  sortOrder: number;
  /** False when the entry carries no phone, email or address — the page never shows those. */
  isReachable: boolean;
}

export interface UpsertContactDirectoryEntryInput {
  id?: string | null;
  kind: ContactEntryKind;
  countryId: string | null;
  titleAr: string | null;
  titleEn: string | null;
  addressAr: string | null;
  addressEn: string | null;
  phone: string | null;
  email: string | null;
  isActive: boolean;
  sortOrder: number;
}

export interface ContactDirectoryParams extends ListParams {
  kind?: ContactEntryKind;
}

const BASE = 'admin/contact-directory';

function toQuery(params: object): string {
  const search = new URLSearchParams();

  for (const [key, value] of Object.entries(params)) {
    if (value !== undefined && value !== null && value !== '') {
      search.set(key, String(value));
    }
  }

  return search.toString();
}

export function useContactDirectory(params: ContactDirectoryParams) {
  const language = useLanguage();

  return useQuery({
    queryKey: ['admin', 'contact-directory', params, language],
    queryFn: () =>
      apiClient.get<PagedResult<ContactDirectoryEntryDto>>(`${BASE}?${toQuery(params)}`),
  });
}

function useInvalidate() {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: ['admin', 'contact-directory'] });
}

export function useSaveContactDirectoryEntry() {
  const invalidate = useInvalidate();

  return useMutation({
    mutationFn: (input: UpsertContactDirectoryEntryInput) =>
      apiClient.post<ContactDirectoryEntryDto>(BASE, input),
    onSuccess: invalidate,
  });
}

export function useDeleteContactDirectoryEntry() {
  const invalidate = useInvalidate();

  return useMutation({
    mutationFn: (id: string) => apiClient.delete<void>(`${BASE}/${id}`),
    onSuccess: invalidate,
  });
}
