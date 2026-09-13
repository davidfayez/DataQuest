import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { apiClient } from '@/shared/api/client';
import type { Translations } from './api';

/** How a country is marked on the map. The API speaks names in both directions. */
export type CoverageMarker = 'Spot' | 'Flag';

export const COVERAGE_MARKERS: readonly CoverageMarker[] = ['Spot', 'Flag'];

/** One country on the coverage map. The name is the country lookup's own, not a copy. */
export interface CoverageEntryDto {
  id: string;
  countryId: string;
  /** ISO 3166-1 alpha-2 — what places the spot on the site's map. */
  code: string;
  name: string;
  marker: CoverageMarker;
  sortOrder: number;
  isPublished: boolean;
}

export interface CoverageContentDto {
  title: Translations;
  subtitle: Translations;
  countries: CoverageEntryDto[];
}

export interface UpsertCoverageEntryBody {
  id?: string;
  countryId: string;
  marker: CoverageMarker;
  sortOrder: number;
  isPublished: boolean;
}

/** Both fields optional; omitting one leaves the stored value alone. */
export interface UpdateCoverageHeadingBody {
  title?: Translations;
  subtitle?: Translations;
}

const coverageKey = ['admin', 'content', 'coverage'] as const;

export function useAdminCoverageContent() {
  return useQuery({
    queryKey: coverageKey,
    queryFn: () => apiClient.get<CoverageContentDto>('admin/content/coverage'),
  });
}

function useInvalidateCoverage() {
  const queryClient = useQueryClient();
  return () => queryClient.invalidateQueries({ queryKey: coverageKey });
}

export function useSaveCoverageEntry() {
  const invalidate = useInvalidateCoverage();

  return useMutation({
    mutationFn: (body: UpsertCoverageEntryBody) =>
      apiClient.post<CoverageEntryDto>('admin/content/coverage/countries', body),
    onSuccess: invalidate,
  });
}

export function useDeleteCoverageEntry() {
  const invalidate = useInvalidateCoverage();

  return useMutation({
    mutationFn: (id: string) =>
      apiClient.delete<void>(`admin/content/coverage/countries/${id}`),
    onSuccess: invalidate,
  });
}

export function useUpdateCoverageHeading() {
  const invalidate = useInvalidateCoverage();

  return useMutation({
    mutationFn: (body: UpdateCoverageHeadingBody) =>
      apiClient.put<void>('admin/content/coverage/heading', body),
    onSuccess: invalidate,
  });
}
