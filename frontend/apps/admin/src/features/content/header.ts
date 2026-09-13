import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/shared/api/client';
import type { Translations } from './api';

/** Who a header entry is shown to. The API speaks names in both directions. */
export type HeaderLinkVisibility = 'Everyone' | 'SignedIn' | 'SignedOut';

export const HEADER_VISIBILITIES: readonly HeaderLinkVisibility[] = [
  'Everyone',
  'SignedIn',
  'SignedOut',
];

/** The entries the site ships with. A row with one of these keys is labelled by the web app. */
export const BUILT_IN_KEYS = ['home', 'how', 'services', 'coverage', 'knowledge', 'contact'] as const;

export interface HeaderLinkDto {
  id: string;
  /** A built-in entry's key, or null for one an operator added. */
  key: string | null;
  /** Label overrides by language. Usually empty: the web app has its own wording. */
  labels: Translations;
  url: string;
  visibility: HeaderLinkVisibility;
  sortOrder: number;
  isActive: boolean;
}

export interface HeaderContentDto {
  links: HeaderLinkDto[];
}

export interface UpsertHeaderLinkBody {
  id?: string;
  key: string | null;
  labels?: Translations;
  url: string;
  visibility: HeaderLinkVisibility;
  sortOrder: number;
  isActive: boolean;
}

const headerKey = ['admin', 'content', 'header'] as const;

export function useAdminHeaderContent() {
  return useQuery({
    queryKey: headerKey,
    queryFn: () => apiClient.get<HeaderContentDto>('admin/content/header'),
  });
}

function useInvalidateHeader() {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: headerKey });
}

export function useSaveHeaderLink() {
  const invalidate = useInvalidateHeader();

  return useMutation({
    mutationFn: (body: UpsertHeaderLinkBody) =>
      apiClient.post<HeaderLinkDto>('admin/content/header/links', body),
    onSuccess: invalidate,
  });
}

export function useDeleteHeaderLink() {
  const invalidate = useInvalidateHeader();

  return useMutation({
    mutationFn: (id: string) => apiClient.delete<void>(`admin/content/header/links/${id}`),
    onSuccess: invalidate,
  });
}
